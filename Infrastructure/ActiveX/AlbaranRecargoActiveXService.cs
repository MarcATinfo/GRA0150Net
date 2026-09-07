using System;
using a3ERPActiveX;
using GRA0150Net.Domain;

namespace GRA0150Net.Infrastructure.ActiveX
{
    /// <summary>
    /// Encapsula les operacions sobre albarans de venda
    /// realitzades mitjançant Interop.a3ERPActiveX.
    ///
    /// Aquesta classe és l'únic punt del projecte que ha de
    /// conèixer els detalls tècnics de l'objecte IAlbaran.
    ///
    /// No s'utilitza SQL per crear, modificar o eliminar
    /// línies de documents d'a3ERP.
    /// </summary>
    internal sealed class AlbaranRecargoActiveXService
    {
        /// <summary>
        /// Obre un albarà de venda existent mitjançant ActiveX
        /// i comprova que l'objecte de negoci és accessible.
        ///
        /// Operació exclusivament de diagnòstic.
        /// No guarda cap modificació.
        /// </summary>
        /// <param name="idAlbaran">
        /// Identificador intern IDALBV de l'albarà.
        /// </param>
        public void ValidarAccesoAlbaranVenta(
            decimal idAlbaran)
        {
            if (idAlbaran <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(idAlbaran),
                    "L'identificador de l'albarà ha de ser superior a zero.");
            }

            IAlbaran albaran = null;
            bool iniciado = false;
            bool abiertoEnEdicion = false;

