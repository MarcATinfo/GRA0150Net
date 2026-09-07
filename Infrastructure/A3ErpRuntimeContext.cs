using System;
using System.Data.Common;

namespace GRA0150Net.Infrastructure.Runtime
{
    /// <summary>
    /// Manté el context de connexions proporcionat per a3ERP
    /// mentre l'empresa activa continua oberta.
    ///
    /// a3ERP proporciona dues connexions: una corresponent
    /// a la base de dades de sistema i una altra corresponent
    /// a l'empresa activa.
    ///
    /// No es confia en l'ordre dels paràmetres rebuts.
    /// Les connexions s'identifiquen mitjançant el nom
    /// de la base de dades.
    /// </summary>
    internal sealed class A3ErpRuntimeContext
    {
        /// <summary>
        /// Nom habitual de la base de dades de sistema d'a3ERP.
        /// </summary>
        private const string BaseDatosSistemaA3Erp =
            "A3ERP$SISTEMA";

        /// <summary>
        /// Cadena de connexió OLE DB corresponent
        /// a l'empresa activa.
        /// </summary>
        public string ConexionEmpresa { get; private set; }

        /// <summary>
        /// Cadena de connexió OLE DB corresponent
        /// a la base de dades de sistema d'a3ERP.
        /// </summary>
        public string ConexionSistema { get; private set; }

        /// <summary>
        /// Nom de la base de dades de l'empresa activa.
        /// </summary>
        public string BaseDatosEmpresa { get; private set; }

        /// <summary>
        /// Nom de la base de dades de sistema d'a3ERP.
        /// </summary>
        public string BaseDatosSistema { get; private set; }

        /// <summary>
        /// Indica si les dues connexions han arribat
        /// en ordre invers al que declara la signatura
        /// del mètode Iniciar d'a3ERP.
        /// </summary>
        public bool ConexionesInvertidasDetectadas { get; private set; }

        /// <summary>
        /// Indica si disposem d'un context vàlid
        /// per a l'empresa activa.
        /// </summary>
        public bool TieneEmpresaInicializada
        {
            get
            {
                return
                    !string.IsNullOrWhiteSpace(ConexionEmpresa)
                    &&
                    !string.IsNullOrWhiteSpace(BaseDatosEmpresa);
            }
        }

        /// <summary>
        /// Inicialitza el context buit.
        /// </summary>
        public A3ErpRuntimeContext()
        {
            Limpiar();
        }

