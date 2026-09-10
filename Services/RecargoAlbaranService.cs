using GRA0150Net.Domain;
using GRA0150Net.Infrastructure.Events;
using System;
using System.Collections.Generic;

namespace GRA0150Net.Services
{
    /// <summary>
    /// Servei responsable de la lògica funcional
    /// dels recàrrecs en albarans de venda.
    ///
    /// Responsabilitats:
    /// - identificar els albarans de venda;
    /// - llegir el percentatge de recàrrec de la capçalera;
    /// - determinar si cal aplicar un recàrrec;
    /// - identificar la línia automàtica de recàrrec;
    /// - calcular la base del recàrrec a partir de BASEMONEDA;
    /// - calcular l'import brut del recàrrec.
    ///
    /// Aquesta classe no crea, modifica ni elimina línies d'a3ERP.
    /// La interacció amb Interop.a3ERPActiveX quedarà encapsulada
    /// posteriorment a la capa Infrastructure.
    /// </summary>
    internal sealed class RecargoAlbaranService
    {
        /// <summary>
        /// Determina si el tipus de document rebut
        /// correspon a un albarà de venda.
        /// </summary>
        /// <param name="tipoDocumento">
        /// Codi de tipus de document proporcionat per a3ERP.
        /// </param>
        /// <returns>
        /// true si el document és un albarà de venda;
        /// false en cas contrari.
        /// </returns>
        public bool EsAlbaranVenta(
            string tipoDocumento)
        {
            return string.Equals(
                tipoDocumento?.Trim(),
                RecargoAlbaranConstants.TipoDocumentoAlbaranVenta,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Indica si a3ERP ha inclòs el camp AT_PORC_RECARGO
        /// dins de la capçalera rebuda a l'esdeveniment.
        ///
        /// Aquesta comprovació permet diferenciar entre:
        /// - camp existent amb valor 0;
        /// - camp absent del payload.
        /// </summary>
        /// <param name="cabecera">
        /// Payload de capçalera proporcionat per a3ERP.
        /// </param>
        /// <returns>
        /// true si el camp és present;
        /// false en cas contrari.
        /// </returns>
        public bool TieneCampoPorcentajeRecargo(
            object cabecera)
        {
            return A3ErpEventDataReader.ContainsField(
                cabecera,
                RecargoAlbaranConstants.CampoPorcentajeRecargo);
        }

        /// <summary>
        /// Llegeix el percentatge de recàrrec informat
        /// a la capçalera de l'albarà.
        ///
        /// El valor s'interpreta directament com a percentatge:
        /// - 0   = sense recàrrec;
        /// - 4   = recàrrec del 4 %;
        /// - 4,5 = recàrrec del 4,5 %.
        ///
        /// Els valors negatius es consideren 0 per evitar
        /// que un valor incorrecte pugui generar accidentalment
        /// un descompte.
        /// </summary>
        /// <param name="cabecera">
        /// Payload de capçalera proporcionat per a3ERP.
        /// </param>
        /// <returns>
        /// Percentatge de recàrrec vàlid.
        /// </returns>
        public decimal ObtenerPorcentajeRecargo(
            object cabecera)
        {
            decimal porcentaje =
                A3ErpEventDataReader.GetDecimal(
                    cabecera,
                    RecargoAlbaranConstants.CampoPorcentajeRecargo,
                    0m);

            if (porcentaje < 0m)
            {
                return 0m;
            }

            return porcentaje;
        }

        /// <summary>
        /// Determina si el percentatge informat requereix
        /// aplicar un recàrrec al document.
        /// </summary>
        /// <param name="porcentaje">
        /// Percentatge informat a la capçalera.
        /// </param>
        /// <returns>
        /// true quan el percentatge és superior a 0.
        /// </returns>
        public bool DebeAplicarRecargo(
            decimal porcentaje)
        {
            return porcentaje > 0m;
        }

        /// <summary>
        /// Determina si una línia correspon a la línia funcional
        /// de recàrrec creada per GRA0150Net.
        ///
        /// Per seguretat no n'hi ha prou amb CODART = 0.
        /// També s'exigeix que DESCLIN sigui el concepte
        /// oficial o comenci pel concepte seguit d'un espai.
        /// </summary>
        /// <param name="linea">
        /// Línia recuperada del payload d'a3ERP.
        /// </param>
        /// <returns>
        /// true si la línia és una línia de recàrrec
        /// gestionada per GRA0150Net.
        /// </returns>
        public bool EsLineaRecargo(
            IDictionary<string, object> linea)
        {
            if (linea == null)
            {
                return false;
            }

            string codigoArticulo =
                A3ErpEventDataReader.GetRowString(
                    linea,
                    "CODART");

            string descripcion =
                A3ErpEventDataReader.GetRowString(
                    linea,
                    "DESCLIN");

            codigoArticulo =
                (codigoArticulo ?? string.Empty).Trim();

            descripcion =
                (descripcion ?? string.Empty).Trim();

            if (!string.Equals(
                codigoArticulo,
                RecargoAlbaranConstants.CodigoArticuloRecargo,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return
                string.Equals(
                    descripcion,
                    RecargoAlbaranConstants.ConceptoRecargo,
                    StringComparison.OrdinalIgnoreCase)
                ||
                descripcion.StartsWith(
                    RecargoAlbaranConstants.ConceptoRecargo + " ",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Calcula la base sobre la qual s'ha d'aplicar
        /// el percentatge de recàrrec.
        ///
        /// La base és la suma de BASEMONEDA de totes
        /// les línies ordinàries de l'albarà.
        ///
        /// BASEMONEDA ha estat validat contra dades reals
        /// d'a3ERP com l'import net de línia després
        /// dels descomptes i en la moneda del document.
        ///
        /// La pròpia línia de recàrrec queda sempre exclosa
        /// per evitar que el recàrrec s'acumuli sobre si mateix
        /// en guardats posteriors.
        ///
        /// També queden exclosos els articles presents
        /// a dbo.AT_ARTICULOS_EXENTOS quan s'informen
        /// mitjançant codigosExentos.
        /// </summary>
        /// <param name="lineas">
        /// Línies recuperades del payload de l'albarà.
        /// </param>
        /// <param name="codigosExentos">
        /// Codis d'article que no han de formar part de la base.
        /// </param>
        /// <returns>
        /// Base total del recàrrec en moneda del document.
        /// </returns>
        public decimal CalcularBaseRecargo(
            IEnumerable<Dictionary<string, object>> lineas)
        {
            return CalcularBaseRecargo(
                lineas,
                null);
        }

        public decimal CalcularBaseRecargo(
            IEnumerable<Dictionary<string, object>> lineas,
            ISet<string> codigosExentos)
        {
            if (lineas == null)
            {
                return 0m;
            }

            decimal baseRecargo = 0m;

            foreach (Dictionary<string, object> linea in lineas)
            {
                if (linea == null)
                {
                    continue;
                }

                /*
                 * La línia automàtica de recàrrec no pot formar
                 * part de la seva pròpia base de càlcul.
                 */
                if (EsLineaRecargo(linea))
                {
                    continue;
                }

                if (EsArticuloExento(
                    linea,
                    codigosExentos))
                {
                    continue;
                }

                decimal baseMoneda =
                    A3ErpEventDataReader.GetRowDecimal(
                        linea,
                        "BASEMONEDA",
                        0m);

                /*
                 * No reconstruïm manualment preus, quantitats
                 * ni descomptes.
                 *
                 * Utilitzem directament BASEMONEDA perquè és
                 * el resultat econòmic calculat per a3ERP.
                 */
                baseRecargo += baseMoneda;
            }

            return baseRecargo;
        }

        /// <summary>
        /// Retorna el nombre de línies ordinàries que formen
        /// part de la base del recàrrec.
        ///
        /// La línia automàtica de recàrrec queda exclosa.
        /// També queden exclosos els articles presents
        /// a dbo.AT_ARTICULOS_EXENTOS.
        ///
        /// Aquest valor s'utilitza principalment per diagnòstic
        /// i traçabilitat al log.
        /// </summary>
        /// <param name="lineas">
        /// Línies recuperades del payload de l'albarà.
        /// </param>
        /// <returns>
        /// Nombre de línies incloses en el càlcul.
        /// </returns>
        public int ContarLineasBaseRecargo(
            IEnumerable<Dictionary<string, object>> lineas)
        {
            return ContarLineasBaseRecargo(
                lineas,
                null);
        }

        public int ContarLineasBaseRecargo(
            IEnumerable<Dictionary<string, object>> lineas,
            ISet<string> codigosExentos)
        {
            if (lineas == null)
            {
                return 0;
            }

            int numeroLineas = 0;

            foreach (Dictionary<string, object> linea in lineas)
            {
                if (linea == null)
                {
                    continue;
                }

                if (EsLineaRecargo(linea))
                {
                    continue;
                }

                if (EsArticuloExento(
                    linea,
                    codigosExentos))
                {
                    continue;
                }

                numeroLineas++;
            }

            return numeroLineas;
        }

        /// <summary>
        /// Retorna el nombre de línies excloses de la base
        /// perquè el seu CODART consta a AT_ARTICULOS_EXENTOS.
        /// </summary>
        public int ContarLineasExcluidasPorArticulosExentos(
            IEnumerable<Dictionary<string, object>> lineas,
            ISet<string> codigosExentos)
        {
            if (lineas == null)
            {
                return 0;
            }

            int numeroLineas = 0;

            foreach (Dictionary<string, object> linea in lineas)
            {
                if (linea == null ||
                    EsLineaRecargo(linea))
                {
                    continue;
                }

                if (EsArticuloExento(
                    linea,
                    codigosExentos))
                {
                    numeroLineas++;
                }
            }

            return numeroLineas;
        }

        /// <summary>
        /// Retorna la suma de BASEMONEDA exclosa de la base
        /// perquè el CODART consta a AT_ARTICULOS_EXENTOS.
        /// </summary>
        public decimal CalcularBaseExcluidaPorArticulosExentos(
            IEnumerable<Dictionary<string, object>> lineas,
            ISet<string> codigosExentos)
        {
            if (lineas == null)
            {
                return 0m;
            }

            decimal baseExcluida = 0m;

            foreach (Dictionary<string, object> linea in lineas)
            {
                if (linea == null ||
                    EsLineaRecargo(linea))
                {
                    continue;
                }

                if (!EsArticuloExento(
                    linea,
                    codigosExentos))
                {
                    continue;
                }

                baseExcluida +=
                    A3ErpEventDataReader.GetRowDecimal(
                        linea,
                        "BASEMONEDA",
                        0m);
            }

            return baseExcluida;
        }

        private static bool EsArticuloExento(
            IDictionary<string, object> linea,
            ISet<string> codigosExentos)
        {
            if (linea == null ||
                codigosExentos == null ||
                codigosExentos.Count == 0)
            {
                return false;
            }

            string codigoArticulo =
                A3ErpEventDataReader.GetRowString(
                    linea,
                    "CODART");

            codigoArticulo =
                (codigoArticulo ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(
                codigoArticulo))
            {
                return false;
            }

            return codigosExentos.Contains(
                codigoArticulo);
        }

        /// <summary>
        /// Calcula l'import brut del recàrrec abans
        /// d'aplicar l'arrodoniment configurat a a3ERP.
        ///
        /// Fórmula:
        ///
        /// import = base × percentatge / 100
        ///
        /// IMPORTANT:
        /// aquest mètode no arrodoneix el resultat.
        /// L'arrodoniment final es farà posteriorment
        /// segons la configuració de decimals d'a3ERP.
        /// </summary>
        /// <param name="baseRecargo">
        /// Base neta sobre la qual s'aplica el percentatge.
        /// </param>
        /// <param name="porcentajeRecargo">
        /// Percentatge informat a AT_PORC_RECARGO.
        /// </param>
        /// <returns>
        /// Import calculat del recàrrec sense arrodoniment final.
        /// </returns>
        public decimal CalcularImporteRecargo(
            decimal baseRecargo,
            decimal porcentajeRecargo)
        {
            if (porcentajeRecargo <= 0m)
            {
                return 0m;
            }

            return
                baseRecargo
                * porcentajeRecargo
                / 100m;
        }

        /// <summary>
        /// Arrodoneix l'import del recàrrec segons el nombre
        /// de decimals configurats a a3ERP.
        ///
        /// S'utilitza arrodoniment comercial: els valors exactament
        /// situats al punt mig s'arrodoneixen allunyant-se de zero.
        /// </summary>
        /// <param name="importeRecargo">
        /// Import calculat abans de l'arrodoniment final.
        /// </param>
        /// <param name="numeroDecimales">
        /// Nombre de decimals configurats a DATOSCONFIG.NUMDECPRC.
        /// </param>
        /// <returns>
        /// Import preparat per informar a la línia d'a3ERP.
        /// </returns>
        public decimal RedondearImporteRecargo(
            decimal importeRecargo,
            int numeroDecimales)
        {
            if (numeroDecimales < 0)
            {
                numeroDecimales = 0;
            }

            if (numeroDecimales > 10)
            {
                numeroDecimales = 10;
            }

            return Math.Round(
                importeRecargo,
                numeroDecimales,
                MidpointRounding.AwayFromZero);
        }
    }
}
