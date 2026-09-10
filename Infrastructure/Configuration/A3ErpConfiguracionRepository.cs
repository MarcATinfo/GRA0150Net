using ADODB;
using GRA0150Net.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Data.OleDb;

namespace GRA0150Net.Infrastructure.Configuration
{
    /// <summary>
    /// Repositori de lectura de configuració d'a3ERP
    /// i de la configuració pròpia de GRA0150Net.
    ///
    /// Les lectures intenten utilitzar primer la connexió
    /// viva d'a3ERP retornada per GetConexionDB("EMPRESA").
    /// Si aquesta no està disponible, es conserva el fallback
    /// OleDb amb la cadena rebuda a Iniciar().
    ///
    /// Aquesta classe només realitza lectures.
    /// No modifica documents ni dades funcionals d'a3ERP.
    /// </summary>
    internal sealed class A3ErpConfiguracionRepository
    {
        private const int DecimalesPrecioPorDefecto = 4;

        private const string ClaveRecargoLogActivo =
            "Recargo_LogActivo";

        private const string ClaveRecargoLogRuta =
            "Recargo_LogRuta";

        /// <summary>
        /// Recupera el nombre de decimals configurats
        /// a DATOSCONFIG.NUMDECPRC.
        ///
        /// Si no es pot recuperar la configuració,
        /// retorna el valor de reserva.
        /// </summary>
        public int ObtenerNumeroDecimalesPrecio(
            string conexionEmpresa)
        {
            int numeroDecimales;

            TryObtenerNumeroDecimalesPrecio(
                conexionEmpresa,
                out numeroDecimales);

            return numeroDecimales;
        }

        /// <summary>
        /// Recupera el nombre de decimals intentant primer
        /// la connexió viva d'a3ERP i després la cadena OleDb
        /// rebuda a Iniciar().
        /// </summary>
        public int ObtenerNumeroDecimalesPrecio(
            Connection conexionAdo,
            string conexionOleDbFallback)
        {
            int numeroDecimales;

            TryObtenerNumeroDecimalesPrecio(
                conexionAdo,
                conexionOleDbFallback,
                out numeroDecimales);

            return numeroDecimales;
        }

        /// <summary>
        /// Intenta recuperar DATOSCONFIG.NUMDECPRC via OleDb.
        ///
        /// Retorna false si la lectura falla
        /// i proporciona 4 decimals com a valor de reserva.
        /// </summary>
        public bool TryObtenerNumeroDecimalesPrecio(
            string conexionEmpresa,
            out int numeroDecimales)
        {
            return TryObtenerNumeroDecimalesPrecioOleDb(
                conexionEmpresa,
                out numeroDecimales);
        }

        /// <summary>
        /// Intenta recuperar DATOSCONFIG.NUMDECPRC primer
        /// via ADODB i, si no és possible, via OleDb.
        /// </summary>
        public bool TryObtenerNumeroDecimalesPrecio(
            Connection conexionAdo,
            string conexionOleDbFallback,
            out int numeroDecimales)
        {
            if (TryObtenerNumeroDecimalesPrecioAdo(
                conexionAdo,
                out numeroDecimales))
            {
                return true;
            }

            return TryObtenerNumeroDecimalesPrecioOleDb(
                conexionOleDbFallback,
                out numeroDecimales);
        }

        /// <summary>
        /// Recupera la configuració del log funcional
        /// del procés de recàrrec.
        ///
        /// Les claus es llegeixen de:
        /// dbo.AT_GRA0150NET_CONFIG
        ///
        /// Claus requerides:
        /// - Recargo_LogActivo
        /// - Recargo_LogRuta
        ///
        /// Només es tenen en compte registres ACTIVO = 1.
        /// </summary>
        /// <param name="conexionEmpresa">
        /// Connexió OLE DB de l'empresa activa.
        /// </param>
        /// <param name="logActivo">
        /// Indica si el log funcional està activat.
        /// </param>
        /// <param name="rutaLog">
        /// Ruta principal configurada.
        /// </param>
        /// <returns>
        /// true si s'han recuperat i interpretat
        /// correctament les dues claus;
        /// false en cas contrari.
        /// </returns>
        public bool TryObtenerConfiguracionLogRecargo(
            string conexionEmpresa,
            out bool logActivo,
            out string rutaLog)
        {
            return TryObtenerConfiguracionLogRecargoOleDb(
                conexionEmpresa,
                out logActivo,
                out rutaLog);
        }

