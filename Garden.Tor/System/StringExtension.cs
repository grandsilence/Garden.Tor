using System;

namespace Garden.Tor.System
{
    internal static class StringExtension
    {
        /*
        private static readonly DateTime Jan1St1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static ulong GetMSTime() {
            return (ulong)((DateTime.UtcNow - Jan1St1970).TotalMilliseconds);
        }

        public static string[] Substrings(this string str, string left, string right,
            int startIndex = 0, StringComparison comparsion = StringComparison.Ordinal) 
        {
            if (string.IsNullOrEmpty(str))
                return new string[0];
            
            #region Проверка параметров
            if (string.IsNullOrEmpty(left))
                throw new ArgumentNullException("left");

            if (string.IsNullOrEmpty(right))
                throw new ArgumentNullException("right");
            
            if (startIndex < 0 || startIndex >= str.Length)
                throw new ArgumentOutOfRangeException("startIndex", "Wrong start index");
            #endregion

            int currentStartIndex = startIndex;
            List<string> strings = new List<string>();
            while (true) {
                // Ищем начало позиции левой подстроки.
                int leftPosBegin = str.IndexOf(left, currentStartIndex, comparsion);
                if (leftPosBegin == -1)
                    break;

                // Вычисляем конец позиции левой подстроки.
                int leftPosEnd = leftPosBegin + left.Length;
                // Ищем начало позиции правой строки.
                int rightPos = str.IndexOf(right, leftPosEnd, comparsion);
                if (rightPos == -1)
                    break;

                // Вычисляем длину найденной подстроки.
                int length = rightPos - leftPosEnd;
                strings.Add(str.Substring(leftPosEnd, length));
                // Вычисляем конец позиции правой подстроки.
                currentStartIndex = rightPos + right.Length;
            }
            return strings.ToArray();
        }

        */

        public static string Substring(this string str, string left, string right,
            int startIndex = 0, StringComparison comparsion = StringComparison.Ordinal) 
        {
            if (!string.IsNullOrEmpty(str) &&
                !string.IsNullOrEmpty(left) &&
                !string.IsNullOrEmpty(right) &&
                startIndex >= 0 && startIndex < str.Length) {

                // Ищем начало позиции левой подстроки.
                int leftPosBegin = str.IndexOf(left, startIndex, comparsion);
                if (leftPosBegin != -1) {
                    // Вычисляем конец позиции левой подстроки.
                    int leftPosEnd = leftPosBegin + left.Length;
                    // Ищем начало позиции правой строки.
                    int rightPos = str.IndexOf(right, leftPosEnd, comparsion);

                    if (rightPos != -1)
                        return str.Substring(leftPosEnd, rightPos - leftPosEnd);
                }
            }
            return null;
        }

