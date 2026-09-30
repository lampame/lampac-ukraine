using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using LME.Bamboo.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace LME.Bamboo.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.Bamboo);
        }

        [HttpGet]
        [Route("lite/lme_bamboo")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.Bamboo);
            if (!init.enable)
                return Forbid();

            var invoke = new BambooInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            if (checksearch)
            {
                if (!StreamHelper.IsCheckOnlineSearchEnabled())
                    return OnError("lme_bamboo", refresh_proxy: true);

                var searchResults = await invoke.Search(title, original_title);
                if (searchResults != null && searchResults.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("lme_bamboo", refresh_proxy: true);
            }

            string itemUrl = href;
            SearchResult picked = null;
            if (string.IsNullOrEmpty(itemUrl))
            {
                var searchResults = await invoke.Search(title, original_title);
                if (searchResults == null || searchResults.Count == 0)
                    return OnError("lme_bamboo", refresh_proxy: true);

                if (searchResults.Count > 1)
                {
                    var similar_tpl = new SimilarTpl(searchResults.Count);
                    foreach (var res in searchResults)
                    {
                        string link = $"{host}/lite/lme_bamboo?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(res.Url)}";
                        similar_tpl.Append(res.Title, string.Empty, string.Empty, link, res.Poster);
                    }

                    return rjson ? Content(similar_tpl.ToJson(), "application/json; charset=utf-8") : Content(similar_tpl.ToHtml(), "text/html; charset=utf-8");
                }

                picked = searchResults[0];
                itemUrl = picked.Url;
            }

            // Тип контенту задає клієнт (serial). Підказка з картки — лише фолбек,
            // бо сам URL (/dorama/) тип не визначає.
            bool isSerial = serial == 1 || (picked != null && picked.IsSeriesHint && !picked.IsMovieHint);

            if (isSerial)
                return await HandleSeries(invoke, init, itemUrl, imdb_id, kinopoisk_id, title, original_title, year, t, s, rjson);

            return await HandleMovie(invoke, init, itemUrl, title, original_title, rjson);
        }

        /// <summary>
        /// Серіал: VoiceTpl (перемикач озвучок) + EpisodeTpl одразу разом —
        /// standalone VoiceTpl клієнт Lampa не рендерить (див. патерн KlonFUN/Franko).
        /// </summary>
        private async Task<ActionResult> HandleSeries(BambooInvoke invoke, OnlinesSettings init, string itemUrl, string imdb_id, long kinopoisk_id, string title, string original_title, int year, string t, int s, bool rjson)
        {
            var series = await invoke.GetSeriesEpisodes(itemUrl);
            if (series == null || series.Voices.Count == 0)
                return OnError("lme_bamboo", refresh_proxy: true);

            var selected = series.Voices.FirstOrDefault(v => v.Key == t) ?? series.Voices[0];
            var voice_tpl = new VoiceTpl();

            foreach (var voice in series.Voices)
            {
                // href передаємо, щоб перемикання озвучки не запускало пошук заново
                // (інакше клієнт може впасти в SimilarTpl).
                string voiceLink = BuildSeriesLink(itemUrl, imdb_id, kinopoisk_id, title, original_title, year, voice.Key, s);
                voice_tpl.Append(voice.Name, voice.Key == selected.Key, voiceLink);
            }

            var episodes = selected.Episodes
                .OrderBy(e => e.Season)
                .ThenBy(e => e.Episode ?? int.MaxValue)
                .ToList();

            if (episodes.Count == 0)
                return OnError("lme_bamboo", refresh_proxy: true);

            var episode_tpl = new EpisodeTpl();
            int index = 1;
            foreach (var ep in episodes)
            {
                int season = ep.Season > 0 ? ep.Season : 1;
                int episodeNumber = ep.Episode ?? index;
                string episodeName = string.IsNullOrEmpty(ep.Title) ? $"Серія {episodeNumber}" : ep.Title;
                string streamUrl = BuildStreamUrl(init, ep.Url);

                episode_tpl.Append(
                    episodeName,
                    title ?? original_title,
                    season.ToString(),
                    episodeNumber.ToString("D2"),
                    streamUrl,
                    subtitles: ApnHelper.ParseSubtitles(ep.Subtitle),
                    voice_name: string.IsNullOrEmpty(selected.Name) ? null : selected.Name);

                index++;
            }

            episode_tpl.Append(voice_tpl);
            return rjson
                ? Content(episode_tpl.ToJson(), "application/json; charset=utf-8")
                : Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        /// <summary>
        /// Фільм: кожен голос окремим потоком (MovieTpl).
        /// </summary>
        private async Task<ActionResult> HandleMovie(BambooInvoke invoke, OnlinesSettings init, string itemUrl, string title, string original_title, bool rjson)
        {
            var streams = await invoke.GetMovieStreams(itemUrl);
            if (streams == null || streams.Count == 0)
                return OnError("lme_bamboo", refresh_proxy: true);

            var movie_tpl = new MovieTpl(title, original_title);
            int index = 1;
            foreach (var stream in streams)
            {
                string label = QualityHelper.BuildDisplayTitle(stream.Voice ?? stream.Title, stream.Url, index);
                movie_tpl.Append(
                    label,
                    BuildStreamUrl(init, stream.Url),
                    subtitles: ApnHelper.ParseSubtitles(stream.Subtitle),
                    voice_name: string.IsNullOrEmpty(stream.Voice) ? null : stream.Voice);

                index++;
            }

            return rjson
                ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8")
                : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        private string BuildSeriesLink(string href, string imdb_id, long kinopoisk_id, string title, string original_title, int year, string t, int s)
        {
            string link = $"{host}/lite/lme_bamboo?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}" +
                          $"&title={HttpUtility.UrlEncode(title ?? string.Empty)}" +
                          $"&original_title={HttpUtility.UrlEncode(original_title ?? string.Empty)}" +
                          $"&year={year}&serial=1&href={HttpUtility.UrlEncode(href ?? string.Empty)}" +
                          $"&t={HttpUtility.UrlEncode(t ?? string.Empty)}";

            if (s > 0)
                link += $"&s={s}";

            return link;
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
            => StreamHelper.BuildStreamUrl(init, streamLink, ModInit.ApnHostProvided, (s, l) => HostStreamProxy(s, l));

        private static void OnLog(string message)
        {
            System.Console.WriteLine(message);
        }
    }
}
