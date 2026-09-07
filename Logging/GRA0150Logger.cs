using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace GRA0150Net.Infrastructure.Logging
{
    /// <summary>
    /// Servei estàtic de logging de GRA0150Net.
    ///
    /// Funcionament:
    /// - s'inicialitza sempre un log local de reserva;
    /// - posteriorment es pot activar un log principal configurable;
    /// - els fitxers són diaris;
    /// - la retenció és de 7 dies;
    /// - si falla el log principal, es commuta automàticament
    ///   al log local de reserva;
    /// - cap incidència del logger pot propagar-se cap a a3ERP.
    /// </summary>
    internal static class GRA0150Logger
    {
        private const string NomAplicacio =
            "GRA0150Net";

        private const string NomCarpetaEmpresa =
            "AT_Infoserveis";

        private const string NomCarpetaLogs =
            "Logs";

        private const int DiesRetencio =
            7;

        private static readonly object SyncRoot =
            new object();

        /// <summary>
        /// Ruta utilitzada actualment
        /// pel logger.
        /// </summary>
        private static string _rutaBase =
            string.Empty;

        /// <summary>
        /// Ruta local de reserva.
        /// </summary>
        private static string _rutaReserva =
            string.Empty;

        /// <summary>
        /// Indica si el log funcional
        /// està habilitat.
        /// </summary>
        private static bool _logActiu;

        /// <summary>
        /// Permet registrar errors crítics
        /// encara que el log funcional
        /// estigui desactivat.
        /// </summary>
        private static bool _errorsCriticsActius =
            true;

        /// <summary>
        /// Inicialitza el logger amb la ruta
        /// local de reserva.
        ///
        /// Aquesta operació s'ha de realitzar
        /// abans de llegir configuració SQL.
        /// </summary>
        public static void InicialitzarLogReserva()
        {
            try
            {
                string rutaLocal =
                    ObtenirRutaReservaLocal();

                if (string.IsNullOrWhiteSpace(
                    rutaLocal))
                {
                    return;
                }

                PrepararRuta(
                    rutaLocal);

                lock (SyncRoot)
                {
                    _rutaReserva =
                        rutaLocal;

                    _rutaBase =
                        rutaLocal;

                    _logActiu =
                        true;

                    _errorsCriticsActius =
                        true;
                }

                NetejarLogsAntics(
                    rutaLocal,
                    DiesRetencio);
            }
            catch
            {
                /*
                 * El sistema de log mai no ha
                 * d'impedir el funcionament d'a3ERP.
                 */
            }
        }

        /// <summary>
        /// Configura el log principal.
        ///
        /// Si el log està desactivat, només es mantenen
        /// disponibles els errors crítics al fallback.
        ///
        /// Si la ruta principal no és utilitzable,
        /// es continua automàticament amb el fallback.
        /// </summary>
        public static void Configurar(
            bool actiu,
            string rutaConfigurada)
        {
            /*
             * Garantim l'existència del fallback.
             */
            AsegurarLogReservaInicializado();

            if (!actiu)
            {
                /*
                 * Deixem constància de la desactivació
                 * abans d'apagar el log funcional.
                 */
                Advertencia(
                    "La configuració ha desactivat "
                    + "el log operatiu del procés de recàrrec.");

                lock (SyncRoot)
                {
                    /*
                     * Els errors crítics continuaran
                     * utilitzant el fallback.
                     */
                    if (!string.IsNullOrWhiteSpace(
                        _rutaReserva))
                    {
                        _rutaBase =
                            _rutaReserva;
                    }

                    _logActiu =
                        false;

                    _errorsCriticsActius =
                        true;
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(
                rutaConfigurada))
            {
                Advertencia(
                    "La ruta configurada del log és buida. "
                    + "Es continua utilitzant el log local de reserva.");

                ActivarReserva();

                return;
            }

            string rutaNormalitzada =
                rutaConfigurada.Trim();

            try
            {
                PrepararRuta(
                    rutaNormalitzada);

                /*
                 * Fem una comprovació real d'escriptura.
                 *
                 * Directory.CreateDirectory pot funcionar
                 * però això no garanteix necessàriament
                 * que l'usuari tingui permisos per crear fitxers.
                 */
                ComprovarEscripturaRuta(
                    rutaNormalitzada);

                lock (SyncRoot)
                {
                    _rutaBase =
                        rutaNormalitzada;

                    _logActiu =
                        true;

                    _errorsCriticsActius =
                        true;
                }

                NetejarLogsAntics(
                    rutaNormalitzada,
                    DiesRetencio);

                Informacio(
                    "S'ha aplicat correctament "
                    + "la configuració del log de GRA0150Net.",
                    "Ruta="
                        + rutaNormalitzada);
            }
            catch
            {
                /*
                 * La ruta principal no és operativa.
                 * Tornem al fallback.
                 */
                ActivarReserva();

                Advertencia(
                    "No s'ha pogut utilitzar la ruta configurada "
                    + "del log. Es continua utilitzant "
                    + "el log local de reserva.",
                    "RutaConfigurada="
                        + rutaNormalitzada);
            }
        }

        public static void Debug(
            string missatge,
            params string[] detalls)
        {
            Escriure(
                "DBG",
                missatge,
                null,
                false,
                detalls);
        }

        public static void Informacio(
            string missatge,
            params string[] detalls)
        {
            Escriure(
                "INF",
                missatge,
                null,
                false,
                detalls);
        }

        public static void Advertencia(
            string missatge,
            params string[] detalls)
        {
            Escriure(
                "WRN",
                missatge,
                null,
                false,
                detalls);
        }

        public static void Error(
            string missatge,
            params string[] detalls)
        {
            Escriure(
                "ERR",
                missatge,
                null,
                false,
                detalls);
        }

        public static void Error(
            string missatge,
            Exception excepcio,
            params string[] detalls)
        {
            Escriure(
                "ERR",
                missatge,
                excepcio,
                false,
                detalls);
        }

        /// <summary>
        /// Escriu un error crític encara que
        /// Recargo_LogActivo estigui desactivat.
        ///
        /// En aquest cas s'utilitza el fallback local.
        /// </summary>
        public static void ErrorCritic(
            string missatge,
            Exception excepcio,
            params string[] detalls)
        {
            Escriure(
                "ERR",
                missatge,
                excepcio,
                true,
                detalls);
        }

        /// <summary>
        /// Retorna el fitxer de log
        /// que s'està utilitzant actualment.
        /// </summary>
        public static string ObtenirRutaFitxerActual()
        {
            try
            {
                lock (SyncRoot)
                {
                    if (string.IsNullOrWhiteSpace(
                        _rutaBase))
                    {
                        return string.Empty;
                    }

                    return ConstruirRutaFitxer(
                        _rutaBase,
                        DateTime.Now);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Escriu físicament una entrada.
        ///
        /// Si falla el log principal,
        /// commuta al fallback i torna
        /// a intentar l'escriptura una vegada.
        /// </summary>
        private static void Escriure(
            string nivell,
            string missatge,
            Exception excepcio,
            bool permetreAmbLogDesactivat,
            params string[] detalls)
        {
            try
            {
                string rutaBase;
                string rutaReserva;
                bool potEscriure;

                lock (SyncRoot)
                {
                    potEscriure =
                        _logActiu
                        ||
                        (permetreAmbLogDesactivat
                         && _errorsCriticsActius);

                    if (!potEscriure)
                    {
                        return;
                    }

                    rutaBase =
                        _rutaBase;

                    rutaReserva =
                        _rutaReserva;
                }

                if (string.IsNullOrWhiteSpace(
                    rutaBase))
                {
                    return;
                }

                string registre =
                    ConstruirRegistre(
                        nivell,
                        missatge,
                        excepcio,
                        detalls);

                /*
                 * Primer intent:
                 * ruta principal/actual.
                 */
                if (TryEscriureRegistre(
                    rutaBase,
                    registre))
                {
                    return;
                }

                /*
                 * Si la ruta actual ja era el fallback,
                 * no existeix una segona alternativa.
                 */
                if (string.IsNullOrWhiteSpace(
                    rutaReserva)
                    ||
                    string.Equals(
                        rutaBase,
                        rutaReserva,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                /*
                 * La ruta principal ha fallat.
                 * Activem el fallback per a les següents
                 * operacions.
                 */
                lock (SyncRoot)
                {
                    _rutaBase =
                        rutaReserva;
                }

                /*
                 * Registrem directament la incidència
                 * sense utilitzar Advertencia(),
                 * per evitar recursivitat.
                 */
                string avisoFallback =
                    ConstruirRegistre(
                        "WRN",
                        "Ha fallat l'escriptura al log principal. "
                        + "GRA0150Net passa a utilitzar "
                        + "el log local de reserva.",
                        null,
                        new string[]
                        {
                            "RutaAnterior="
                                + rutaBase
                        });

                TryEscriureRegistre(
                    rutaReserva,
                    avisoFallback);

                /*
                 * Reintentem el registre original
                 * al fallback.
                 */
                TryEscriureRegistre(
                    rutaReserva,
                    registre);
            }
            catch
            {
                /*
                 * Una incidència del logger
                 * no pot afectar mai a3ERP.
                 */
            }
        }

        /// <summary>
        /// Intenta escriure un registre
        /// en una ruta concreta.
        /// </summary>
        private static bool TryEscriureRegistre(
            string rutaBase,
            string registre)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(
                    rutaBase))
                {
                    return false;
                }

                PrepararRuta(
                    rutaBase);

                string rutaFitxer =
                    ConstruirRutaFitxer(
                        rutaBase,
                        DateTime.Now);

                lock (SyncRoot)
                {
                    File.AppendAllText(
                        rutaFitxer,
                        registre
                        + Environment.NewLine,
                        Encoding.UTF8);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Activa la ruta local de reserva
        /// com a ruta de log operativa.
        /// </summary>
        private static void ActivarReserva()
        {
            try
            {
                AsegurarLogReservaInicializado();

                lock (SyncRoot)
                {
                    if (string.IsNullOrWhiteSpace(
                        _rutaReserva))
                    {
                        return;
                    }

                    _rutaBase =
                        _rutaReserva;

                    _logActiu =
                        true;

                    _errorsCriticsActius =
                        true;
                }

                PrepararRuta(
                    _rutaReserva);

                NetejarLogsAntics(
                    _rutaReserva,
                    DiesRetencio);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Inicialitza el fallback només
        /// si encara no existeix.
        /// </summary>
        private static void AsegurarLogReservaInicializado()
        {
            bool necessitaInicialitzar;

            lock (SyncRoot)
            {
                necessitaInicialitzar =
                    string.IsNullOrWhiteSpace(
                        _rutaReserva);
            }

            if (necessitaInicialitzar)
            {
                InicialitzarLogReserva();
            }
        }

        /// <summary>
        /// Comprova que la ruta permet
        /// crear i eliminar físicament un fitxer.
        /// </summary>
        private static void ComprovarEscripturaRuta(
            string ruta)
        {
            string fitxerProva =
                Path.Combine(
                    ruta,
                    ".GRA0150Net_write_test_"
                    + Guid.NewGuid().ToString("N")
                    + ".tmp");

            try
            {
                File.WriteAllText(
                    fitxerProva,
                    "test",
                    Encoding.UTF8);
            }
            finally
            {
                try
                {
                    if (File.Exists(
                        fitxerProva))
                    {
                        File.Delete(
                            fitxerProva);
                    }
                }
                catch
                {
                }
            }
        }

        private static string ConstruirRegistre(
            string nivell,
            string missatge,
            Exception excepcio,
            string[] detalls)
        {
            StringBuilder registre =
                new StringBuilder();

            registre.Append(
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture));

            registre.Append(" [");
            registre.Append(
                NormalitzarText(
                    nivell));
            registre.Append("] ");

            registre.Append(
                NormalitzarText(
                    missatge));

            if (detalls != null)
            {
                foreach (string detall in detalls)
                {
                    string detallNormalitzat =
                        NormalitzarText(
                            detall);

                    if (string.IsNullOrWhiteSpace(
                        detallNormalitzat))
                    {
                        continue;
                    }

                    registre.Append(" | ");
                    registre.Append(
                        detallNormalitzat);
                }
            }

            if (excepcio != null)
            {
                registre.Append(" | ");
                registre.Append(
                    NormalitzarText(
                        excepcio.GetType().Name));

                registre.Append(": ");
                registre.Append(
                    NormalitzarText(
                        excepcio.Message));

                registre.Append(
                    Environment.NewLine);

                registre.Append(
                    excepcio.ToString());
            }

            return registre.ToString();
        }

        /// <summary>
        /// Retorna:
        ///
        /// %LOCALAPPDATA%
        /// \AT_Infoserveis
        /// \GRA0150Net
        /// \Logs
        /// </summary>
        private static string ObtenirRutaReservaLocal()
        {
            try
            {
                string localAppData =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .LocalApplicationData);

                if (string.IsNullOrWhiteSpace(
                    localAppData))
                {
                    return string.Empty;
                }

                return Path.Combine(
                    localAppData,
                    NomCarpetaEmpresa,
                    NomAplicacio,
                    NomCarpetaLogs);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ConstruirRutaFitxer(
            string rutaBase,
            DateTime data)
        {
            string nomFitxer =
                NomAplicacio
                + "_"
                + data.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture)
                + ".log";

            return Path.Combine(
                rutaBase,
                nomFitxer);
        }

        private static void PrepararRuta(
            string ruta)
        {
            if (string.IsNullOrWhiteSpace(
                ruta))
            {
                return;
            }

            Directory.CreateDirectory(
                ruta);
        }

        /// <summary>
        /// Elimina exclusivament fitxers
        /// GRA0150Net_*.log que superen
        /// els 7 dies de retenció.
        ///
        /// No afecta cap altre fitxer
        /// existent a la mateixa carpeta.
        /// </summary>
        private static void NetejarLogsAntics(
            string ruta,
            int diesRetencio)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(
                    ruta)
                    ||
                    !Directory.Exists(
                        ruta))
                {
                    return;
                }

                DateTime limit =
                    DateTime.Now.AddDays(
                        -diesRetencio);

                string patro =
                    NomAplicacio
                    + "_*.log";

                string[] fitxers =
                    Directory.GetFiles(
                        ruta,
                        patro,
                        SearchOption.TopDirectoryOnly);

                foreach (string fitxer in fitxers)
                {
                    try
                    {
                        DateTime dataFitxer =
                            File.GetLastWriteTime(
                                fitxer);

                        if (dataFitxer < limit)
                        {
                            File.Delete(
                                fitxer);
                        }
                    }
                    catch
                    {
                        /*
                         * Un únic fitxer no eliminable
                         * no ha d'aturar la neteja.
                         */
                    }
                }
            }
            catch
            {
            }
        }

        private static string NormalitzarText(
            string text)
        {
            if (string.IsNullOrWhiteSpace(
                text))
            {
                return string.Empty;
            }

            return text
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }
    }
}