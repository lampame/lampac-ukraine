using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Models.Templates;
using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace Shared.Engine
{
    public static class ApnHelper
    {
        public const string DefaultHost = "https://tut.im/proxy.php?url={encodeurl}";

        private static readonly Regex SubtitleLineRegex = new Regex(@"\[([^\]]+)\]([^,]+)", RegexOptions.Compiled);

        public static bool TryGetInitConf(JObject conf, out bool enabled, out string host)
        {
            enabled = false;
            host = null;

            if (conf == null)
                return false;

            if (!conf.TryGetValue("apn", out var apnToken) || apnToken == null)
                return false;

            if (apnToken.Type == JTokenType.Boolean)
            {
                enabled = apnToken.Value<bool>();
                host = NormalizeHost(conf.Value<string>("apn_host"));
                return true;
            }

            if (apnToken.Type == JTokenType.String)
            {
                host = NormalizeHost(apnToken.Value<string>());
                enabled = host != null;
                return true;
            }

            return false;
        }

        public static string TryGetMagicAshdiHost(JObject conf)
        {
            if (conf == null || !conf.TryGetValue("magic_apn", out var magicToken) || magicToken == null)
                return null;

            if (magicToken.Type == JTokenType.Boolean)
                return magicToken.Value<bool>() ? DefaultHost : null;

            if (magicToken.Type == JTokenType.String)
                return NormalizeHost(magicToken.Value<string>());

            if (magicToken.Type != JTokenType.Object)
                return null;

            return NormalizeHost(((JObject)magicToken).Value<string>("ashdi"));
        }

        public static void ApplyInitConf(bool enabled, string host, BaseSettings init, bool useDefaultHostWhenEmpty = false)
        {
            if (init == null)
                return;

            if (!enabled)
            {
                init.apnstream = false;
                init.apn = null;
                return;
            }

            host = NormalizeHost(host);
            if (host == null && useDefaultHostWhenEmpty)
                host = DefaultHost;

            if (host == null)
            {
                init.apnstream = false;
                init.apn = null;
                return;
            }

            if (init.apn == null)
                init.apn = new ApnConf();

            init.apn.host = host;
            init.apnstream = true;
        }

        public static bool IsEnabled(BaseSettings init)
        {
            return init?.apnstream == true && !string.IsNullOrWhiteSpace(init?.apn?.host);
        }

        public static bool IsAshdiUrl(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   url.IndexOf("ashdi.vip", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string WrapUrl(BaseSettings init, string url)
        {
            if (!IsEnabled(init))
                return url;

            return BuildUrl(init.apn.host, url);
        }

        public static string BuildUrl(string host, string url)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(url))
                return url;

            if (host.Contains("{encodeurl}"))
                return host.Replace("{encodeurl}", HttpUtility.UrlEncode(url));

            if (host.Contains("{encode_uri}"))
                return host.Replace("{encode_uri}", HttpUtility.UrlEncode(url));

            if (host.Contains("{uri}"))
                return host.Replace("{uri}", url);

            return $"{host.TrimEnd('/')}/{url}";
        }

        public static SubtitleTpl ParseSubtitles(string subtitleValue)
        {
            if (string.IsNullOrWhiteSpace(subtitleValue))
                return null;

            var subtitles = new SubtitleTpl();
            string normalized = WebUtility.HtmlDecode(subtitleValue)
                .Replace("\\/", "/")
                .Replace("\\'", "'")
                .Replace("\\\"", "\"");

            foreach (Match match in SubtitleLineRegex.Matches(normalized))
            {
                string label = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                string url = WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(url))
                    subtitles.Append(label, url);
            }

            return subtitles.IsEmpty ? null : subtitles;
        }

        public static string ExtractPlayerSubtitle(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var match = Regex.Match(html, @"subtitle\s*:\s*['""]([^'""']+)['""]", RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(html, @"subtitle['""]?\s*:\s*['""]([^'""']+)['""]", RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value : null;
        }

        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            return host.Trim();
        }
    }
}
