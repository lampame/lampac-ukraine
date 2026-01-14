using Shared.Models.Base;  
using System.Collections.Generic;  
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
  
namespace Uaflix  
{  
    public class OnlineApi  
    {
        private static int _seed;
        public static List<(string name, string url, string plugin, int index)> Events(string host, long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email)
        {
            Touch(host);

            var online = new List<(string name, string url, string plugin, int index)>();

            var init = ModInit.UaFlix;
            if (init.enable && !init.rip)
            {
                string url = init.overridehost;
                if (string.IsNullOrEmpty(url))
                    url = $"{host}/uaflix";

                online.Add((init.displayname, url, "uaflix", init.displayindex));
            }

            return online;
        }
        private static void Touch(string host)
        {
            if (Interlocked.Exchange(ref _seed, 1) == 1)
                return;

            Task.Run(async () =>
            {
                try
                {
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
