using System;

namespace LME.Common.Engine
{
    /// <summary>
    /// Розширення для APN: multivoice параметр для Ashdi.
    /// </summary>
    public static class ApnExtensions
    {
        /// <summary>
        /// Додає параметр multivoice до URL Ashdi VOD.
        /// </summary>
        /// <param name="url">URL стріму.</param>
        /// <param name="enable">Чи застосовувати (default true). Деякі модулі мають умовне вимкнення.</param>
        public static string WithAshdiMultivoice(string url, bool enable = true)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            if (!enable)
                return url;

            if (url.IndexOf("ashdi.vip/vod/", StringComparison.OrdinalIgnoreCase) < 0)
                return url;

            if (url.IndexOf("multivoice", StringComparison.OrdinalIgnoreCase) >= 0)
                return url;

            return url.Contains("?") ? $"{url}&multivoice" : $"{url}?multivoice";
        }
    }
}
