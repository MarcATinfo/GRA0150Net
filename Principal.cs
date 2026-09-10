using GRA0150Net.Domain;
using GRA0150Net.Infrastructure.ActiveX;
using GRA0150Net.Infrastructure.Connections;
using GRA0150Net.Infrastructure.Configuration;
using GRA0150Net.Infrastructure.Events;
using GRA0150Net.Infrastructure.Logging;
using GRA0150Net.Infrastructure.Runtime;
using GRA0150Net.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GRA0150Net
{
    /// <summary>
    /// Classe COM principal de GRA0150Net.
    ///
    /// a3ERP instancia aquesta classe per notificar els esdeveniments
    /// relacionats amb els documents de gestió.
    ///
    /// Responsabilitats:
    /// - rebre i identificar les connexions proporcionades per a3ERP;
    /// - mantenir el context de l'empresa activa;
    /// - identificar els esdeveniments d'albarans de venda;
    /// - calcular el recàrrec corresponent;
    /// - decidir si cal crear, actualitzar o eliminar la línia RECARGO;
    /// - recuperar configuració general d'a3ERP;
    /// - executar les modificacions mitjançant a3ERPActiveX;
    /// - evitar execucions recursives provocades per operacions ActiveX;
    /// - sol·licitar a a3ERP la recàrrega visual del document;
    /// - informar d'errors que puguin impedir el guardat;
    /// - registrar operacions i incidències rellevants;
    /// - netejar l'estat temporal quan es tanca l'empresa.
    ///
    /// La lògica funcional específica es delega als serveis
    /// especialitzats del projecte sempre que sigui possible.
    /// </summary>
    [ComVisible(true)]
    [Guid("6F41C93F-65F0-46D2-AE03-A5B4FC5EA150")]
    [ProgId("GRA0150Net.Principal")]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class Principal
    {
        /*
         * Estats de document observats i validats
         * en els esdeveniments d'a3ERP.
         */
        private const int EstadoDocumentoAlta = 0;
        private const int EstadoDocumentoModificacion = 1;
        private const int EstadoDocumentoEliminacion = 2;

        /// <summary>
        /// Context mantingut durant la sessió
        /// de l'empresa activa.
        ///
        /// Identifica de manera segura quina connexió
        /// correspon a l'empresa i quina al sistema d'a3ERP.
        /// </summary>
        private readonly A3ErpRuntimeContext _runtimeContext =
            new A3ErpRuntimeContext();

        /// <summary>
        /// Servei responsable de la lògica funcional
        /// relacionada amb el recàrrec dels albarans de venda.
        /// </summary>
        private readonly RecargoAlbaranService _recargoAlbaranService =
            new RecargoAlbaranService();

        /// <summary>
        /// Repositori utilitzat per recuperar configuració general
        /// de l'empresa activa d'a3ERP.
        /// </summary>
        private readonly A3ErpConfiguracionRepository _configuracionRepository =
            new A3ErpConfiguracionRepository();

        /// <summary>
        /// Servei que obté la connexió viva d'a3ERP
        /// per a lectures auxiliars amb ADODB.
        /// </summary>
        private readonly A3ErpConexionActivaService _conexionActivaService =
            new A3ErpConexionActivaService();

        /// <summary>
        /// Servei responsable de les operacions sobre albarans
        /// realitzades mitjançant Interop.a3ERPActiveX.
        /// </summary>
        private readonly AlbaranRecargoActiveXService _albaranActiveXService =
            new AlbaranRecargoActiveXService();

        /// <summary>
        /// Manté temporalment les operacions de recàrrec
        /// calculades entre l'esdeveniment anterior
        /// i posterior al guardat.
        ///
        /// També permet gestionar les altes noves
        /// que arriben al BeforeSave amb IdDoc=0.
        /// </summary>
        private readonly RecargoAlbaranPendingState _recargoPendingState =
            new RecargoAlbaranPendingState();

        /// <summary>
        /// Motiu de l'últim error produït durant un esdeveniment
        /// que pugui impedir el guardat del document.
        /// </summary>
        private string _ultimoMotivo = string.Empty;

        /// <summary>
        /// Indica que GRA0150Net ha modificat un albarà de venda
        /// mitjançant ActiveX després del guardat original
        /// i que el formulari d'a3ERP ha de recarregar-se.
        ///
        /// Només es retorna true a Repintar quan realment
        /// s'ha realitzat una modificació ActiveX correcta.
        /// </summary>
        private bool _repintarAlbaranVentaPendiente;

        /// <summary>
        /// Retorna a a3ERP els procediments públics
        /// disponibles en aquesta DLL.
        /// </summary>
        public object[] ListaProcedimientos()
        {
            return new object[]
            {
                "Iniciar",
                "Finalizar",
                "AntesDeGuardarDocumentoV2",
                "DespuesDeGuardarDocumentoV2",
                "Repintar",
                "UltimoMotivo"
            };
        }

        /// <summary>
        /// Rep les dues connexions proporcionades per a3ERP
        /// quan l'usuari entra en una empresa.
        ///
        /// Inicialitza primer el log local de reserva,
        /// identifica la connexió d'empresa i posteriorment
        /// intenta aplicar la configuració del log principal
        /// definida a dbo.AT_GRA0150NET_CONFIG.
        /// </summary>
        public void Iniciar(
            string conexionSistema,
            string conexionEmpresa)
        {
            /*
             * El fallback ha d'existir abans
             * de qualsevol lectura SQL.
             *
             * Si falla la inicialització, la connexió
             * o la configuració, disposarem igualment
             * d'un log local.
             */
            GRA0150Logger.InicialitzarLogReserva();

            GRA0150Logger.Informacio(
                "S'inicia GRA0150Net.");

            try
            {
                _recargoPendingState.Limpiar();
                _repintarAlbaranVentaPendiente = false;

                /*
                 * Primer identifiquem correctament
                 * la connexió d'empresa.
                 */
                _runtimeContext.Inicializar(
                    conexionSistema,
                    conexionEmpresa);

                _conexionActivaService.Inicializar(
                    _runtimeContext.BaseDatosEmpresa);

                _ultimoMotivo =
                    string.Empty;

                /*
                 * Recuperem la configuració SQL
                 * del log del procés de recàrrec.
                 */
                bool logActivo;
                string rutaLog;

                bool configuracionLogDisponible =
                    _configuracionRepository
                        .TryObtenerConfiguracionLogRecargo(
                            _conexionActivaService.ConexionEmpresa,
                            _runtimeContext.ConexionEmpresa,
                            out logActivo,
                            out rutaLog);

                if (configuracionLogDisponible)
                {
                    GRA0150Logger.Configurar(
                        logActivo,
                        rutaLog);
                }
                else
                {
                    /*
                     * No desactivem el fallback.
                     *
                     * Una configuració absent o incorrecta
                     * no ha de deixar GRA0150Net sense log.
                     */
                    GRA0150Logger.Advertencia(
                        "No s'ha pogut recuperar la configuració "
                        + "del log des de AT_GRA0150NET_CONFIG. "
                        + "Es manté el log local de reserva.");
                }

                /*
                 * No registrem mai les cadenes
                 * de connexió completes.
                 */
                GRA0150Logger.Informacio(
                    "El context d'a3ERP s'ha inicialitzat correctament.",
                    "BaseDatosEmpresa="
                        + _runtimeContext.BaseDatosEmpresa,
                    "BaseDatosSistema="
                        + _runtimeContext.BaseDatosSistema,
                    "ConexionesInvertidas="
                        + _runtimeContext.ConexionesInvertidasDetectadas);
            }
            catch (Exception ex)
            {
                _recargoPendingState.Limpiar();
                _repintarAlbaranVentaPendiente = false;

                _conexionActivaService.Finalizar();

                _runtimeContext.Limpiar();
                _ultimoMotivo = string.Empty;

                /*
                 * ErrorCritic continua intentant
                 * escriure al fallback encara que
                 * Recargo_LogActivo sigui False.
                 */
                GRA0150Logger.ErrorCritic(
                    "S'ha produït un error durant la inicialització "
                    + "del context de GRA0150Net.",
                    ex);
            }
        }

        /// <summary>
        /// Neteja el context mantingut per la DLL
        /// quan a3ERP tanca l'empresa activa.
        /// </summary>
        public void Finalizar()
        {
            try
            {
                GRA0150Logger.Informacio(
                    "Es finalitza GRA0150Net.");

                _conexionActivaService.Finalizar();

                _recargoPendingState.Limpiar();

                _runtimeContext.Limpiar();

                _ultimoMotivo = string.Empty;
                _repintarAlbaranVentaPendiente = false;
            }
            catch (Exception ex)
            {
                /*
                 * Finalizar no ha de propagar excepcions COM
                 * ni impedir el tancament correcte de l'empresa.
                 */
                GRA0150Logger.ErrorCritic(
                    "S'ha produït un error durant la finalització "
                    + "de GRA0150Net.",
                    ex);
            }
        }

        /// <summary>
        /// Esdeveniment executat per a3ERP abans
        /// de guardar un document.
        ///
        /// GRA0150Net només actua sobre albarans de venda.
        ///
        /// Aquest mètode:
        /// - comprova AT_PORC_RECARGO;
        /// - recupera les línies del document;
        /// - identifica una possible línia RECARGO;
        /// - calcula la base mitjançant BASEMONEDA;
        /// - exclou RECARGO de la seva pròpia base;
        /// - recupera DATOSCONFIG.NUMDECPRC;
        /// - calcula l'import final;
        /// - decideix si cal crear, actualitzar o eliminar;
        /// - conserva temporalment l'operació fins a l'AfterSave.
        ///
        /// En una alta nova IdDoc pot ser 0.
        /// En aquest cas es conserva temporalment
        /// la creació fins que l'AfterSave proporcioni
        /// l'IDALBV definitiu.
        /// </summary>
        public bool AntesDeGuardarDocumentoV2(
            string documento,
            double idDoc,
            object cabecera,
            object lineas,
            int estado)
        {
            try
            {
                /*
                 * Ignorem guardats generats per les nostres
                 * pròpies operacions ActiveX.
                 */
                if (A3ErpEventExecutionGuard.EstaActivo)
                {
                    GRA0150Logger.Debug(
                        "S'ignora l'esdeveniment anterior al guardat "
                        + "perquè correspon a una operació interna "
                        + "de GRA0150Net.",
                        "Documento="
                            + Convert.ToString(documento),
                        "IdDoc="
                            + idDoc.ToString(
                                CultureInfo.InvariantCulture));

                    return true;
                }

                _ultimoMotivo = string.Empty;

                /*
                 * Només treballem amb albarans de venda.
                 */
                if (!_recargoAlbaranService.EsAlbaranVenta(
                    documento))
                {
                    return true;
                }

                /*
                 * L'eliminació d'un document no requereix
                 * cap processament funcional del recàrrec.
                 *
                 * En aquest estat a3ERP tampoc no proporciona
                 * necessàriament capçalera ni línies completes.
                 */
                if (estado == EstadoDocumentoEliminacion)
                {
                    decimal idAlbaranEliminado =
                        Convert.ToDecimal(idDoc);

                    if (idAlbaranEliminado > 0m)
                    {
                        _recargoPendingState.Eliminar(
                            idAlbaranEliminado);
                    }

                    _recargoPendingState.EliminarAltaPendiente();
                    _repintarAlbaranVentaPendiente = false;

                    GRA0150Logger.Debug(
                        "S'ignora l'eliminació d'un albarà de venda.",
                        "IdDoc="
                            + idDoc.ToString(
                                CultureInfo.InvariantCulture));

                    return true;
                }

                /*
                 * Una nova operació iniciada per l'usuari
                 * invalida qualsevol petició de refresc anterior
                 * que no hagués estat consumida.
                 */
                _repintarAlbaranVentaPendiente = false;

                /*
                 * També eliminem una possible alta pendent antiga.
                 *
                 * Una alta pendent vàlida només ha d'existir
                 * entre el BeforeSave i l'AfterSave corresponents.
                 */
                _recargoPendingState.EliminarAltaPendiente();

                decimal idAlbaran =
                    Convert.ToDecimal(idDoc);

                bool tieneCampoPorcentaje =
                    _recargoAlbaranService
                        .TieneCampoPorcentajeRecargo(
                            cabecera);

                decimal porcentajeRecargo =
                    _recargoAlbaranService
                        .ObtenerPorcentajeRecargo(
                            cabecera);

                GRA0150Logger.Informacio(
                    "S'ha rebut un albarà de venda abans del guardat.",
                    "IdDoc="
                        + idDoc.ToString(
                            CultureInfo.InvariantCulture),
                    "Estado="
                        + estado.ToString(
                            CultureInfo.InvariantCulture),
                    "CampoPorcentajePresente="
                        + tieneCampoPorcentaje,
                    "PorcentajeRecargo="
                        + porcentajeRecargo.ToString(
                            CultureInfo.InvariantCulture));

                /*
                 * Recuperem les línies actuals
                 * proporcionades per a3ERP.
                 */
                List<Dictionary<string, object>> lineasDetectadas =
                    A3ErpEventDataReader.GetRows(
                        lineas);

                GRA0150Logger.Debug(
                    "S'han recuperat les línies de l'albarà.",
                    "IdDoc="
                        + idDoc.ToString(
                            CultureInfo.InvariantCulture),
                    "NumeroLineas="
                        + lineasDetectadas.Count.ToString(
                            CultureInfo.InvariantCulture));

                /*
                 * Localitzem la possible línia de recàrrec.
                 *
                 * EsLineaRecargo reconeix:
                 * - CODART = 0;
                 * - DESCLIN = RECARGO o començant per RECARGO
                 *   seguit d'un espai.
                 */
                bool existeLineaRecargo = false;
                decimal numeroLineaRecargo = 0m;

                foreach (Dictionary<string, object> linea
                    in lineasDetectadas)
                {
                    if (!_recargoAlbaranService.EsLineaRecargo(
                        linea))
                    {
                        continue;
                    }

                    existeLineaRecargo = true;

                    numeroLineaRecargo =
                        A3ErpEventDataReader.GetRowDecimal(
                            linea,
                            "NUMLINALB");

                    object idLin =
                        A3ErpEventDataReader.GetRowValue(
                            linea,
                            "IDLIN");

                    string descripcionActual =
                        A3ErpEventDataReader.GetRowString(
                            linea,
                            "DESCLIN");

                    GRA0150Logger.Debug(
                        "S'ha identificat la línia de recàrrec existent.",
                        "IdDoc="
                            + idDoc.ToString(
                                CultureInfo.InvariantCulture),
                        "IDLIN="
                            + Convert.ToString(
                                idLin,
                                CultureInfo.InvariantCulture),
                        "NUMLINALB="
                            + numeroLineaRecargo.ToString(
                                CultureInfo.InvariantCulture),
                        "DESCLIN="
                            + descripcionActual);

                    break;
                }

                GRA0150Logger.Debug(
                    "S'ha comprovat l'existència de la línia de recàrrec.",
                    "IdDoc="
                        + idDoc.ToString(
                            CultureInfo.InvariantCulture),
                    "ExisteLineaRecargo="
                        + existeLineaRecargo);

                /*
                 * Sense el camp personalitzat
                 * no podem aplicar la funcionalitat.
                 *
                 * No bloquegem el guardat normal d'a3ERP.
                 */
                if (!tieneCampoPorcentaje)
                {
                    if (idAlbaran > 0m)
                    {
                        _recargoPendingState.Eliminar(
                            idAlbaran);
                    }

                    GRA0150Logger.Advertencia(
                        "AT_PORC_RECARGO no s'ha trobat a la capçalera "
                        + "rebuda des d'a3ERP.",
                        "IdDoc="
                            + idDoc.ToString(
                                CultureInfo.InvariantCulture));

                    return true;
                }

                /*
                 * AT_PORC_RECARGO = 0.
                 *
                 * Si no existeix RECARGO, no hi ha res a fer.
                 * Si existeix, programem la seva eliminació.
                 */
                if (!_recargoAlbaranService.DebeAplicarRecargo(
                    porcentajeRecargo))
                {
                    if (!existeLineaRecargo)
                    {
                        if (idAlbaran > 0m)
                        {
                            _recargoPendingState.Eliminar(
                                idAlbaran);
                        }

                        GRA0150Logger.Debug(
                            "L'albarà no requereix recàrrec "
                            + "i no existeix cap línia per eliminar.",
                            "IdDoc="
                                + idDoc.ToString(
                                    CultureInfo.InvariantCulture));

                        return true;
                    }

                    /*
                     * Si estem en una alta sense ID o no tenim
                     * NUMLINALB, no podem eliminar amb seguretat.
                     */
                    if (idAlbaran <= 0m ||
                        numeroLineaRecargo <= 0m)
                    {
                        if (idAlbaran > 0m)
                        {
                            _recargoPendingState.Eliminar(
                                idAlbaran);
                        }

                        GRA0150Logger.Advertencia(
                            "Existeix una línia de recàrrec però "
                            + "no s'ha pogut preparar la seva eliminació.",
                            "IdDoc="
                                + idDoc.ToString(
                                    CultureInfo.InvariantCulture),
                            "NUMLINALB="
                                + numeroLineaRecargo.ToString(
                                    CultureInfo.InvariantCulture));

                        return true;
                    }

                    _recargoPendingState.EstablecerEliminacion(
                        idAlbaran,
                        numeroLineaRecargo);

                    GRA0150Logger.Informacio(
                        "S'ha registrat l'eliminació pendent "
                        + "de la línia de recàrrec.",
                        "IdAlbaran="
                            + idAlbaran.ToString(
                                CultureInfo.InvariantCulture),
                        "NUMLINALB="
                            + numeroLineaRecargo.ToString(
                                CultureInfo.InvariantCulture));

                    return true;
                }

                /*
                 * A partir d'aquí el percentatge és > 0.
                 */
                HashSet<string> codigosArticulosExentos =
                    _configuracionRepository
                        .ObtenerCodigosArticulosExentos(
                            _conexionActivaService.ConexionEmpresa,
                            _runtimeContext.ConexionEmpresa);

                decimal baseRecargo =
                    _recargoAlbaranService.CalcularBaseRecargo(
                        lineasDetectadas,
                        codigosArticulosExentos);

                int numeroLineasBase =
                    _recargoAlbaranService.ContarLineasBaseRecargo(
                        lineasDetectadas,
                        codigosArticulosExentos);

                int numeroLineasExcluidas =
                    _recargoAlbaranService
                        .ContarLineasExcluidasPorArticulosExentos(
                            lineasDetectadas,
                            codigosArticulosExentos);

                decimal baseExcluida =
                    _recargoAlbaranService
                        .CalcularBaseExcluidaPorArticulosExentos(
                            lineasDetectadas,
                            codigosArticulosExentos);

                decimal importeRecargo =
                    _recargoAlbaranService.CalcularImporteRecargo(
                        baseRecargo,
                        porcentajeRecargo);

                int numeroDecimalesPrecio;

                bool configuracionDecimalesDisponible =
                    _configuracionRepository
                        .TryObtenerNumeroDecimalesPrecio(
                            _conexionActivaService.ConexionEmpresa,
                            _runtimeContext.ConexionEmpresa,
                            out numeroDecimalesPrecio);

                decimal importeRecargoFinal =
                    _recargoAlbaranService.RedondearImporteRecargo(
                        importeRecargo,
                        numeroDecimalesPrecio);

                GRA0150Logger.Informacio(
                    "S'ha calculat el recàrrec de l'albarà.",
                    "IdDoc="
                        + idDoc.ToString(
                            CultureInfo.InvariantCulture),
                    "NumeroLineasBase="
                        + numeroLineasBase.ToString(
                            CultureInfo.InvariantCulture),
                    "NumeroLineasExcluidas="
                        + numeroLineasExcluidas.ToString(
                            CultureInfo.InvariantCulture),
                    "BaseExcluida="
                        + baseExcluida.ToString(
                            CultureInfo.InvariantCulture),
                    "BaseRecargo="
                        + baseRecargo.ToString(
                            CultureInfo.InvariantCulture),
                    "PorcentajeRecargo="
                        + porcentajeRecargo.ToString(
                            CultureInfo.InvariantCulture),
                    "ImporteCalculado="
                        + importeRecargo.ToString(
                            CultureInfo.InvariantCulture),
                    "NUMDECPRC="
                        + numeroDecimalesPrecio.ToString(
                            CultureInfo.InvariantCulture),
                    "ConfiguracionDecimalesDisponible="
                        + configuracionDecimalesDisponible,
                    "ImporteFinal="
                        + importeRecargoFinal.ToString(
                            CultureInfo.InvariantCulture));

                if (!configuracionDecimalesDisponible)
                {
                    GRA0150Logger.Advertencia(
                        "No s'ha pogut recuperar DATOSCONFIG.NUMDECPRC. "
                        + "S'utilitza el valor de decimals de reserva.",
                        "IdDoc="
                            + idDoc.ToString(
                                CultureInfo.InvariantCulture),
                        "DecimalesReserva="
                            + numeroDecimalesPrecio.ToString(
                                CultureInfo.InvariantCulture));
                }

                /*
                 * Un percentatge > 0 no implica necessàriament
                 * un import final > 0.
                 *
                 * Pot passar si:
                 * - totes les línies tenen base zero;
                 * - l'import arrodonit acaba sent zero.
                 */
                if (importeRecargoFinal <= 0m)
                {
                    /*
                     * Si no existeix RECARGO,
                     * simplement no fem res.
                     */
                    if (!existeLineaRecargo)
                    {
                        if (idAlbaran > 0m)
                        {
                            _recargoPendingState.Eliminar(
                                idAlbaran);
                        }

                        GRA0150Logger.Debug(
                            "L'import calculat del recàrrec és zero. "
                            + "No es crearà cap línia de recàrrec.",
                            "IdAlbaran="
                                + idAlbaran.ToString(
                                    CultureInfo.InvariantCulture),
                            "BaseRecargo="
                                + baseRecargo.ToString(
                                    CultureInfo.InvariantCulture),
                            "PorcentajeRecargo="
                                + porcentajeRecargo.ToString(
                                    CultureInfo.InvariantCulture),
                            "ImporteRecargo="
                                + importeRecargoFinal.ToString(
                                    CultureInfo.InvariantCulture));

                        return true;
                    }

                    /*
                     * Si existeix RECARGO però el nou import
                     * calculat és zero, eliminem la línia
                     * perquè no quedi un import anterior obsolet.
                     */
                    if (idAlbaran > 0m &&
                        numeroLineaRecargo > 0m)
                    {
                        _recargoPendingState.EstablecerEliminacion(
                            idAlbaran,
                            numeroLineaRecargo);

                        GRA0150Logger.Informacio(
                            "L'import calculat del recàrrec és zero. "
                            + "S'ha registrat l'eliminació pendent "
                            + "de la línia de recàrrec existent.",
                            "IdAlbaran="
                                + idAlbaran.ToString(
                                    CultureInfo.InvariantCulture),
                            "NUMLINALB="
                                + numeroLineaRecargo.ToString(
                                    CultureInfo.InvariantCulture));

                        return true;
                    }

                    /*
                     * Existeix una línia però no disposem
                     * de prou informació per eliminar-la.
                     */
                    if (idAlbaran > 0m)
                    {
                        _recargoPendingState.Eliminar(
                            idAlbaran);
                    }

                    GRA0150Logger.Advertencia(
                        "L'import calculat del recàrrec és zero "
                        + "i existeix una línia de recàrrec, "
                        + "però no s'ha pogut preparar la seva eliminació.",
                        "IdAlbaran="
                            + idAlbaran.ToString(
                                CultureInfo.InvariantCulture),
                        "NUMLINALB="
                            + numeroLineaRecargo.ToString(
                                CultureInfo.InvariantCulture));

                    return true;
                }

                /*
                 * ALTA NOVA.
                 *
                 * En una alta:
                 * BeforeSave -> IdDoc=0
                 * AfterSave  -> IDALBV definitiu
                 *
                 * Conservem temporalment l'import
                 * fins que arribi l'AfterSave.
                 */
                if (idAlbaran <= 0m)
                {
                    /*
                     * Si ja existís manualment una línia
                     * identificada com RECARGO, no en creem una altra.
                     */
                    if (existeLineaRecargo)
                    {
                        GRA0150Logger.Advertencia(
                            "L'albarà nou ja conté una línia identificada "
                            + "com a RECARGO. No es programarà cap nova creació.");

                        return true;
                    }

                    _recargoPendingState.EstablecerCreacionAltaPendiente(
                        importeRecargoFinal,
                        porcentajeRecargo);

                    GRA0150Logger.Informacio(
                        "S'ha registrat la creació pendent del recàrrec "
                        + "per a un albarà nou encara sense ID definitiu.",
                        "ImporteRecargo="
                            + importeRecargoFinal.ToString(
                                CultureInfo.InvariantCulture),
                        "PorcentajeRecargo="
                            + porcentajeRecargo.ToString(
                                CultureInfo.InvariantCulture));

                    return true;
                }

                /*
                 * EXISTEIX RECARGO:
                 * programem una actualització.
                 */
                if (existeLineaRecargo)
                {
                    if (numeroLineaRecargo <= 0m)
                    {
                        _recargoPendingState.Eliminar(
                            idAlbaran);

                        GRA0150Logger.Advertencia(
                            "S'ha detectat una línia de recàrrec "
                            + "però no s'ha recuperat NUMLINALB.",
                            "IdAlbaran="
                                + idAlbaran.ToString(
                                    CultureInfo.InvariantCulture));

                        return true;
                    }

                    _recargoPendingState.EstablecerActualizacion(
                        idAlbaran,
                        numeroLineaRecargo,
                        importeRecargoFinal,
                        porcentajeRecargo);

                    GRA0150Logger.Informacio(
                        "S'ha registrat l'actualització pendent "
                        + "de la línia de recàrrec.",
                        "IdAlbaran="
                            + idAlbaran.ToString(
                                CultureInfo.InvariantCulture),
                        "NUMLINALB="
                            + numeroLineaRecargo.ToString(
                                CultureInfo.InvariantCulture),
                        "ImporteRecargo="
                            + importeRecargoFinal.ToString(
                                CultureInfo.InvariantCulture),
                        "PorcentajeRecargo="
                            + porcentajeRecargo.ToString(
                                CultureInfo.InvariantCulture));

                    return true;
                }

                /*
                 * NO EXISTEIX RECARGO:
                 * programem una creació.
                 */
                _recargoPendingState.EstablecerCreacion(
                    idAlbaran,
                    importeRecargoFinal,
                    porcentajeRecargo);

                GRA0150Logger.Informacio(
                    "S'ha registrat la creació pendent "
                    + "de la línia de recàrrec.",
                    "IdAlbaran="
                        + idAlbaran.ToString(
                            CultureInfo.InvariantCulture),
                    "ImporteRecargo="
                        + importeRecargoFinal.ToString(
                            CultureInfo.InvariantCulture),
                    "PorcentajeRecargo="
                        + porcentajeRecargo.ToString(
                            CultureInfo.InvariantCulture));

                return true;
            }
            catch (Exception ex)
            {
                /*
                 * Eliminem qualsevol estat temporal
                 * que pogués haver quedat pendent.
                 */
                try
                {
                    decimal idAlbaran =
                        Convert.ToDecimal(idDoc);

                    if (idAlbaran > 0m)
                    {
                        _recargoPendingState.Eliminar(
                            idAlbaran);
                    }

                    _recargoPendingState.EliminarAltaPendiente();
                    _repintarAlbaranVentaPendiente = false;
                }
                catch
                {
                }

                _ultimoMotivo =
                    "Error de GRA0150Net abans de guardar "
                    + "l'albarà de venda: "
                    + ex.Message;

                GRA0150Logger.Error(
                    "S'ha produït un error abans de guardar "
                    + "l'albarà de venda.",
                    ex,
                    "Documento="
                        + Convert.ToString(documento),
                    "IdDoc="
                        + idDoc.ToString(
                            CultureInfo.InvariantCulture),
                    "Estado="
                        + estado.ToString(
                            CultureInfo.InvariantCulture));

                return false;
            }
        }

        /// <summary>
        /// Esdeveniment executat per a3ERP després
        /// de guardar correctament un albarà de venda.
        ///
        /// Recupera l'operació que havia quedat pendent
        /// durant el BeforeSave i executa mitjançant ActiveX:
        /// - Crear;
        /// - Actualizar;
        /// - Eliminar.
        ///
        /// En una alta nova associa la creació temporal
        /// amb l'IDALBV definitiu proporcionat per a3ERP.
        ///
        /// Les operacions ActiveX es protegeixen
        /// contra recursivitat mitjançant
        /// A3ErpEventExecutionGuard.
        /// </summary>
        public void DespuesDeGuardarDocumentoV2(
            string documento,
            double idDoc,
            int estado)
        {
            try
            {
                /*
                 * Ignorem els esdeveniments provocats
                 * per les nostres pròpies operacions ActiveX.
                 */
                if (A3ErpEventExecutionGuard.EstaActivo)
                {
                    GRA0150Logger.Debug(
                        "S'ignora l'esdeveniment posterior al guardat "
                        + "perquè correspon a una operació interna "
                        + "de GRA0150Net.",
                        "Documento="
                            + Convert.ToString(documento),
                        "IdDoc="
                            + idDoc.ToString(
                                CultureInfo.InvariantCulture));

                    return;
                }

                if (!_recargoAlbaranService.EsAlbaranVenta(
                    documento))
                {
                    return;
                }

                /*
                 * Després d'eliminar un albarà
                 * no existeix cap operació posterior
                 * que GRA0150Net hagi de realitzar.
                 */
                if (estado == EstadoDocumentoEliminacion)
                {
                    _repintarAlbaranVentaPendiente = false;

                    GRA0150Logger.Debug(
                        "S'ignora l'esdeveniment posterior "
                        + "a l'eliminació d'un albarà de venda.");

                    return;
                }

                decimal idAlbaran =
                    Convert.ToDecimal(idDoc);

                /*
                 * Després d'un guardat correcte hauríem
                 * de disposar sempre d'un IDALBV definitiu.
                 *
                 * No executem mai ActiveX amb un ID no vàlid.
                 */
                if (idAlbaran <= 0m)
                {
                    if (estado == EstadoDocumentoAlta)
                    {
                        _recargoPendingState.EliminarAltaPendiente();
                    }

                    _repintarAlbaranVentaPendiente = false;

                    GRA0150Logger.Advertencia(
                        "L'esdeveniment posterior al guardat "
                        + "no proporciona un IDALBV vàlid. "
                        + "No s'executarà cap operació ActiveX.",
                        "IdDoc="
                            + idDoc.ToString(
                                CultureInfo.InvariantCulture),
                        "Estado="
                            + estado.ToString(
                                CultureInfo.InvariantCulture));

                    return;
                }

                GRA0150Logger.Debug(
                    "S'ha rebut l'esdeveniment posterior al guardat "
                    + "d'un albarà de venda.",
                    "IdDoc="
                        + idDoc.ToString(
                            CultureInfo.InvariantCulture),
                    "Estado="
                        + estado.ToString(
                            CultureInfo.InvariantCulture));

                OperacionRecargoPendiente operacion;

                /*
                 * Primer busquem una operació associada
                 * directament a l'IDALBV.
                 *
                 * Aquest és el cas habitual
                 * de modificació d'un document existent.
                 */
                bool tieneOperacionPendiente =
                    _recargoPendingState.TryConsumir(
                        idAlbaran,
                        out operacion);

                /*
                 * En una alta nova el BeforeSave tenia IdDoc=0.
                 *
                 * L'AfterSave ja arriba amb:
                 * - Estado = Alta;
                 * - IDALBV definitiu.
                 */
                if (!tieneOperacionPendiente &&
                    estado == EstadoDocumentoAlta)
                {
                    tieneOperacionPendiente =
                        _recargoPendingState.TryConsumirAltaPendiente(
                            out operacion);

                    if (tieneOperacionPendiente &&
                        operacion != null)
                    {
                        operacion.IdAlbaran =
                            idAlbaran;

                        GRA0150Logger.Informacio(
                            "S'ha associat el recàrrec pendent "
                            + "d'una alta nova amb l'IDALBV definitiu.",
                            "IdAlbaran="
                                + idAlbaran.ToString(
                                    CultureInfo.InvariantCulture),
                            "ImporteRecargo="
                                + operacion.ImporteRecargo.ToString(
                                    CultureInfo.InvariantCulture),
                            "PorcentajeRecargo="
                                + operacion.PorcentajeRecargo.ToString(
                                    CultureInfo.InvariantCulture));
                    }
                }

                /*
                 * Sense cap operació pendent
                 * no hem de fer res més.
                 */
                if (!tieneOperacionPendiente ||
                    operacion == null)
                {
                    GRA0150Logger.Debug(
                        "L'albarà no té cap operació "
                        + "de recàrrec pendent.",
                        "IdAlbaran="
                            + idAlbaran.ToString(
                                CultureInfo.InvariantCulture),
                        "Estado="
                            + estado.ToString(
                                CultureInfo.InvariantCulture));

                    return;
                }

                GRA0150Logger.Informacio(
                    "S'inicia l'operació ActiveX "
                    + "sobre la línia de recàrrec.",
                    "IdAlbaran="
                        + idAlbaran.ToString(
                            CultureInfo.InvariantCulture),
                    "TipoOperacion="
                        + operacion.TipoOperacion,
                    "NUMLINALB="
                        + operacion.NumeroLineaAlbaran.ToString(
                            CultureInfo.InvariantCulture),
                    "ImporteRecargo="
                        + operacion.ImporteRecargo.ToString(
                            CultureInfo.InvariantCulture),
                    "PorcentajeRecargo="
                        + operacion.PorcentajeRecargo.ToString(
                            CultureInfo.InvariantCulture));

                /*
                 * L'operació ActiveX provoca un nou guardat
                 * intern de l'albarà.
                 *
                 * El guard evita que aquest guardat intern
                 * torni a executar la lògica funcional.
                 */
                using (A3ErpEventExecutionGuard.Entrar())
                {
                    switch (operacion.TipoOperacion)
                    {
                        case TipoOperacionRecargoPendiente.Crear:

                            _albaranActiveXService.AgregarLineaRecargo(
                                idAlbaran,
                                operacion.ImporteRecargo,
                                operacion.PorcentajeRecargo);

                            break;

                        case TipoOperacionRecargoPendiente.Actualizar:

                            _albaranActiveXService.ActualizarLineaRecargo(
                                idAlbaran,
                                operacion.NumeroLineaAlbaran,
                                operacion.ImporteRecargo,
                                operacion.PorcentajeRecargo);

                            break;

                        case TipoOperacionRecargoPendiente.Eliminar:

                            _albaranActiveXService.EliminarLineaRecargo(
                                idAlbaran,
                                operacion.NumeroLineaAlbaran);

                            break;

                        default:

                            throw new InvalidOperationException(
                                "Tipus d'operació de recàrrec "
                                + "no reconegut: "
                                + operacion.TipoOperacion);
                    }
                }

                /*
                 * Només després d'una operació ActiveX
                 * completada correctament sol·licitem
                 * la recàrrega visual del formulari.
                 */
                _repintarAlbaranVentaPendiente = true;

                GRA0150Logger.Informacio(
                    "S'ha completat correctament l'operació ActiveX "
                    + "sobre la línia de recàrrec.",
                    "IdAlbaran="
                        + idAlbaran.ToString(
                            CultureInfo.InvariantCulture),
                    "TipoOperacion="
                        + operacion.TipoOperacion,
                    "NUMLINALB="
                        + operacion.NumeroLineaAlbaran.ToString(
                            CultureInfo.InvariantCulture),
                    "ImporteRecargo="
                        + operacion.ImporteRecargo.ToString(
                            CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                /*
                 * Si l'operació ActiveX falla,
                 * no sol·licitem cap Repintar com si
                 * la modificació hagués estat correcta.
                 */
                _repintarAlbaranVentaPendiente = false;

                GRA0150Logger.Error(
                    "S'ha produït un error durant l'operació ActiveX "
                    + "sobre la línia de recàrrec.",
                    ex,
                    "Documento="
                        + Convert.ToString(documento),
                    "IdDoc="
                        + idDoc.ToString(
                            CultureInfo.InvariantCulture),
                    "Estado="
                        + estado.ToString(
                            CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// Indica a a3ERP si ha de recarregar
        /// l'entitat que acaba de guardar-se.
        ///
        /// GRA0150Net retorna true únicament quan:
        /// - la taula correspon a CABEALBV;
        /// - la DLL acaba de modificar correctament
        ///   l'albarà mitjançant ActiveX.
        ///
        /// Aquesta recàrrega:
        /// - mostra immediatament la línia RECARGO;
        /// - sincronitza el formulari amb la BD;
        /// - evita conflictes de concurrència
        ///   en guardats posteriors.
        /// </summary>
        public bool Repintar(
            string tabla)
        {
            try
            {
                /*
                 * Durant un guardat ActiveX intern
                 * no volem provocar cap refresc visual.
                 */
                if (A3ErpEventExecutionGuard.EstaActivo)
                {
                    return false;
                }

                bool esAlbaranVenta =
                    string.Equals(
                        (tabla ?? string.Empty).Trim(),
                        "CABEALBV",
                        StringComparison.OrdinalIgnoreCase);

                if (!esAlbaranVenta)
                {
                    return false;
                }

                /*
                 * Sense una modificació ActiveX correcta
                 * no hi ha res a recarregar.
                 */
                if (!_repintarAlbaranVentaPendiente)
                {
                    return false;
                }

                /*
                 * Consumim la petició.
                 *
                 * Cada modificació ActiveX genera
                 * un únic refresc del formulari.
                 */
                _repintarAlbaranVentaPendiente = false;

                GRA0150Logger.Informacio(
                    "Es sol·licita a a3ERP la recàrrega "
                    + "del formulari de l'albarà de venda.",
                    "Tabla="
                        + Convert.ToString(tabla));

                return true;
            }
            catch (Exception ex)
            {
                /*
                 * Un problema de refresc visual
                 * no ha de provocar una excepció COM.
                 */
                _repintarAlbaranVentaPendiente = false;

                GRA0150Logger.Error(
                    "S'ha produït un error en determinar "
                    + "si cal repintar l'albarà.",
                    ex,
                    "Tabla="
                        + Convert.ToString(tabla));

                return false;
            }
        }

        /// <summary>
        /// Retorna el motiu de l'últim error
        /// que hagi provocat la cancel·lació
        /// d'un esdeveniment abans del guardat.
        /// </summary>
        public string UltimoMotivo()
        {
            return _ultimoMotivo ?? string.Empty;
        }
    }
}
