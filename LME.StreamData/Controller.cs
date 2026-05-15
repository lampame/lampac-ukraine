using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using LME.StreamData.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace LME.StreamData.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.StreamDataSettings);
        }

        /// <summary>
        /// Головний ендпоінт модуля StreamData
        /// Працює виключно через TMDB ID (параметр id)
        /// </summary>
        [HttpGet]
        [Route("lite/lme_streamdata")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, int e = -1, bool play = false, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.StreamDataSettings);
            if (!init.enable)
                return Forbid();

            var invoke = new StreamDataInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            // checksearch — перевірка доступності
            if (checksearch)
            {
                if (!IsCheckOnlineSearchEnabled())
                    return OnError("lme_streamdata", refresh_proxy: true);

                if (id > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("lme_streamdata", refresh_proxy: true);
            }

            // play — повернути стрім для конкретного епізоду (call метод)
            if (play)
            {
                return await HandlePlay(invoke, init, id, s, e, title, original_title, t);
            }

            // Фільм
            if (serial != 1)
            {
                return await HandleMovie(invoke, init, id, title, original_title, rjson);
            }

            // Серіал
            return await HandleSerial(invoke, init, id, title, original_title, s, e, t, rjson);
        }

        /// <summary>
        /// Обробка фільму: отримуємо всі stream_urls та показуємо їх
        /// </summary>
        private async Task<ActionResult> HandleMovie(StreamDataInvoke invoke, OnlinesSettings init, long tmdbId, string title, string originalTitle, bool rjson)
        {
            var response = await invoke.GetMovie(tmdbId);
            if (response?.data?.stream_urls == null || response.data.stream_urls.Count == 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            var streamUrls = response.data.stream_urls;
            var subs = CollectSubtitles(response);

            var displayTitle = !string.IsNullOrEmpty(title) ? title : (!string.IsNullOrEmpty(originalTitle) ? originalTitle : response.data.title);
            var tpl = new MovieTpl(displayTitle, originalTitle);

            int index = 1;
            foreach (var streamUrl in streamUrls)
            {
                if (string.IsNullOrWhiteSpace(streamUrl))
                    continue;

                string label = $"Джерело #{index}";
                string processedUrl = BuildStreamUrl(init, streamUrl);
                tpl.Append(label, processedUrl, subtitles: subs);
                index++;
            }

            if (tpl.data == null || tpl.data.Count == 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            return Content(
                rjson ? tpl.ToJson() : tpl.ToHtml(),
                rjson ? "application/json; charset=utf-8" : "text/html; charset=utf-8"
            );
        }

        /// <summary>
        /// Обробка серіалу: eps → сезони → епізоди з voice-вкладками (джерела)
        /// </summary>
        private async Task<ActionResult> HandleSerial(StreamDataInvoke invoke, OnlinesSettings init, long tmdbId, string title, string originalTitle, int s, int e, string t, bool rjson)
        {
            var response = await invoke.GetTvSeries(tmdbId);
            if (response?.data?.eps == null || response.data.eps.Count == 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            var eps = response.data.eps;
            var seasons = eps.Keys
                .Select(k => int.TryParse(k, out int sn) ? sn : 0)
                .Where(sn => sn > 0)
                .OrderBy(sn => sn)
                .ToList();

            if (seasons.Count == 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            // Кількість джерел (CDN) з першого запиту
            var sources = response.data?.stream_urls?.Where(u => !string.IsNullOrWhiteSpace(u)).ToList() ?? new List<string>();
            int sourceCount = Math.Max(1, sources.Count);

            var displayTitle = !string.IsNullOrEmpty(title) ? title : (!string.IsNullOrEmpty(originalTitle) ? originalTitle : response.data.title);

            // Список сезонів
            if (s <= 0)
            {
                var seasonTpl = new SeasonTpl(seasons.Count);
                foreach (var season in seasons)
                {
                    string seasonLink = $"{host}/lite/lme_streamdata?id={tmdbId}&serial=1&s={season}&title={HttpUtility.UrlEncode(displayTitle)}&original_title={HttpUtility.UrlEncode(originalTitle)}";
                    seasonTpl.Append($"Сезон {season}", seasonLink, season.ToString());
                }

                return Content(
                    rjson ? seasonTpl.ToJson() : seasonTpl.ToHtml(),
                    rjson ? "application/json; charset=utf-8" : "text/html; charset=utf-8"
                );
            }

            // Список епізодів з voice-вкладками для вибору джерела
            string seasonKey = s.ToString();
            if (!eps.ContainsKey(seasonKey) || eps[seasonKey] == null || eps[seasonKey].Count == 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            var episodeNumbers = eps[seasonKey]
                .Select(ep => int.TryParse(ep, out int en) ? en : 0)
                .Where(en => en > 0)
                .OrderBy(en => en)
                .ToList();

            if (episodeNumbers.Count == 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            // Voice-вкладки: кожне джерело як окрема "озвучка"
            string selectedSource = string.IsNullOrEmpty(t) ? "1" : t;
            int selectedIndex = int.TryParse(selectedSource, out int si) && si >= 1 && si <= sourceCount ? si : 1;

            var voiceTpl = new VoiceTpl();
            for (int i = 1; i <= sourceCount; i++)
            {
                string voiceLink = $"{host}/lite/lme_streamdata?id={tmdbId}&serial=1&s={s}&t={i}&title={HttpUtility.UrlEncode(displayTitle)}&original_title={HttpUtility.UrlEncode(originalTitle)}";
                voiceTpl.Append($"Джерело #{i}", i == selectedIndex, voiceLink);
            }

            // Епізоди з посиланнями на вибране джерело
            var episodeTpl = new EpisodeTpl(episodeNumbers.Count);
            foreach (var epNum in episodeNumbers)
            {
                string episodeName = $"Епізод {epNum}";
                string callUrl = $"{host}/lite/lme_streamdata?id={tmdbId}&serial=1&s={s}&e={epNum}&play=true&t={selectedSource}&title={HttpUtility.UrlEncode(displayTitle)}&original_title={HttpUtility.UrlEncode(originalTitle)}";
                episodeTpl.Append(episodeName, displayTitle, s.ToString(), epNum.ToString("D2"), accsArgs(callUrl), "call");
            }

            episodeTpl.Append(voiceTpl);

            return Content(
                rjson ? episodeTpl.ToJson() : episodeTpl.ToHtml(),
                rjson ? "application/json; charset=utf-8" : "text/html; charset=utf-8"
            );
        }

        /// <summary>
        /// Обробка play-запиту: API з season/episode → JSON з вибраним джерелом
        /// </summary>
        private async Task<ActionResult> HandlePlay(StreamDataInvoke invoke, OnlinesSettings init, long tmdbId, int season, int episode, string title, string originalTitle, string t)
        {
            if (tmdbId <= 0 || season <= 0 || episode <= 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            var response = await invoke.GetEpisode(tmdbId, season, episode);
            if (response?.data?.stream_urls == null || response.data.stream_urls.Count == 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            var streamUrls = response.data.stream_urls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (streamUrls.Count == 0)
                return OnError("lme_streamdata", refresh_proxy: true);

            // Вибираємо джерело за індексом з voice-вкладки t (1-based)
            int sourceIndex = int.TryParse(t, out int si) && si >= 1 && si <= streamUrls.Count ? si - 1 : 0;
            string streamUrl = BuildStreamUrl(init, streamUrls[sourceIndex]);

            var subs = CollectSubtitles(response);
            string displayTitle = !string.IsNullOrEmpty(title) ? title : (!string.IsNullOrEmpty(originalTitle) ? originalTitle : response.data.title);

            return UpdateService.Validate(Content(
                VideoTpl.ToJson("play", streamUrl, displayTitle, subtitles: subs),
                "application/json; charset=utf-8"
            ));
        }

        /// <summary>
        /// Зібрати субтитри з відповіді API
        /// </summary>
        private SubtitleTpl CollectSubtitles(StreamDataResponse response)
        {
            if (response?.default_subs == null || response.default_subs.Count == 0)
                return null;

            var tpl = new SubtitleTpl(response.default_subs.Count);
            foreach (var sub in response.default_subs)
            {
                if (!string.IsNullOrWhiteSpace(sub?.url) && !string.IsNullOrWhiteSpace(sub?.lang))
                {
                    tpl.Append(sub.lang, sub.url);
                }
            }

            return tpl;
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
        {
            string link = StripLampacArgs(streamLink?.Trim());
            if (string.IsNullOrEmpty(link))
                return link;

            if (ApnHelper.IsEnabled(init))
            {
                if (ModInit.ApnHostProvided)
                    return ApnHelper.WrapUrl(init, link);

                var noApn = (OnlinesSettings)init.Clone();
                noApn.apnstream = false;
                noApn.apn = null;
                return HostStreamProxy(noApn, link);
            }

            return HostStreamProxy(init, link);
        }

        private static string StripLampacArgs(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                url,
                @"([?&])(account_email|uid|nws_id)=[^&]*",
                "$1",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            cleaned = cleaned.Replace("?&", "?").Replace("&&", "&").TrimEnd('?', '&');
            return cleaned;
        }

        private static bool IsCheckOnlineSearchEnabled()
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

        private static void OnLog(string message)
        {
            System.Console.WriteLine(message);
        }
    }
}
