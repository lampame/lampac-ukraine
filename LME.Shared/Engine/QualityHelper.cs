using System;
using System.Net;
using System.Text.RegularExpressions;

namespace LME.Common.Engine
{
    /// <summary>
    /// Визначення якості відео та побудова відображуваної назви.
    /// Винесено з 5 копій у *Invoke.cs (Uaflix, Mikai, AnimeON, KlonFUN, Makhno).
    /// </summary>
    public static partial class QualityHelper
    {
        [GeneratedRegex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase)]
        private static partial Regex Quality4kRegex();

        [GeneratedRegex(@"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b", RegexOptions.IgnoreCase)]
        private static partial Regex QualityFhdRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        [GeneratedRegex(@"(19|20)\d{2}")]
        private static partial Regex YearPrefixRegex();

        /// <summary>
        /// Визначає тег якості ([4K] або [FHD]) з назви/посилання.
        /// </summary>
        public static string DetectQualityTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (Quality4kRegex().IsMatch(value))
                return "[4K]";

            if (QualityFhdRegex().IsMatch(value))
                return "[FHD]";

            return null;
        }

        /// <summary>
        /// Якщо назва починається з року (напр. "2024 - Фільм"), прибирає префікс.
        /// </summary>
        public static string StripMoviePrefix(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title;

            string normalized = WhitespaceRegex().Replace(title, " ").Trim();
            int sepIndex = normalized.LastIndexOf(" - ", StringComparison.Ordinal);
            if (sepIndex <= 0 || sepIndex >= normalized.Length - 3)
                return normalized;

            string prefix = normalized.Substring(0, sepIndex).Trim();
            string suffix = normalized.Substring(sepIndex + 3).Trim();
            if (string.IsNullOrWhiteSpace(suffix))
                return normalized;

            if (YearPrefixRegex().IsMatch(prefix))
                return suffix;

            return normalized;
        }

        /// <summary>
        /// Будує відображувану назву з теґом якості.
        /// Використовує WebUtility.HtmlDecode для декодування HTML-entities.
        /// </summary>
        /// <param name="rawTitle">Сира назва з плеєра.</param>
        /// <param name="linkOrUrl">URL або посилання для визначення якості.</param>
        /// <param name="index">Індекс варіанту (для fallback назви).</param>
        public static string BuildDisplayTitle(string rawTitle, string linkOrUrl, int index)
        {
            string normalized = string.IsNullOrWhiteSpace(rawTitle)
                ? $"Варіант {index}"
                : StripMoviePrefix(WebUtility.HtmlDecode(rawTitle).Trim());

            string qualityTag = DetectQualityTag($"{normalized} {linkOrUrl}");
            if (string.IsNullOrWhiteSpace(qualityTag))
                return normalized;

            if (normalized.StartsWith("[4K]", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("[FHD]", StringComparison.OrdinalIgnoreCase))
                return normalized;

            return $"{qualityTag} {normalized}";
        }
    }
}