            try
            {
                albaran =
                    new Albaran();

                albaran.Iniciar();
                iniciado = true;

                /*
                 * false = albarà de venda.
                 */
                albaran.Modifica(
                    idAlbaran,
                    false);

                abiertoEnEdicion = true;
            }
            finally
            {
                /*
                 * Aquesta operació és només de diagnòstic.
                 * Cancel·lem explícitament qualsevol edició oberta.
                 */
                if (albaran != null &&
                    abiertoEnEdicion)
                {
                    try
                    {
                        albaran.Cancela();
                    }
                    catch
                    {
                        /*
                         * No ocultem una possible excepció principal
                         * per un error secundari durant la cancel·lació.
                         */
                    }
                }

                if (albaran != null &&
                    iniciado)
                {
                    try
                    {
                        albaran.Acabar();
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Afegeix una línia de recàrrec a un albarà
        /// de venda existent mitjançant ActiveX.
        ///
        /// La línia es crea utilitzant:
        /// - CODART = 0;
        /// - UNIDADES = 1;
        /// - DESCLIN = RECÀRREC;
        /// - PRCMONEDA = import calculat.
        ///
        /// El document es guarda mitjançant Anade(),
        /// que és el patró validat en altres integracions a3ERP
        /// per modificar albarans existents.
        /// </summary>
        /// <param name="idAlbaran">
        /// Identificador intern IDALBV.
        /// </param>
        /// <param name="importeRecargo">
        /// Import final del recàrrec ja calculat i arrodonit.
        /// </param>
        public void AgregarLineaRecargo(
            decimal idAlbaran,
            decimal importeRecargo)
        {
            if (idAlbaran <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(idAlbaran),
                    "L'identificador de l'albarà ha de ser superior a zero.");
            }

            if (importeRecargo <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(importeRecargo),
                    "L'import del recàrrec ha de ser superior a zero.");
            }

            IAlbaran albaran = null;

            bool iniciado = false;
            bool abiertoEnEdicion = false;
            bool guardado = false;

            try
            {
                /*
                 * Treballem sempre mitjançant la interfície IAlbaran.
                 */
                albaran =
                    new Albaran();

                albaran.Iniciar();
                iniciado = true;

                /*
                 * Evitem diàlegs interactius durant
                 * una operació automàtica de la DLL.
                 */
                albaran.OmitirMensajes = true;

                /*
                 * El preu del recàrrec és un import calculat
                 * per GRA0150Net i no s'ha de substituir
                 * pel preu habitual de l'article 0.
                 */
                albaran.ValidarPrecios = false;

                /*
                 * Evitem que una possible configuració de bloqueig
                 * de l'article tècnic impedeixi l'operació automàtica.
                 */
                albaran.ValidarArtBloqueado = false;

                albaran.AvisarRiesgo = false;

                /*
                 * false indica albarà de venda.
                 */
                albaran.Modifica(
                    idAlbaran,
                    false);

                abiertoEnEdicion = true;

                /*
                 * Creem una nova línia d'article.
                 *
                 * NuevaLineaArt ja informa el codi d'article
                 * i les unitats inicials.
                 */
                albaran.NuevaLineaArt(
                    RecargoAlbaranConstants.CodigoArticuloRecargo,
                    RecargoAlbaranConstants.UnidadesRecargo);

                /*
                 * DESCLIN és el nostre identificador funcional
                 * estable de la línia de recàrrec.
                 *
                 * No hi posem el percentatge perquè aquest
                 * continua guardat a AT_PORC_RECARGO.
                 */
                albaran.AsStringLin["DESCLIN"] =
                    RecargoAlbaranConstants.ConceptoRecargo;

                /*
                 * Reafirmem explícitament les unitats.
                 */
                albaran.AsCurrencyLin["UNIDADES"] =
                    Convert.ToDecimal(
                        RecargoAlbaranConstants.UnidadesRecargo);

                /*
                 * PRCMONEDA és el camp validat en integracions
                 * existents per informar el preu de línia
                 * en la moneda del document.
                 *
                 * Com que UNIDADES = 1, el preu coincideix
                 * amb l'import total del recàrrec.
                 */
                albaran.AsCurrencyLin["PRCMONEDA"] =
                    importeRecargo;

                /*
                 * Confirmem la línia dins del document.
                 */
                albaran.AnadirLinea();

                /*
                 * No cridem CalcularImpuestosyTotales().
                 *
                 * En els patrons validats d'albarans existents,
                 * el document es confirma directament amb Anade().
                 */
                albaran.Anade();

                guardado = true;
                abiertoEnEdicion = false;
            }
            finally
            {
                /*
                 * Si alguna operació falla abans del guardat,
                 * cancel·lem l'edició del document.
                 */
                if (albaran != null &&
                    abiertoEnEdicion &&
                    !guardado)
                {
                    try
                    {
                        albaran.Cancela();
                    }
                    catch
                    {
                    }
                }

                /*
                 * Finalitzem sempre l'objecte ActiveX
                 * si s'ha inicialitzat.
                 */
                if (albaran != null &&
                    iniciado)
                {
                    try
                    {
                        albaran.Acabar();
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Actualitza l'import d'una línia de recàrrec existent
        /// d'un albarà de venda mitjançant Interop.a3ERPActiveX.
        ///
        /// La línia s'identifica pel seu NUMLINALB, que és
        /// l'identificador utilitzat per EditarLinea().
        ///
        /// El document es guarda mitjançant Anade(),
        /// seguint el patró validat en integracions a3ERP existents.
        /// </summary>
        /// <param name="idAlbaran">
        /// Identificador intern IDALBV de l'albarà.
        /// </param>
        /// <param name="numeroLineaAlbaran">
        /// Valor NUMLINALB de la línia RECÀRREC.
        /// </param>
        /// <param name="importeRecargo">
        /// Nou import final del recàrrec.
        /// </param>
        public void ActualizarLineaRecargo(
            decimal idAlbaran,
            decimal numeroLineaAlbaran,
            decimal importeRecargo)
        {
            if (idAlbaran <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(idAlbaran),
                    "L'identificador de l'albarà ha de ser superior a zero.");
            }

            if (numeroLineaAlbaran <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(numeroLineaAlbaran),
                    "El número de línia de l'albarà ha de ser superior a zero.");
            }

            if (importeRecargo <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(importeRecargo),
                    "L'import del recàrrec ha de ser superior a zero.");
            }

            IAlbaran albaran = null;

            bool iniciado = false;
            bool abiertoEnEdicion = false;
            bool guardado = false;

            try
            {
                albaran =
                    new Albaran();

                albaran.Iniciar();
                iniciado = true;

                /*
                 * Evitem intervenció interactiva de l'usuari
                 * durant l'operació automàtica.
                 */
                albaran.OmitirMensajes = true;
                albaran.ValidarPrecios = false;
                albaran.ValidarArtBloqueado = false;
                albaran.AvisarRiesgo = false;

                /*
                 * false = albarà de venda.
                 */
                albaran.Modifica(
                    idAlbaran,
                    false);

                abiertoEnEdicion = true;

                /*
                 * EditarLinea treballa amb NUMLINALB.
                 *
                 * No utilitzem IDLIN ni la posició física
                 * de la línia dins del payload.
                 */
                albaran.EditarLinea(
                    numeroLineaAlbaran);

                /*
                 * Reafirmem el concepte reservat.
                 *
                 * Això garanteix que la línia continua
                 * identificant-se funcionalment com RECÀRREC.
                 */
                albaran.AsStringLin["DESCLIN"] =
                    RecargoAlbaranConstants.ConceptoRecargo;

                /*
                 * Mantenim una unitat perquè PRCMONEDA
                 * coincideixi directament amb l'import
                 * total del recàrrec.
                 */
                albaran.AsCurrencyLin["UNIDADES"] =
                    Convert.ToDecimal(
                        RecargoAlbaranConstants.UnidadesRecargo);

                /*
                 * Nou import calculat en moneda del document.
                 */
                albaran.AsCurrencyLin["PRCMONEDA"] =
                    importeRecargo;

                /*
                 * Confirmem els canvis de la línia.
                 */
                albaran.AnadirLinea();

                /*
                 * Confirmem la modificació del document.
                 */
                albaran.Anade();

                guardado = true;
                abiertoEnEdicion = false;
            }
            finally
            {
                /*
                 * Si l'operació falla abans del guardat,
                 * cancel·lem l'edició oberta.
                 */
                if (albaran != null &&
                    abiertoEnEdicion &&
                    !guardado)
                {
                    try
                    {
                        albaran.Cancela();
                    }
                    catch
                    {
                    }
                }

                /*
                 * Finalitzem sempre l'objecte ActiveX.
                 */
                if (albaran != null &&
                    iniciado)
                {
                    try
                    {
                        albaran.Acabar();
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Elimina una línia de recàrrec existent
        /// d'un albarà de venda mitjançant Interop.a3ERPActiveX.
        ///
        /// La línia s'identifica pel seu NUMLINALB,
        /// el mateix identificador funcional utilitzat
        /// per EditarLinea().
        ///
        /// Després de l'eliminació, el document es confirma
        /// mitjançant Anade().
        /// </summary>
        /// <param name="idAlbaran">
        /// Identificador intern IDALBV.
        /// </param>
        /// <param name="numeroLineaAlbaran">
        /// NUMLINALB de la línia RECARGO.
        /// </param>
        public void EliminarLineaRecargo(
            decimal idAlbaran,
            decimal numeroLineaAlbaran)
        {
            if (idAlbaran <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(idAlbaran),
                    "L'identificador de l'albarà ha de ser superior a zero.");
            }

            if (numeroLineaAlbaran <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(numeroLineaAlbaran),
                    "El número de línia de l'albarà ha de ser superior a zero.");
            }

            IAlbaran albaran = null;

            bool iniciado = false;
            bool abiertoEnEdicion = false;
            bool guardado = false;

            try
            {
                albaran =
                    new Albaran();

                albaran.Iniciar();
                iniciado = true;

                albaran.OmitirMensajes = true;
                albaran.ValidarPrecios = false;
                albaran.ValidarArtBloqueado = false;
                albaran.AvisarRiesgo = false;

                /*
                 * false = albarà de venda.
                 */
                albaran.Modifica(
                    idAlbaran,
                    false);

                abiertoEnEdicion = true;

                /*
                 * BorrarLinea forma part de la mateixa API
                 * de gestió de línies que EditarLinea.
                 *
                 * Utilitzem NUMLINALB, que és el número
                 * de línia real del document.
                 */
                albaran.BorrarLinea(
                    numeroLineaAlbaran);

                /*
                 * Confirmem la modificació del document.
                 */
                albaran.Anade();

                guardado = true;
                abiertoEnEdicion = false;
            }
            finally
            {
                /*
                 * Si l'eliminació o el guardat fallen,
                 * cancel·lem l'edició del document.
                 */
                if (albaran != null &&
                    abiertoEnEdicion &&
                    !guardado)
                {
                    try
                    {
                        albaran.Cancela();
                    }
                    catch
                    {
                    }
                }

                if (albaran != null &&
                    iniciado)
                {
                    try
                    {
                        albaran.Acabar();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}