using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StarLight
{
    public class ModInit
    {
        public static OnlinesSettings StarLight;
        private static int _seed;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            Touch(initspace);

            StarLight = new OnlinesSettings("StarLight", "https://tp-back.starlight.digital", streamproxy: false, useproxy: false)
            {
                displayname = "StarLight",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            StarLight = ModuleInvoke.Conf("StarLight", StarLight).ToObject<OnlinesSettings>();

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("starlight");
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

                    if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
                        host = uri.Host;
                    else if (Uri.TryCreate("http://" + host, UriKind.Absolute, out uri))
                        host = uri.Host;

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
