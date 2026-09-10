using ADODB;
using a3ERPActiveX;
using GRA0150Net.Infrastructure.Logging;
using System;
using System.Runtime.InteropServices;

namespace GRA0150Net.Infrastructure.Connections
{
    /// <summary>
    /// Manté una connexió viva amb l'empresa activa d'a3ERP
    /// per a lectures auxiliars de configuració.
    ///
    /// La connexió ADODB retornada per GetConexionDB("EMPRESA")
    /// és propietat d'a3ERP. Aquest servei no la tanca ni la
    /// disposa; només finalitza l'objecte Enlace que ell mateix
    /// ha inicialitzat.
    /// </summary>
    internal sealed class A3ErpConexionActivaService
    {
        private IEnlace _enlace;
        private Connection _conexionEmpresa;
        private bool _enlaceInicializado;

        /// <summary>
        /// Connexió viva de l'empresa activa retornada per a3ERP.
        /// Pot ser null si no s'ha pogut inicialitzar l'Enlace.
        /// </summary>
        public Connection ConexionEmpresa
        {
            get
            {
                return _conexionEmpresa;
            }
        }

        /// <summary>
        /// Indica si disposem d'una connexió ADODB d'empresa.
        /// </summary>
        public bool ConexionEmpresaDisponible
        {
            get
            {
                return _conexionEmpresa != null;
            }
        }

        /// <summary>
        /// Inicialitza Enlace seguint el patró validat en
        /// DLLs d'esdeveniments existents, obté la connexió
        /// d'empresa amb GetConexionDB("EMPRESA") i valida
        /// que apunti a la base de dades esperada.
        /// </summary>
        /// <param name="baseDatosEsperada">
        /// Nom de la base de dades d'empresa identificada pel
        /// context d'a3ERP.
        /// </param>
        /// <returns>
        /// true si s'ha obtingut i validat la connexió ADODB;
        /// false si cal continuar amb el fallback OleDb.
        /// </returns>
        public bool Inicializar(
            string baseDatosEsperada)
        {
            Finalizar();

            try
            {
                _enlace =
                    new Enlace();

                _enlace.RaiseOnException =
                    true;

                _enlace.VerBarraDeProgreso =
                    true;

                _enlace.Iniciar(
                    string.Empty,
                    string.Empty);

                _enlaceInicializado =
                    true;

                _conexionEmpresa =
                    _enlace.GetConexionDB(
                        "EMPRESA");

                if (_conexionEmpresa == null)
                {
                    GRA0150Logger.Advertencia(
                        "No s'ha pogut obtenir la connexió ADODB "
                        + "de l'empresa activa mitjançant "
                        + "GetConexionDB(\"EMPRESA\"). "
                        + "Es manté el fallback OleDb.");

                    return false;
                }

                if (!ValidarBaseDatosEmpresa(
                    _conexionEmpresa,
                    baseDatosEsperada))
                {
                    Finalizar();

                    return false;
                }

                GRA0150Logger.Informacio(
                    "Connexió ADODB d'empresa validada.",
                    "BaseDatos="
                        + baseDatosEsperada.Trim());

                return true;
            }
            catch (Exception ex)
            {
                GRA0150Logger.Advertencia(
                    "No s'ha pogut inicialitzar Enlace "
                    + "per obtenir la connexió ADODB de l'empresa. "
                    + "Es manté el fallback OleDb.",
                    "TipoError="
                        + ex.GetType().FullName,
                    "Mensaje="
                        + ex.Message);

                Finalizar();

                return false;
            }
        }

