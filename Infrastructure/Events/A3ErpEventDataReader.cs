using System;
using System.Globalization;
using System.Collections.Generic;

namespace GRA0150Net.Infrastructure.Events
{
    /// <summary>
    /// Proporciona funcions auxiliars per llegir les dades
    /// que a3ERP envia als esdeveniments de documents.
    ///
    /// a3ERP lliura la capçalera i altres estructures
    /// de l'esdeveniment com objectes COM que, en aquesta
    /// integració, es materialitzen habitualment com arrays
    /// d'objectes.
    ///
    /// Aquesta classe centralitza la interpretació d'aquest
    /// format per evitar que Principal o els serveis de negoci
    /// hagin de conèixer l'estructura interna del payload.
    /// </summary>
    internal static class A3ErpEventDataReader
    {
        /// <summary>
        /// Busca un camp pel seu nom i retorna el valor brut
        /// proporcionat per a3ERP.
        ///
        /// La comparació del nom del camp no diferencia
        /// majúscules i minúscules.
        /// </summary>
        /// <param name="data">
        /// Objecte rebut des de l'esdeveniment d'a3ERP.
        /// </param>
        /// <param name="fieldName">
        /// Nom del camp que es vol recuperar.
        /// </param>
        /// <returns>
        /// Valor del camp si s'ha pogut localitzar;
        /// null en cas contrari.
        /// </returns>
        public static object GetValue(
            object data,
            string fieldName)
        {
            if (data == null ||
                string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            object[] root = data as object[];

            if (root == null ||
                root.Length < 2 ||
                root[1] == null)
            {
                return null;
            }

            object[] fields = root[1] as object[];

            if (fields == null)
            {
                return null;
            }

            foreach (object fieldObject in fields)
            {
                object[] field = fieldObject as object[];

                if (field == null ||
                    field.Length < 2)
                {
                    continue;
                }

                string currentFieldName =
                    Convert.ToString(
                        field[0],
                        CultureInfo.InvariantCulture);

                if (!string.Equals(
                    currentFieldName,
                    fieldName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return field[1];
            }

            return null;
        }

        /// <summary>
        /// Retorna un camp com a text.
        /// Si el camp no existeix o és null,
        /// retorna una cadena buida.
        /// </summary>
        public static string GetString(
            object data,
            string fieldName)
        {
            object value =
                GetValue(
                    data,
                    fieldName);

            if (value == null ||
                value == DBNull.Value)
            {
                return string.Empty;
            }

            return Convert.ToString(
                       value,
                       CultureInfo.CurrentCulture)
                   ?? string.Empty;
        }

        /// <summary>
        /// Retorna un camp com a valor decimal.
        ///
        /// Si el camp no existeix, és buit o no es pot convertir,
        /// retorna el valor per defecte indicat.
        /// </summary>
        public static decimal GetDecimal(
            object data,
            string fieldName,
            decimal defaultValue = 0m)
        {
            object value =
                GetValue(
                    data,
                    fieldName);

            if (value == null ||
                value == DBNull.Value)
            {
                return defaultValue;
            }

            if (value is decimal decimalValue)
            {
                return decimalValue;
            }

            try
            {
                return Convert.ToDecimal(
                    value,
                    CultureInfo.CurrentCulture);
            }
            catch
            {
                try
                {
                    return Convert.ToDecimal(
                        value,
                        CultureInfo.InvariantCulture);
                }
                catch
                {
                    return defaultValue;
                }
            }
        }

        /// <summary>
        /// Retorna un camp com a valor double.
        ///
        /// Es manté aquest mètode perquè alguns camps numèrics
        /// d'a3ERP poden arribar a través de COM com a Double.
        /// </summary>
        public static double GetDouble(
            object data,
            string fieldName,
            double defaultValue = 0d)
        {
            object value =
                GetValue(
                    data,
                    fieldName);

            if (value == null ||
                value == DBNull.Value)
            {
                return defaultValue;
            }

            if (value is double doubleValue)
            {
                return doubleValue;
            }

            try
            {
                return Convert.ToDouble(
                    value,
                    CultureInfo.CurrentCulture);
            }
            catch
            {
                try
                {
                    return Convert.ToDouble(
                        value,
                        CultureInfo.InvariantCulture);
                }
                catch
                {
                    return defaultValue;
                }
            }
        }

        /// <summary>
        /// Retorna un camp com a enter.
        /// </summary>
        public static int GetInt32(
            object data,
            string fieldName,
            int defaultValue = 0)
        {
            object value =
                GetValue(
                    data,
                    fieldName);

            if (value == null ||
                value == DBNull.Value)
            {
                return defaultValue;
            }

            try
            {
                return Convert.ToInt32(
                    value,
                    CultureInfo.CurrentCulture);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Indica si el payload conté un camp determinat.
        ///
        /// És útil per diferenciar entre:
        /// - un camp existent amb valor null;
        /// - un camp que a3ERP no ha inclòs en l'esdeveniment.
        /// </summary>
        public static bool ContainsField(
            object data,
            string fieldName)
        {
            if (data == null ||
                string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            object[] root = data as object[];

            if (root == null ||
                root.Length < 2 ||
                root[1] == null)
            {
                return false;
            }

            object[] fields = root[1] as object[];

            if (fields == null)
            {
                return false;
            }

            foreach (object fieldObject in fields)
            {
                object[] field = fieldObject as object[];

                if (field == null ||
                    field.Length == 0)
                {
                    continue;
                }

                string currentFieldName =
                    Convert.ToString(
                        field[0],
                        CultureInfo.InvariantCulture);

                if (string.Equals(
                    currentFieldName,
                    fieldName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Recupera els noms de camps detectats de manera recursiva
        /// dins del payload rebut des d'a3ERP.
        ///
        /// Aquest mètode està pensat principalment per diagnòstic
        /// durant el desenvolupament de la integració.
        /// </summary>
        /// <param name="data">
        /// Payload rebut des d'a3ERP.
        /// </param>
        /// <returns>
        /// Col·lecció de noms de camp diferents.
        /// </returns>
        public static List<string> GetFieldNames(
            object data)
        {
            HashSet<string> fieldNames =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            BuscarNomsCampsRecursiu(
                data,
                fieldNames);

            List<string> result =
                new List<string>(
                    fieldNames);

            result.Sort(
                StringComparer.OrdinalIgnoreCase);

            return result;
        }

        /// <summary>
        /// Recorre recursivament els arrays del payload d'a3ERP
        /// i identifica estructures del tipus:
        ///
        /// object[] { "NOMCAMP", valor }
        /// </summary>
        private static void BuscarNomsCampsRecursiu(
            object data,
            HashSet<string> fieldNames)
        {
            object[] array =
                data as object[];

            if (array == null)
            {
                return;
            }

            /*
             * Si els dos primers elements representen
             * una parella camp/valor, registrem el nom.
             */
            if (array.Length >= 2 &&
                array[0] is string)
            {
                string possibleFieldName =
                    Convert.ToString(
                        array[0],
                        CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(
                    possibleFieldName))
                {
                    fieldNames.Add(
                        possibleFieldName.Trim());
                }
            }

            /*
             * Continuem recorrent tots els elements perquè
             * el payload de línies pot contenir diversos
             * nivells d'arrays.
             */
            foreach (object item in array)
            {
                BuscarNomsCampsRecursiu(
                    item,
                    fieldNames);
            }
        }

        /// <summary>
        /// Recupera les línies detectades dins del payload
        /// proporcionat per a3ERP.
        ///
        /// Cada línia es representa com un diccionari
        /// nomCamp -> valor.
        ///
        /// El mètode identifica com a línia una estructura
        /// que contingui camps propis del detall de document,
        /// especialment IDLIN o CODART.
        /// </summary>
        /// <param name="data">
        /// Payload de línies proporcionat per a3ERP.
        /// </param>
        /// <returns>
        /// Llista de línies detectades.
        /// </returns>
        public static List<Dictionary<string, object>> GetRows(
            object data)
        {
            List<Dictionary<string, object>> rows =
                new List<Dictionary<string, object>>();

            BuscarFilesRecursiu(
                data,
                rows);

            return rows;
        }

        /// <summary>
        /// Recorre recursivament el payload d'a3ERP
        /// fins localitzar les estructures que representen
        /// les línies del document.
        /// </summary>
        private static void BuscarFilesRecursiu(
            object data,
            List<Dictionary<string, object>> rows)
        {
            object[] array =
                data as object[];

            if (array == null)
            {
                return;
            }

            Dictionary<string, object> fields =
                new Dictionary<string, object>(
                    StringComparer.OrdinalIgnoreCase);

            /*
             * Intentem interpretar els elements directes
             * de l'array com parelles:
             *
             * { "NOMCAMP", valor }
             */
            foreach (object item in array)
            {
                object[] field =
                    item as object[];

                if (field == null ||
                    field.Length < 2 ||
                    !(field[0] is string))
                {
                    continue;
                }

                string fieldName =
                    Convert.ToString(
                        field[0],
                        CultureInfo.InvariantCulture);

                if (string.IsNullOrWhiteSpace(
                    fieldName))
                {
                    continue;
                }

                fields[fieldName.Trim()] =
                    field[1];
            }

            /*
             * IDLIN i CODART són camps característics
             * d'una línia de document.
             *
             * Si en trobem algun, considerem que aquest
             * nivell del payload representa una línia.
             */
            bool pareceLinea =
                fields.ContainsKey("IDLIN")
                ||
                fields.ContainsKey("CODART");

            if (pareceLinea)
            {
                rows.Add(fields);

                /*
                 * No continuem recorrent aquesta mateixa
                 * estructura per evitar detectar la mateixa
                 * línia més d'una vegada.
                 */
                return;
            }

            /*
             * Encara no hem trobat una línia.
             * Continuem baixant pels nivells del payload.
             */
            foreach (object item in array)
            {
                BuscarFilesRecursiu(
                    item,
                    rows);
            }
        }

        /// <summary>
        /// Recupera un valor brut d'una línia detectada.
        /// </summary>
        public static object GetRowValue(
            IDictionary<string, object> row,
            string fieldName)
        {
            if (row == null ||
                string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            object value;

            return row.TryGetValue(
                fieldName,
                out value)
                ? value
                : null;
        }

        /// <summary>
        /// Recupera un camp de línia com a text.
        /// </summary>
        public static string GetRowString(
            IDictionary<string, object> row,
            string fieldName)
        {
            object value =
                GetRowValue(
                    row,
                    fieldName);

            if (value == null ||
                value == DBNull.Value)
            {
                return string.Empty;
            }

            return Convert.ToString(
                       value,
                       CultureInfo.CurrentCulture)
                   ?? string.Empty;
        }

        /// <summary>
        /// Recupera un camp numèric d'una línia
        /// com a decimal.
        ///
        /// Els imports i percentatges del projecte
        /// es treballen amb decimal per evitar errors
        /// de precisió propis dels tipus de coma flotant.
        /// </summary>
        public static decimal GetRowDecimal(
            IDictionary<string, object> row,
            string fieldName,
            decimal defaultValue = 0m)
        {
            object value =
                GetRowValue(
                    row,
                    fieldName);

            if (value == null ||
                value == DBNull.Value)
            {
                return defaultValue;
            }

            if (value is decimal decimalValue)
            {
                return decimalValue;
            }

            try
            {
                return Convert.ToDecimal(
                    value,
                    CultureInfo.CurrentCulture);
            }
            catch
            {
                try
                {
                    return Convert.ToDecimal(
                        value,
                        CultureInfo.InvariantCulture);
                }
                catch
                {
                    return defaultValue;
                }
            }
        }
    }
}