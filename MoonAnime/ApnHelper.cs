using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using System;
using System.Web;

namespace Shared.Engine
{
    public static class ApnHelper
    {
        public const string DefaultHost = "https://tut.im/proxy.php?url={encodeurl}";

        public static bool TryGetInitConf(JObject conf, out bool enabled, out string host)
        {
            enabled = false;
            host = null;

            if (conf == null)
                return false;

            if (!conf.TryGetValue("apn", out var apnToken) || apnToken?.Type != JTokenType.Boolean)
                return false;

            enabled = apnToken.Value<bool>();
            host = conf.Value<string>("apn_host");
            return true;
        }

        public static void ApplyInitConf(bool enabled, string host, BaseSettings init)
        {
            if (init == null)
                return;

            if (!enabled)
            {
                init.apnstream = false;
                init.apn = null;
                return;
            }

            if (string.IsNullOrWhiteSpace(host))
                host = DefaultHost;

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
    }
}
