using System;
using System.Collections.Generic;
using System.Data.OleDb;

namespace GRA0150Net.Infrastructure.Configuration
{
    /// <summary>
    /// Repositori de lectura de configuració d'a3ERP
    /// i de la configuració pròpia de GRA0150Net.
    ///
    /// Les connexions proporcionades per a3ERP són
    /// cadenes de connexió OLE DB.
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
        /// Intenta recuperar DATOSCONFIG.NUMDECPRC.
        ///
        /// Retorna false si la lectura falla
        /// i proporciona 4 decimals com a valor de reserva.
        /// </summary>
        public bool TryObtenerNumeroDecimalesPrecio(
            string conexionEmpresa,
            out int numeroDecimales)
        {
            numeroDecimales =
                DecimalesPrecioPorDefecto;

            if (string.IsNullOrWhiteSpace(
                conexionEmpresa))
            {
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

                        if (resultado == null ||
                            resultado == DBNull.Value)
                        {
                            return false;
                        }

                        int valor =
                            Convert.ToInt32(
                                resultado);

                        /*
                         * Protecció davant configuracions
                         * incoherents o inesperades.
                         */
                        if (valor < 0 ||
                            valor > 10)
                        {
                            return false;
                        }

                        numeroDecimales =
                            valor;

                        return true;
                    }
                }
            }
            catch
            {
                /*
                 * Una incidència en una lectura auxiliar
                 * no ha de generar mai una excepció COM.
                 */
                return false;
            }
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
            logActivo = false;
            rutaLog = string.Empty;

            if (string.IsNullOrWhiteSpace(
                conexionEmpresa))
            {
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
            catch
            {
                /*
                 * Si la taula no existeix, hi ha una incidència
                 * SQL o qualsevol altre problema de lectura,
                 * Principal mantindrà el log local de reserva.
                 */
                logActivo = false;
                rutaLog = string.Empty;

                return false;
            }
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
    }
}