        /// <summary>
        /// Rep les dues connexions proporcionades per a3ERP
        /// i determina quina correspon al sistema
        /// i quina correspon a l'empresa activa.
        ///
        /// No es confia en l'ordre dels paràmetres.
        /// La identificació es realitza a partir del nom
        /// de la base de dades contingut a cada connexió.
        /// </summary>
        /// <param name="conexionPrimera">
        /// Primera connexió proporcionada per a3ERP.
        /// </param>
        /// <param name="conexionSegunda">
        /// Segona connexió proporcionada per a3ERP.
        /// </param>
        public void Inicializar(
            string conexionPrimera,
            string conexionSegunda)
        {
            Limpiar();

            if (string.IsNullOrWhiteSpace(
                conexionPrimera))
            {
                throw new ArgumentException(
                    "La primera connexió rebuda d'a3ERP és buida.",
                    nameof(conexionPrimera));
            }

            if (string.IsNullOrWhiteSpace(
                conexionSegunda))
            {
                throw new ArgumentException(
                    "La segona connexió rebuda d'a3ERP és buida.",
                    nameof(conexionSegunda));
            }

            string baseDatosPrimera =
                ObtenerBaseDatos(
                    conexionPrimera);

            string baseDatosSegunda =
                ObtenerBaseDatos(
                    conexionSegunda);

            bool primeraEsSistema =
                EsBaseDatosSistema(
                    baseDatosPrimera);

            bool segundaEsSistema =
                EsBaseDatosSistema(
                    baseDatosSegunda);

            if (primeraEsSistema &&
                !segundaEsSistema)
            {
                /*
                 * Ordre habitual:
                 *
                 * primera connexió = sistema
                 * segona connexió  = empresa
                 */
                ConexionSistema =
                    conexionPrimera;

                BaseDatosSistema =
                    baseDatosPrimera;

                ConexionEmpresa =
                    conexionSegunda;

                BaseDatosEmpresa =
                    baseDatosSegunda;

                ConexionesInvertidasDetectadas =
                    false;
            }
            else if (!primeraEsSistema &&
                     segundaEsSistema)
            {
                /*
                 * Les connexions han arribat invertides.
                 */
                ConexionSistema =
                    conexionSegunda;

                BaseDatosSistema =
                    baseDatosSegunda;

                ConexionEmpresa =
                    conexionPrimera;

                BaseDatosEmpresa =
                    baseDatosPrimera;

                ConexionesInvertidasDetectadas =
                    true;
            }
            else
            {
                /*
                 * No podem continuar de manera segura si
                 * no sabem quina connexió correspon a l'empresa.
                 */
                throw new InvalidOperationException(
                    "No s'han pogut distingir les connexions "
                    + "de sistema i empresa rebudes des d'a3ERP. "
                    + "Base de dades primera: '"
                    + baseDatosPrimera
                    + "'. Base de dades segona: '"
                    + baseDatosSegunda
                    + "'.");
            }

            if (string.IsNullOrWhiteSpace(
                BaseDatosEmpresa))
            {
                throw new InvalidOperationException(
                    "No s'ha pogut identificar la base de dades "
                    + "de l'empresa activa.");
            }
        }

        /// <summary>
        /// Elimina qualsevol informació conservada
        /// de l'empresa anterior.
        /// </summary>
        public void Limpiar()
        {
            ConexionEmpresa =
                string.Empty;

            ConexionSistema =
                string.Empty;

            BaseDatosEmpresa =
                string.Empty;

            BaseDatosSistema =
                string.Empty;

            ConexionesInvertidasDetectadas =
                false;
        }

        /// <summary>
        /// Extreu el nom de la base de dades
        /// d'una cadena de connexió.
        ///
        /// S'admeten les claus equivalents
        /// Initial Catalog i Database.
        /// </summary>
        private static string ObtenerBaseDatos(
            string cadenaConexion)
        {
            DbConnectionStringBuilder builder =
                new DbConnectionStringBuilder
                {
                    ConnectionString =
                        cadenaConexion
                };

            string baseDatos =
                ObtenerValor(
                    builder,
                    "Initial Catalog",
                    "Database");

            return baseDatos.Trim();
        }

        /// <summary>
        /// Determina si el nom rebut correspon
        /// a una base de dades de sistema d'a3ERP.
        /// </summary>
        private static bool EsBaseDatosSistema(
            string baseDatos)
        {
            if (string.IsNullOrWhiteSpace(
                baseDatos))
            {
                return false;
            }

            string valorNormalizado =
                baseDatos.Trim();

            return
                string.Equals(
                    valorNormalizado,
                    BaseDatosSistemaA3Erp,
                    StringComparison.OrdinalIgnoreCase)
                ||
                valorNormalizado.EndsWith(
                    "$SISTEMA",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Retorna el primer valor informat
        /// entre diverses claus equivalents
        /// d'una cadena de connexió.
        /// </summary>
        private static string ObtenerValor(
            DbConnectionStringBuilder builder,
            params string[] claves)
        {
            foreach (string clave in claves)
            {
                if (!builder.ContainsKey(
                    clave))
                {
                    continue;
                }

                object valor =
                    builder[clave];

                if (valor == null)
                {
                    continue;
                }

                string texto =
                    Convert.ToString(
                        valor);

                if (!string.IsNullOrWhiteSpace(
                    texto))
                {
                    return texto.Trim();
                }
            }

            return string.Empty;
        }
    }
}