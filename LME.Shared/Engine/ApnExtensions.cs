using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using System;

namespace LME.Common.Engine
{
    /// <summary>
    /// Розширення для APN: magic APN для Ashdi, multivoice параметр.
    /// Винесено з 5-6 копій у Controller.cs / *Invoke.cs.
    /// </summary>
    public static class ApnExtensions
    {
        /// <summary>
        /// Вмикає magic APN для Ashdi, якщо гравець — inner player і APN ще не налаштовано.
        /// </summary>
        /// <param name="httpContext">HTTP-контекст контролера.</param>
        /// <param name="host">Хост лампи.</param>
        /// <param name="init">Налаштування модуля.</param>
        /// <param name="requestInfo">Інформація про запит (для RchClient).</param>
        /// <param name="magicApnAshdiHost">Хост Ashdi з конфігу (ModInit.MagicApnAshdiHost).</param>
        /// <param name="onLog">Логер модуля.</param>
        /// <param name="moduleName">Назва модуля для логування.</param>
        public static void TryEnableMagicApn(
            HttpContext httpContext,
            string host,
            OnlinesSettings init,
            RequestModel requestInfo,
            string magicApnAshdiHost,
            Action<string> onLog,
            string moduleName)
        {
            if (init == null
                || init.apn != null
                || init.streamproxy
                || string.IsNullOrWhiteSpace(magicApnAshdiHost))
                return;

            string player = new RchClient(httpContext, host, init, requestInfo).InfoConnected()?.player;
            bool useInnerPlayer = string.IsNullOrWhiteSpace(player)
                || player.Equals("inner", StringComparison.OrdinalIgnoreCase);
            if (!useInnerPlayer)
                return;

            ApnHelper.ApplyInitConf(true, magicApnAshdiHost, init);
            onLog($"{moduleName}: увімкнено magic_apn для Ashdi (player={player ?? "unknown"}).");
        }

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