        private static bool ValidarBaseDatosEmpresa(
            Connection conexionEmpresa,
            string baseDatosEsperada)
        {
            string baseDatosEsperadaNormalizada =
                NormalizarBaseDatos(
                    baseDatosEsperada);

            if (string.IsNullOrWhiteSpace(
                baseDatosEsperadaNormalizada))
            {
                GRA0150Logger.Advertencia(
                    "No es pot validar la connexió ADODB "
                    + "d'empresa perquè no hi ha cap base "
                    + "de dades esperada informada. "
                    + "Es manté el fallback OleDb.");

                return false;
            }

            string baseDatosObtenida;

            if (!TryObtenerBaseDatosActual(
                conexionEmpresa,
                out baseDatosObtenida))
            {
                return false;
            }

            string baseDatosObtenidaNormalizada =
                NormalizarBaseDatos(
                    baseDatosObtenida);

            if (string.Equals(
                baseDatosEsperadaNormalizada,
                baseDatosObtenidaNormalizada,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            GRA0150Logger.Advertencia(
                "La connexió ADODB d'empresa no apunta "
                + "a la base de dades esperada. "
                + "Es manté el fallback OleDb.",
                "BaseDatosEsperada="
                    + baseDatosEsperadaNormalizada,
                "BaseDatosObtenida="
                    + baseDatosObtenidaNormalizada);

            return false;
        }

        private static bool TryObtenerBaseDatosActual(
            Connection conexionEmpresa,
            out string baseDatosActual)
        {
            baseDatosActual =
                null;

            Recordset lector =
                null;

            try
            {
                object parametres =
                    Type.Missing;

                object registresAfectats;

                Command comando =
                    new Command
                    {
                        ActiveConnection =
                            conexionEmpresa,
                        CommandText =
                            "SELECT DB_NAME()",
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
                        "No s'ha pogut validar la connexió ADODB "
                        + "d'empresa perquè SELECT DB_NAME() "
                        + "no ha retornat cap resultat. "
                        + "Es manté el fallback OleDb.");

                    return false;
                }

                object valor =
                    lector.Fields[0].Value;

                if (valor == null ||
                    valor == DBNull.Value)
                {
                    GRA0150Logger.Advertencia(
                        "No s'ha pogut validar la connexió ADODB "
                        + "d'empresa perquè SELECT DB_NAME() "
                        + "ha retornat un valor buit. "
                        + "Es manté el fallback OleDb.");

                    return false;
                }

                baseDatosActual =
                    Convert.ToString(
                        valor);

                return true;
            }
            catch (Exception ex)
            {
                GRA0150Logger.Advertencia(
                    "No s'ha pogut validar la connexió ADODB "
                    + "d'empresa amb SELECT DB_NAME(). "
                    + "Es manté el fallback OleDb.",
                    "TipoError="
                        + ex.GetType().FullName,
                    "Mensaje="
                        + ex.Message);

                return false;
            }
            finally
            {
                TancarRecordset(
                    lector);
            }
        }

        private static string NormalizarBaseDatos(
            string baseDatos)
        {
            return baseDatos == null
                ? null
                : baseDatos.Trim();
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

        /// <summary>
        /// Finalitza l'Enlace inicialitzat per aquest servei.
        ///
        /// No tanca explícitament la Connection ADODB perquè
        /// pertany a a3ERP.
        /// </summary>
        public void Finalizar()
        {
            _conexionEmpresa =
                null;

            if (_enlace == null)
            {
                _enlaceInicializado =
                    false;

                return;
            }

            try
            {
                if (_enlaceInicializado)
                {
                    _enlace.Acabar();
                }
            }
            catch (Exception ex)
            {
                GRA0150Logger.Advertencia(
                    "No s'ha pogut finalitzar l'Enlace "
                    + "de la connexió activa d'a3ERP.",
                    "TipoError="
                        + ex.GetType().FullName,
                    "Mensaje="
                        + ex.Message);
            }
            finally
            {
                try
                {
                    if (Marshal.IsComObject(
                        _enlace))
                    {
                        Marshal.FinalReleaseComObject(
                            _enlace);
                    }
                }
                catch (Exception exAlliberar)
                {
                    GRA0150Logger.Advertencia(
                        "No s'ha pogut alliberar la referència COM "
                        + "de l'Enlace de connexió activa.",
                        "TipoError="
                            + exAlliberar.GetType().FullName,
                        "Mensaje="
                            + exAlliberar.Message);
                }

                _enlace =
                    null;

                _enlaceInicializado =
                    false;
            }
        }
    }
}
