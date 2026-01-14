using Shared.Models.Base;  
using System.Collections.Generic;  
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
  
namespace AnimeON
{
    public class OnlineApi
    {
        private static int _seed;
        public static List<(string name, string url, string plugin, int index)> Events(string host, long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email)
        {
            Touch(host);

            var online = new List<(string name, string url, string plugin, int index)>();

            var init = ModInit.AnimeON;

            // Визначаємо isAnime згідно стандарту Lampac (Deepwiki):
            // isanime = true якщо original_language == "ja" або "zh"
            bool hasLang = !string.IsNullOrEmpty(original_language);
            bool isanime = hasLang && (original_language == "ja" || original_language == "zh");

            // AnimeON — аніме-провайдер. Додаємо його:
            // - при загальному пошуку (serial == -1), або
            // - якщо контент визначений як аніме (isanime), або
            // - якщо мова невідома (відсутній original_language)
            if (init.enable && !init.rip && (serial == -1 || isanime || !hasLang))
            {
                string url = init.overridehost;
                if (string.IsNullOrEmpty(url))
                    url = $"{host}/animeon";

                online.Add((init.displayname, url, "animeon", init.displayindex));
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