        /*
        public static string ToUpperFirst(this string s) {
            // Check for empty string.
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            
            // Return char and concat substring.
            return char.ToUpper(s[0]) + s.Substring(1).ToLower();
        }

        /// <summary>
        /// Преобразовывает юникод символы строки в вид \u0000.
        /// </summary>
        public static string EncodeJson(this string value) {
            StringBuilder sb = new StringBuilder();
            foreach (char c in value) {
                if (c > 127) {
                    // This character is too big for ASCII
                    string encodedValue = "\\u" + ((int)c).ToString("x4");
                    sb.Append(encodedValue);
                } else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Преобразовывает юникод символы вида \u0000 в строке в нормальный вид.
        /// </summary>
        public static string DecodeJson(this string value) {
            if (string.IsNullOrEmpty(value))
                return null;
            else
                return Regex.Replace(value, @"\\u([\dA-Fa-f]{4})", v => ((char)Convert.ToInt32(v.Groups[1].Value, 16)).ToString());
        }

        public static string ParseJson(this string str, string tag, ref int lastIndex,
            int startIndex = 0, StringComparison comparsion = StringComparison.Ordinal) {
            if (!String.IsNullOrEmpty(str) &&
                !String.IsNullOrEmpty(tag) &&
                startIndex >= 0 && startIndex < str.Length) {
                const int notFound = -1;
                string left = "\"" + tag + "\":\"";
                string right = "\"";
                // Ищем начало позиции левой подстроки.
                int leftPosBegin = str.IndexOf(left, startIndex, comparsion);
                if (leftPosBegin != notFound) {
                    // Вычисляем конец позиции левой подстроки.
                    int leftPosEnd = leftPosBegin + left.Length;
                    // Ищем начало позиции правой строки.
                    int rightPos = str.IndexOf(right, leftPosEnd, comparsion);

                    lastIndex = rightPos + right.Length;

                    if (rightPos != notFound)
                        return str.Substring(leftPosEnd, rightPos - leftPosEnd);
                    else
                        lastIndex = notFound;
                } else
                    lastIndex = notFound;
            }
            return null;
        }

        public static string ParseJson(this string str, string tag,
            int startIndex = 0, StringComparison comparsion = StringComparison.Ordinal) {
            if (!String.IsNullOrEmpty(str) &&
                !String.IsNullOrEmpty(tag) &&
                startIndex >= 0 && startIndex < str.Length) {
                const int notFound = -1;
                string left = "\"" + tag + "\":\"";
                string right = "\"";
                // Ищем начало позиции левой подстроки.
                int leftPosBegin = str.IndexOf(left, startIndex, comparsion);
                if (leftPosBegin != notFound) {
                    // Вычисляем конец позиции левой подстроки.
                    int leftPosEnd = leftPosBegin + left.Length;
                    // Ищем начало позиции правой строки.
                    int rightPos = str.IndexOf(right, leftPosEnd, comparsion);


                    if (rightPos != notFound)
                        return str.Substring(leftPosEnd, rightPos - leftPosEnd);
                }
            }
            return null;
        }

        public static string ParseJsonEx(this string str, string tag, ref int lastIndex,
            int startIndex = 0, StringComparison comparsion = StringComparison.Ordinal) {
            if (startIndex < 0 || startIndex >= str.Length)
                throw new IndexOutOfRangeException("Не найден тег " + tag);

            if (!string.IsNullOrEmpty(str) && !string.IsNullOrEmpty(tag)) {
                const int notFound = -1;
                string left = "\"" + tag + "\":\"";
                string right = "\"";
                // Ищем начало позиции левой подстроки.
                int leftPosBegin = str.IndexOf(left, startIndex, comparsion);
                if (leftPosBegin != notFound) {
                    // Вычисляем конец позиции левой подстроки.
                    int leftPosEnd = leftPosBegin + left.Length;
                    // Ищем начало позиции правой строки.
                    int rightPos = str.IndexOf(right, leftPosEnd, comparsion);

                    lastIndex = rightPos + right.Length;

                    if (rightPos != notFound)
                        return str.Substring(leftPosEnd, rightPos - leftPosEnd);
                    else
                        lastIndex = notFound;
                } else
                    lastIndex = notFound;
            }
            return null;
        }

        public static string ParseJsonEx(this string str, string tag,
            int startIndex = 0, StringComparison comparsion = StringComparison.Ordinal) {
            if (startIndex < 0 || startIndex >= str.Length)
                throw new IndexOutOfRangeException("Не найден тег " + tag);

            if (!string.IsNullOrEmpty(str) &&
                !string.IsNullOrEmpty(tag)) {
                const int notFound = -1;
                string left = "\"" + tag + "\":\"";
                string right = "\"";
                // Ищем начало позиции левой подстроки.
                int leftPosBegin = str.IndexOf(left, startIndex, comparsion);
                if (leftPosBegin != notFound) {
                    // Вычисляем конец позиции левой подстроки.
                    int leftPosEnd = leftPosBegin + left.Length;
                    // Ищем начало позиции правой строки.
                    int rightPos = str.IndexOf(right, leftPosEnd, comparsion);


                    if (rightPos != notFound)
                        return str.Substring(leftPosEnd, rightPos - leftPosEnd);
                }
            }
            return null;
        }

        /// <summary>
        /// Преобразовывает текст из кодировки Windows-1251 в кодировку UTF-8
        /// </summary>
        /// <param name="source">Исходный текст</param>
        /// <returns>Возвращает исправленную версию строки, в понятном виде</returns>
        public static string Win1251ToUTF8(this string source) {
            Encoding win1251 = Encoding.GetEncoding("Windows-1251");

            byte[] utf8Bytes = win1251.GetBytes(source);
            byte[] win1251Bytes = Encoding.Convert(Encoding.UTF8, win1251, utf8Bytes);
            source = win1251.GetString(win1251Bytes);
            return source;
        }
        */
    }
}