        /// <summary>
        /// Recupera la configuració del log intentant primer
        /// la connexió viva d'a3ERP i després el fallback OleDb.
        /// </summary>
        public bool TryObtenerConfiguracionLogRecargo(
            Connection conexionAdo,
            string conexionOleDbFallback,
            out bool logActivo,
            out string rutaLog)
        {
            if (TryObtenerConfiguracionLogRecargoAdo(
                conexionAdo,
                out logActivo,
                out rutaLog))
            {
                return true;
            }

            return TryObtenerConfiguracionLogRecargoOleDb(
                conexionOleDbFallback,
                out logActivo,
                out rutaLog);
        }

        /// <summary>
        /// Recupera els codis d'article exempts de formar
        /// part de la base del recàrrec.
        ///
        /// La lectura intenta primer la connexió viva d'a3ERP
        /// i després el fallback OleDb. Si totes dues vies fallen,
        /// retorna una llista buida perquè el guardat de l'albarà
        /// no quedi bloquejat.
        /// </summary>
        public HashSet<string> ObtenerCodigosArticulosExentos(
            Connection conexionAdo,
            string conexionOleDbFallback)
        {
            HashSet<string> codigos =
                CrearConjuntoCodigosArticulos();

            if (TryObtenerCodigosArticulosExentosAdo(
                conexionAdo,
                codigos))
            {
                return codigos;
            }

            codigos =
                CrearConjuntoCodigosArticulos();

            if (TryObtenerCodigosArticulosExentosOleDb(
                conexionOleDbFallback,
                codigos))
            {
                return codigos;
            }

            GRA0150Logger.Advertencia(
                "No s'ha pogut llegir AT_ARTICULOS_EXENTOS "
                + "via ADODB ni OleDb. "
                + "Es continua sense exclusions addicionals.",
                "Operacion=ArticulosExentos");

            return CrearConjuntoCodigosArticulos();
        }

        private static bool TryObtenerNumeroDecimalesPrecioAdo(
            Connection conexionAdo,
            out int numeroDecimales)
        {
            numeroDecimales =
                DecimalesPrecioPorDefecto;

            if (conexionAdo == null)
            {
                GRA0150Logger.Advertencia(
                    "No hi ha connexió ADODB disponible "
                    + "per llegir NUMDECPRC. "
                    + "Es provarà el fallback OleDb.",
                    "Origen=ADODB",
                    "Operacion=NUMDECPRC");

                return false;
            }

            const string sql =
                @"SELECT TOP (1) NUMDECPRC
                  FROM dbo.DATOSCONFIG;";

            try
            {
                Recordset lector =
                    null;

                try
                {
                    object registresAfectats;
                    object parametres =
                        Type.Missing;

                    Command comando =
                        new Command
                        {
                            ActiveConnection =
                                conexionAdo,
                            CommandText =
                                sql,
                            CommandType =
                                CommandTypeEnum.adCmdText
                        };

                    lector =
                        comando.Execute(
                            out registresAfectats,
                            ref parametres,
                            (int)CommandTypeEnum.adCmdText);

                    if (lector == null ||
                        lector.EOF)
                    {
                        GRA0150Logger.Advertencia(
                            "La lectura NUMDECPRC via ADODB "
                            + "no ha retornat cap registre. "
                            + "Es provarà el fallback OleDb.",
                            "Origen=ADODB",
                            "Operacion=NUMDECPRC");

                        return false;
                    }

                    object resultado =
                        lector.Fields[0].Value;

                    if (!TryConvertirDecimalesPrecio(
                        resultado,
                        out numeroDecimales))
                    {
                        GRA0150Logger.Advertencia(
                            "La lectura NUMDECPRC via ADODB "
                            + "ha retornat un valor no vàlid. "
                            + "Es provarà el fallback OleDb.",
                            "Origen=ADODB",
                            "Operacion=NUMDECPRC");

                        return false;
                    }

                    GRA0150Logger.Informacio(
                        "Lectura NUMDECPRC via ADODB correcta.");

                    return true;
                }
                finally
                {
                    TancarRecordset(
                        lector);
                }
            }
            catch (Exception ex)
            {
                RegistrarErrorLectura(
                    "ADODB",
                    "NUMDECPRC",
                    ex,
                    "Es provarà el fallback OleDb.");

                return false;
            }
        }

