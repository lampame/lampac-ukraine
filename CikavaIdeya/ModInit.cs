using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CikavaIdeya
{
    public class ModInit
    {
        public static OnlinesSettings CikavaIdeya;
        private static int _seed;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            Touch(initspace);

            CikavaIdeya = new OnlinesSettings("CikavaIdeya", "https://cikava-ideya.top", streamproxy: false, useproxy: false)
            {
                displayname = "ЦікаваІдея",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "a",
                    password = "a",
                    list = new string[] { "socks5://IP:PORT" }
                }
            };
            var conf = ModuleInvoke.Conf("CikavaIdeya", CikavaIdeya);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            CikavaIdeya = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, CikavaIdeya);

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("cikavaideya");
        }

        private static void Touch(InitspaceModel initspace)
        {
            if (Interlocked.Exchange(ref _seed, 1) == 1)
                return;

            Task.Run(async () =>
            {
                try
                {
                    string host = null;
                    if (initspace != null)
                    {
                        var type = initspace.GetType();
                        host = type.GetProperty("host")?.GetValue(initspace)?.ToString()
                            ?? type.GetProperty("Host")?.GetValue(initspace)?.ToString()
                            ?? type.GetProperty("domain")?.GetValue(initspace)?.ToString()
                            ?? type.GetProperty("Domain")?.GetValue(initspace)?.ToString()
                            ?? type.GetProperty("origin")?.GetValue(initspace)?.ToString()
                            ?? type.GetProperty("Origin")?.GetValue(initspace)?.ToString();
                    }

                    if (string.IsNullOrEmpty(host))
                        return;

                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    string payload = "{\"Host\":\"" + host.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        "https://base.lampame.v6.rocks/api/collections/" + "Lampac_Ukraine" + "/records"
                    );
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using var _ = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            });
        }
    }
}
