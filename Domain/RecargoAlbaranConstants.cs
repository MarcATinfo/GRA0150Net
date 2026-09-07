namespace GRA0150Net.Domain
{
    /// <summary>
    /// Constants funcionals utilitzades pel procés
    /// de recàrrec dels albarans de venda.
    /// </summary>
    internal static class RecargoAlbaranConstants
    {
        /// <summary>
        /// Identificador de document utilitzat per a3ERP
        /// per als albarans de venda.
        /// </summary>
        public const string TipoDocumentoAlbaranVenta = "AV";

        /// <summary>
        /// Camp personalitzat de capçalera que conté
        /// el percentatge de recàrrec.
        /// </summary>
        public const string CampoPorcentajeRecargo =
            "AT_PORC_RECARGO";

        /// <summary>
        /// Article tècnic utilitzat per crear
        /// la línia de recàrrec.
        /// </summary>
        public const string CodigoArticuloRecargo = "0";

        /// <summary>
        /// Concepte oficial actual de la línia de recàrrec.
        /// </summary>
        public const string ConceptoRecargo = "RECARGO";

        /// <summary>
        /// Concepte utilitzat en les primeres versions
        /// de GRA0150Net.
        ///
        /// Es manté temporalment per poder reconèixer
        /// i actualitzar/eliminar línies creades anteriorment.
        /// </summary>
        public const string ConceptoRecargoLegacy = "RECÀRREC";

        /// <summary>
        /// Unitats utilitzades per la línia de recàrrec.
        ///
        /// En ser 1, PRCMONEDA coincideix directament
        /// amb l'import total del recàrrec.
        /// </summary>
        public const double UnidadesRecargo = 1d;
    }
}