        private static bool TryObtenerNumeroDecimalesPrecioOleDb(
            string conexionEmpresa,
            out int numeroDecimales)
        {
            numeroDecimales =
                DecimalesPrecioPorDefecto;

            if (string.IsNullOrWhiteSpace(
                conexionEmpresa))
            {
                GRA0150Logger.Advertencia(
                    "No hi ha connexió OleDb disponible "
                    + "per llegir NUMDECPRC. "
                    + "S'utilitza fallback funcional de 4 decimals.",
                    "Origen=OleDb",
                    "Operacion=NUMDECPRC");

                return false;
            }

            const string sql =
                @"SELECT TOP (1) NUMDECPRC
                  FROM dbo.DATOSCONFIG;";

            try
            {
                using (OleDbConnection conexion =
                    new OleDbConnection(
                        conexionEmpresa))
                {
                    conexion.Open();

                    using (OleDbCommand comando =
                        new OleDbCommand(
                            sql,
                            conexion))
                    {
                        object resultado =
                            comando.ExecuteScalar();

                        if (!TryConvertirDecimalesPrecio(
                            resultado,
                            out numeroDecimales))
                        {
                            GRA0150Logger.Advertencia(
                                "La lectura NUMDECPRC via OleDb "
                                + "ha retornat un valor no vàlid. "
                                + "S'utilitza fallback funcional "
                                + "de 4 decimals.",
                                "Origen=OleDb",
                                "Operacion=NUMDECPRC");

                            return false;
                        }

                        GRA0150Logger.Informacio(
                            "Lectura NUMDECPRC via OleDb correcta.");

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarErrorLectura(
                    "OleDb",
                    "NUMDECPRC",
                    ex,
                    "S'utilitza fallback funcional de 4 decimals.");

                return false;
            }
        }

        private static bool TryObtenerConfiguracionLogRecargoAdo(
            Connection conexionAdo,
            out bool logActivo,
            out string rutaLog)
        {
            logActivo = false;
            rutaLog = string.Empty;

            if (conexionAdo == null)
            {
                GRA0150Logger.Advertencia(
                    "No hi ha connexió ADODB disponible "
                    + "per llegir la configuració del log. "
                    + "Es provarà el fallback OleDb.",
                    "Origen=ADODB",
                    "Operacion=ConfiguracionLog");

                return false;
            }

            const string sql =
                @"SELECT CLAVE, VALOR
                  FROM dbo.AT_GRA0150NET_CONFIG
                  WHERE ACTIVO = 1
                    AND CLAVE IN
                    (
                        'Recargo_LogActivo',
                        'Recargo_LogRuta'
                    );";

            try
            {
                Dictionary<string, string> configuracion =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                Recordset lector =
                    null;

                try
                {
                    object registresAfectats;
                    object parametres =
                        Type.Missing;

                    Command comando =
                        new Command
                        {
                            ActiveConnection =
                                conexionAdo,
                            CommandText =
                                sql,
                            CommandType =
                                CommandTypeEnum.adCmdText
                        };

                    lector =
                        comando.Execute(
                            out registresAfectats,
                            ref parametres,
                            (int)CommandTypeEnum.adCmdText);

                    if (lector == null)
                    {
                        GRA0150Logger.Advertencia(
                            "La lectura de configuració del log "
                            + "via ADODB no ha retornat cap lector. "
                            + "Es provarà el fallback OleDb.",
                            "Origen=ADODB",
                            "Operacion=ConfiguracionLog");

                        return false;
                    }

                    while (!lector.EOF)
                    {
                        string clave =
                            Convert.ToString(
                                lector.Fields["CLAVE"].Value);

                        object valorBruto =
                            lector.Fields["VALOR"].Value;

                        string valor =
                            valorBruto == null ||
                            valorBruto == DBNull.Value
                                ? string.Empty
                                : Convert.ToString(
                                    valorBruto);

                        if (!string.IsNullOrWhiteSpace(
                            clave))
                        {
                            configuracion[
                                clave.Trim()] =
                                valor == null
                                    ? string.Empty
                                    : valor.Trim();
                        }

                        lector.MoveNext();
                    }
                }
                finally
                {
                    TancarRecordset(
                        lector);
                }

                if (!TryInterpretarConfiguracionLog(
                    configuracion,
                    out logActivo,
                    out rutaLog))
                {
                    GRA0150Logger.Advertencia(
                        "La configuració del log llegida via ADODB "
                        + "és incompleta o no vàlida. "
                        + "Es provarà el fallback OleDb.",
                        "Origen=ADODB",
                        "Operacion=ConfiguracionLog");

                    return false;
                }

                GRA0150Logger.Informacio(
                    "Lectura de configuració del log via ADODB correcta.");

                return true;
            }
            catch (Exception ex)
            {
                RegistrarErrorLectura(
                    "ADODB",
                    "ConfiguracionLog",
                    ex,
                    "Es provarà el fallback OleDb.");

                logActivo = false;
                rutaLog = string.Empty;

                return false;
            }
        }

        private static bool TryObtenerConfiguracionLogRecargoOleDb(
            string conexionEmpresa,
            out bool logActivo,
            out string rutaLog)
        {
            logActivo = false;
            rutaLog = string.Empty;

            if (string.IsNullOrWhiteSpace(
                conexionEmpresa))
            {
                GRA0150Logger.Advertencia(
                    "No hi ha connexió OleDb disponible "
                    + "per llegir la configuració del log. "
                    + "Es manté el log local de reserva.",
                    "Origen=OleDb",
                    "Operacion=ConfiguracionLog");

                return false;
            }

            const string sql =
                @"SELECT CLAVE, VALOR
                  FROM dbo.AT_GRA0150NET_CONFIG
                  WHERE ACTIVO = 1
                    AND CLAVE IN
                    (
                        'Recargo_LogActivo',
                        'Recargo_LogRuta'
                    );";

            try
            {
                Dictionary<string, string> configuracion =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                using (OleDbConnection conexion =
                    new OleDbConnection(
                        conexionEmpresa))
                {
                    conexion.Open();

                    using (OleDbCommand comando =
                        new OleDbCommand(
                            sql,
                            conexion))
                    using (OleDbDataReader lector =
                        comando.ExecuteReader())
                    {
                        if (lector == null)
                        {
                            GRA0150Logger.Advertencia(
                                "La lectura de configuració del log "
                                + "via OleDb no ha retornat cap lector. "
                                + "Es manté el log local de reserva.",
                                "Origen=OleDb",
                                "Operacion=ConfiguracionLog");

                            return false;
                        }

                        while (lector.Read())
                        {
                            string clave =
                                Convert.ToString(
                                    lector["CLAVE"]);

                            string valor =
                                lector["VALOR"] == DBNull.Value
                                    ? string.Empty
                                    : Convert.ToString(
                                        lector["VALOR"]);

                            if (string.IsNullOrWhiteSpace(
                                clave))
                            {
                                continue;
                            }

                            configuracion[
                                clave.Trim()] =
                                valor == null
                                    ? string.Empty
                                    : valor.Trim();
                        }
                    }
                }

                if (!TryInterpretarConfiguracionLog(
                    configuracion,
                    out logActivo,
                    out rutaLog))
                {
                    GRA0150Logger.Advertencia(
                        "La configuració del log llegida via OleDb "
                        + "és incompleta o no vàlida. "
                        + "Es manté el log local de reserva.",
                        "Origen=OleDb",
                        "Operacion=ConfiguracionLog");

                    return false;
                }

                GRA0150Logger.Informacio(
                    "Lectura de configuració del log via OleDb correcta.");

                return true;
            }
            catch (Exception ex)
            {
                RegistrarErrorLectura(
                    "OleDb",
                    "ConfiguracionLog",
                    ex,
                    "Es manté el log local de reserva.");

                logActivo = false;
                rutaLog = string.Empty;

                return false;
            }
        }

        private static bool TryObtenerCodigosArticulosExentosAdo(
            Connection conexionAdo,
            HashSet<string> codigos)
        {
            if (conexionAdo == null)
            {
                GRA0150Logger.Advertencia(
                    "No hi ha connexió ADODB disponible "
                    + "per llegir AT_ARTICULOS_EXENTOS. "
                    + "Es provarà el fallback OleDb.",
                    "Origen=ADODB",
                    "Operacion=ArticulosExentos");

                return false;
            }

            const string sql =
                @"SELECT CODART
                  FROM dbo.AT_ARTICULOS_EXENTOS;";

            try
            {
                Recordset lector =
                    null;

                try
                {
                    object registresAfectats;
                    object parametres =
                        Type.Missing;

                    Command comando =
                        new Command
                        {
                            ActiveConnection =
                                conexionAdo,
                            CommandText =
                                sql,
                            CommandType =
                                CommandTypeEnum.adCmdText
                        };

                    lector =
                        comando.Execute(
                            out registresAfectats,
                            ref parametres,
                            (int)CommandTypeEnum.adCmdText);

                    if (lector == null)
                    {
                        GRA0150Logger.Advertencia(
                            "La lectura AT_ARTICULOS_EXENTOS "
                            + "via ADODB no ha retornat cap lector. "
                            + "Es provarà el fallback OleDb.",
                            "Origen=ADODB",
                            "Operacion=ArticulosExentos");

                        return false;
                    }

                    while (!lector.EOF)
                    {
                        TryAfegirCodigoArticuloExento(
                            codigos,
                            lector.Fields["CODART"].Value);

                        lector.MoveNext();
                    }
                }
                finally
                {
                    TancarRecordset(
                        lector);
                }

                RegistrarLecturaArticulosExentosCorrecta(
                    "ADODB",
                    codigos);

                return true;
            }
            catch (Exception ex)
            {
                RegistrarErrorLectura(
                    "ADODB",
                    "ArticulosExentos",
                    ex,
                    "Es provarà el fallback OleDb.");

                return false;
            }
        }

        private static bool TryObtenerCodigosArticulosExentosOleDb(
            string conexionEmpresa,
            HashSet<string> codigos)
        {
            if (string.IsNullOrWhiteSpace(
                conexionEmpresa))
            {
                GRA0150Logger.Advertencia(
                    "No hi ha connexió OleDb disponible "
                    + "per llegir AT_ARTICULOS_EXENTOS. "
                    + "Es continua sense exclusions addicionals.",
                    "Origen=OleDb",
                    "Operacion=ArticulosExentos");

                return false;
            }

            const string sql =
                @"SELECT CODART
                  FROM dbo.AT_ARTICULOS_EXENTOS;";

            try
            {
                using (OleDbConnection conexion =
                    new OleDbConnection(
                        conexionEmpresa))
                {
                    conexion.Open();

                    using (OleDbCommand comando =
                        new OleDbCommand(
                            sql,
                            conexion))
                    using (OleDbDataReader lector =
                        comando.ExecuteReader())
                    {
                        if (lector == null)
                        {
                            GRA0150Logger.Advertencia(
                                "La lectura AT_ARTICULOS_EXENTOS "
                                + "via OleDb no ha retornat cap lector. "
                                + "Es continua sense exclusions addicionals.",
                                "Origen=OleDb",
                                "Operacion=ArticulosExentos");

                            return false;
                        }

                        while (lector.Read())
                        {
                            TryAfegirCodigoArticuloExento(
                                codigos,
                                lector["CODART"]);
                        }
                    }
                }

                RegistrarLecturaArticulosExentosCorrecta(
                    "OleDb",
                    codigos);

                return true;
            }
            catch (Exception ex)
            {
                RegistrarErrorLectura(
                    "OleDb",
                    "ArticulosExentos",
                    ex,
                    "Es continua sense exclusions addicionals.");

                return false;
            }
        }

        private static bool TryInterpretarConfiguracionLog(
            Dictionary<string, string> configuracion,
            out bool logActivo,
            out string rutaLog)
        {
            logActivo = false;
            rutaLog = string.Empty;

            string valorLogActivo;
            string valorRuta;

            if (!configuracion.TryGetValue(
                ClaveRecargoLogActivo,
                out valorLogActivo))
            {
                return false;
            }

            if (!configuracion.TryGetValue(
                ClaveRecargoLogRuta,
                out valorRuta))
            {
                return false;
            }

            bool activoInterpretado;

            if (!TryConvertirBooleano(
                valorLogActivo,
                out activoInterpretado))
            {
                return false;
            }

            logActivo =
                activoInterpretado;

            rutaLog =
                valorRuta ?? string.Empty;

            return true;
        }

        private static HashSet<string> CrearConjuntoCodigosArticulos()
        {
            return new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        }

        private static void TryAfegirCodigoArticuloExento(
            HashSet<string> codigos,
            object valorCodigo)
        {
            if (codigos == null ||
                valorCodigo == null ||
                valorCodigo == DBNull.Value)
            {
                return;
            }

            string codigo =
                Convert.ToString(
                    valorCodigo);

            if (string.IsNullOrWhiteSpace(
                codigo))
            {
                return;
            }

            codigos.Add(
                codigo.Trim());
        }

        private static void RegistrarLecturaArticulosExentosCorrecta(
            string origen,
            HashSet<string> codigos)
        {
            int numeroCodigos =
                codigos == null
                    ? 0
                    : codigos.Count;

            GRA0150Logger.Informacio(
                "Lectura AT_ARTICULOS_EXENTOS via "
                + origen
                + " correcta.",
                "NumeroCodigos="
                    + numeroCodigos);

            if (numeroCodigos <= 0)
            {
                return;
            }

            GRA0150Logger.Debug(
                "Codis d'article exempts recuperats.",
                "Origen="
                    + origen,
                "Codigos="
                    + string.Join(
                        ", ",
                        codigos));
        }

        /// <summary>
        /// Interpreta els formats habituals utilitzats
        /// per representar valors booleans.
        /// </summary>
        private static bool TryConvertirBooleano(
            string valor,
            out bool resultado)
        {
            resultado = false;

            if (string.IsNullOrWhiteSpace(
                valor))
            {
                return false;
            }

            string normalizado =
                valor.Trim();

            bool valorBooleano;

            if (bool.TryParse(
                normalizado,
                out valorBooleano))
            {
                resultado =
                    valorBooleano;

                return true;
            }

            switch (normalizado.ToUpperInvariant())
            {
                case "1":
                case "T":
                case "S":
                case "SI":
                case "SÍ":
                case "Y":
                case "YES":

                    resultado = true;
                    return true;

                case "0":
                case "F":
                case "N":
                case "NO":

                    resultado = false;
                    return true;

                default:

                    return false;
            }
        }

        private static bool TryConvertirDecimalesPrecio(
            object valor,
            out int numeroDecimales)
        {
            numeroDecimales =
                DecimalesPrecioPorDefecto;

            if (valor == null ||
                valor == DBNull.Value)
            {
                return false;
            }

            int numero =
                Convert.ToInt32(
                    valor);

            if (numero < 0 ||
                numero > 10)
            {
                return false;
            }

            numeroDecimales =
                numero;

            return true;
        }

        private static void TancarRecordset(
            Recordset lector)
        {
            if (lector == null)
            {
                return;
            }

            try
            {
                if (lector.State ==
                    (int)ObjectStateEnum.adStateOpen)
                {
                    lector.Close();
                }
            }
            catch
            {
                /*
                 * El tancament del Recordset no pot propagar
                 * cap excepció cap a a3ERP.
                 */
            }
        }

        private static void RegistrarErrorLectura(
            string origen,
            string operacion,
            Exception ex,
            string accioPosterior)
        {
            GRA0150Logger.Advertencia(
                "No s'ha pogut llegir "
                + operacion
                + " via "
                + origen
                + ". "
                + accioPosterior,
                "Origen="
                    + origen,
                "Operacion="
                    + operacion,
                "TipoError="
                    + ex.GetType().FullName,
                "Mensaje="
                    + ex.Message);
        }
    }
}
