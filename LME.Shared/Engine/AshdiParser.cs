using System;

namespace LME.Common.Engine
{
    /// <summary>
    /// Парсинг масивів плеєрів з HTML-відповіді Ashdi/Zetvideo.
    /// Винесено з 3-4 копій у *Invoke.cs (Uaflix, Mikai, AnimeON, Makhno).
    /// </summary>
    public static class AshdiParser
    {
        /// <summary>
        /// Знаходить масив [file:..., title:...] у HTML після ключового слова "file".
        /// Повертає рядок-масив включаючи квадратні дужки, або null.
        /// </summary>
        public static string ExtractPlayerFileArray(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            int searchIndex = 0;
            while (searchIndex >= 0 && searchIndex < html.Length)
            {
                int fileIndex = html.IndexOf("file", searchIndex, StringComparison.OrdinalIgnoreCase);
                if (fileIndex < 0)
                    return null;

                int colonIndex = html.IndexOf(':', fileIndex);
                if (colonIndex < 0)
                    return null;

                int startIndex = colonIndex + 1;
                while (startIndex < html.Length && char.IsWhiteSpace(html[startIndex]))
                    startIndex++;

                if (startIndex < html.Length && (html[startIndex] == '\'' || html[startIndex] == '"'))
                {
                    startIndex++;
                    while (startIndex < html.Length && char.IsWhiteSpace(html[startIndex]))
                        startIndex++;
                }

                if (startIndex >= html.Length || html[startIndex] != '[')
                {
                    searchIndex = fileIndex + 4;
                    continue;
                }

                return ExtractBracketArray(html, startIndex);
            }

            return null;
        }

        /// <summary>
        /// Витягує збалансований масив у квадратних дужках, починаючи з startIndex.
        /// Враховує вкладеність та рядки в лапках (одинарних/подвійних) з екрануванням.
        /// </summary>
        public static string ExtractBracketArray(string text, int startIndex)
        {
            if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '[')
                return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            char quoteChar = '\0';

            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (ch == quoteChar)
                    {
                        inString = false;
                        quoteChar = '\0';
                    }

                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    inString = true;
                    quoteChar = ch;
                    continue;
                }

                if (ch == '[')
                {
                    depth++;
                    continue;
                }

                if (ch == ']')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(startIndex, i - startIndex + 1);
                }
            }

            return null;
        }
    }
}
