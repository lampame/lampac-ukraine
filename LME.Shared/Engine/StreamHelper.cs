using Shared.Engine;
using Shared.Models.Online.Settings;
using System;
using System.Text.RegularExpressions;

namespace LME.Common.Engine
{
    /// <summary>
    /// Спільні утиліти для роботи зі стрімами: очистка URL, APN-логіка, checkOnlineSearch.
    /// Винесено з 11-12 копій у Controller.cs кожного модуля.
    /// </summary>
    public static partial class StreamHelper
    {
        [GeneratedRegex(@"([?&])(account_email|uid|nws_id)=[^&]*", RegexOptions.IgnoreCase)]
        private static partial Regex LampacArgsRegex();

        /// <summary>
        /// Видаляє службові параметри lampac (account_email, uid, nws_id) з URL стріму.
        /// </summary>
        public static string StripLampacArgs(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            string cleaned = LampacArgsRegex().Replace(url, "$1");
            cleaned = cleaned.Replace("?&", "?").Replace("&&", "&").TrimEnd('?', '&');
            return cleaned;
        }

        /// <summary>
        /// Уніфікована логіка побудови URL стріму з APN-підтримкою.
        /// Контролер передає лямбду hostStreamProxy, яка викликає власний HostStreamProxy з потрібними параметрами.
        /// </summary>
        /// <param name="init">Налаштування модуля.</param>
        /// <param name="streamLink">Сирий URL стріму (буде очищено через StripLampacArgs).</param>
        /// <param name="apnHostProvided">Чи налаштовано APN-хост (ModInit.ApnHostProvided).</param>
        /// <param name="hostStreamProxy">Лямбда (init, cleanedLink) → URL. Контролер передає свою версію HostStreamProxy.</param>
        /// <param name="checkAshdiUrl">Чи перевіряти ApnHelper.IsAshdiUrl (default true). NMoonAnime передає false.</param>
        public static string BuildStreamUrl(
            OnlinesSettings init,
            string streamLink,
            bool apnHostProvided,
            Func<OnlinesSettings, string, string> hostStreamProxy,
            bool checkAshdiUrl = true)
        {
            string link = StripLampacArgs(streamLink?.Trim());
            if (string.IsNullOrEmpty(link))
                return link;

            if (ApnHelper.IsEnabled(init))
            {
                if (apnHostProvided || (checkAshdiUrl && ApnHelper.IsAshdiUrl(link)))
                    return ApnHelper.WrapUrl(init, link);

                var noApn = (OnlinesSettings)init.Clone();
                noApn.apnstream = false;
                noApn.apn = null;
                return hostStreamProxy(noApn, link);
            }

            return hostStreamProxy(init, link);
        }

        /// <summary>
        /// Перевіряє, чи увімкнено checkOnlineSearch у головному Online.ModInit.
        /// Рефлексивний доступ до Shared.dll конфігу.
        /// </summary>
        public static bool IsCheckOnlineSearchEnabled()
        {
            try
            {
                var onlineType = Type.GetType("Online.ModInit");
                if (onlineType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        onlineType = asm.GetType("Online.ModInit");
                        if (onlineType != null)
                            break;
                    }
                }

                var confField = onlineType?.GetField("conf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var conf = confField?.GetValue(null);
                var checkProp = conf?.GetType().GetProperty("checkOnlineSearch", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (checkProp?.GetValue(conf) is bool enabled)
                    return enabled;
            }
            catch
            {
            }

            return true;
        }
    }
}
