using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using LME.AniWorld.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace LME.AniWorld.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.AniWorld);
        }

        [HttpGet]
        [Route("lite/lme_aniworld")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.AniWorld);
            if (!init.enable)
                return Forbid();

            var invoke = new AniWorldInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            if (checksearch)
            {
                if (!StreamHelper.IsCheckOnlineSearchEnabled())
                    return OnError("lme_aniworld", refresh_proxy: true);

                var searchResults = await invoke.Search(original_title, year, serial);
                if (searchResults != null && searchResults.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("lme_aniworld", refresh_proxy: true);
            }

            int catalogId = 0;
            if (!string.IsNullOrEmpty(href) && int.TryParse(href, out int parsedId))
            {
                catalogId = parsedId;
            }

            if (catalogId == 0)
            {
                var searchResults = await invoke.Search(original_title, year, serial);
                if (searchResults == null || searchResults.Count == 0)
                    return OnError("lme_aniworld", refresh_proxy: true);

                if (searchResults.Count == 1)
                {
                    // Один результат — одразу переходимо до епізодів
                    catalogId = searchResults[0].Id;
                }
                else
                {
                    // Перевіряємо, чи всі результати мають однаковий original_title
                    // Якщо так — це багатосезонний тайтл, показуємо SeasonTpl
                    var sameTitle = searchResults.All(r =>
                        r.OriginalTitle.Equals(searchResults[0].OriginalTitle, StringComparison.OrdinalIgnoreCase));

                    if (sameTitle)
                    {
                        // Багатосезонний тайтл — показуємо сезони
                        var season_tpl = new SeasonTpl(searchResults.Count);
                        foreach (var res in searchResults)
                        {
                            string link = $"{host}/lite/lme_aniworld?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={res.Id}";

                            // Для SPECIAL показуємо "[SPECIAL] title", для інших — тільки номер сезону
                            string seasonName = GetSeasonDisplayName(res);
                            string seasonNumber = ExtractSeasonNumber(res.Title).ToString();
                            season_tpl.Append(seasonName, link, seasonNumber);
                        }

                        return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
                    }
                    else
                    {
                        // Різні тайтли — показуємо SimilarTpl
                        var similar_tpl = new SimilarTpl(searchResults.Count);
                        foreach (var res in searchResults)
                        {
                            string link = $"{host}/lite/lme_aniworld?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={res.Id}";
                            similar_tpl.Append($"{res.Title} ({res.ReleaseYear})", res.MediaType, string.Empty, link);
                        }

                        return rjson ? Content(similar_tpl.ToJson(), "application/json; charset=utf-8") : Content(similar_tpl.ToHtml(), "text/html; charset=utf-8");
                    }
                }
            }

            var detail = await invoke.GetDetail(catalogId);
            if (detail == null || detail.Episodes == null || detail.Episodes.Count == 0)
                return OnError("lme_aniworld", refresh_proxy: true);

            if (serial == 1)
            {
                // API повертає плоский список епізодів
                // Сортуємо по номеру епізоду (від меньшого до більшого)
                var sortedEpisodes = detail.Episodes
                    .OrderBy(e => e.Episode)
                    .ToList();

                var episode_tpl = new EpisodeTpl();
                foreach (var ep in sortedEpisodes)
                {
                    string episodeName = $"Серія {ep.Episode}";
                    string callUrl = $"{host}/lite/lme_aniworld/play?episode_id={ep.Id}&title={HttpUtility.UrlEncode(title ?? original_title)}";
                    episode_tpl.Append(episodeName, title ?? original_title, "1", ep.Episode.ToString("D2"), accsArgs(callUrl), "call");
                }

                if (rjson)
                    return Content(episode_tpl.ToJson(), "application/json; charset=utf-8");

                return Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
            }
            else
            {
                // Фільм — отримуємо перший епізод
                var firstEpisode = detail.Episodes.FirstOrDefault();
                if (firstEpisode == null)
                    return OnError("lme_aniworld", refresh_proxy: true);

                var episodeSource = await invoke.GetEpisodeSource(firstEpisode.Id);
                if (episodeSource == null)
                    return OnError("lme_aniworld", refresh_proxy: true);

                if (episodeSource.Type == StreamType.Dailymotion)
                {
                    // Dailymotion — відкладений резолв через call
                    string callUrl = $"{host}/lite/lme_aniworld/play?episode_id={firstEpisode.Id}&title={HttpUtility.UrlEncode(title ?? original_title)}";
                    var movie_tpl = new MovieTpl(title, original_title);
                    movie_tpl.Append(title ?? original_title, accsArgs(callUrl), "call");
                    return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
                }
                else if (episodeSource.Type == StreamType.Mediadelivery)
                {
                    // Mediadelivery — прямий URL
                    string streamUrl = await invoke.GetMediadeliveryStreamUrl(episodeSource.Url);
                    if (string.IsNullOrEmpty(streamUrl))
                        return OnError("lme_aniworld", refresh_proxy: true);

                    var movie_tpl = new MovieTpl(title, original_title);
                    movie_tpl.Append(title ?? original_title, BuildStreamUrl(init, streamUrl));
                    return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
                }

                return OnError("lme_aniworld", refresh_proxy: true);
            }
        }

        [HttpGet("lite/lme_aniworld/play")]
        async public Task<ActionResult> Play(int episode_id, string title)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.AniWorld);
            if (!init.enable)
                return Forbid();

            var invoke = new AniWorldInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            if (episode_id <= 0)
                return OnError("lme_aniworld", refresh_proxy: true);

            var episodeSource = await invoke.GetEpisodeSource(episode_id);
            if (episodeSource == null)
                return OnError("lme_aniworld", refresh_proxy: true);

            if (episodeSource.Type == StreamType.Dailymotion)
            {
                // Dailymotion — отримуємо якості
                string videoId = AniWorldInvoke.ExtractDailymotionVideoId(episodeSource.Url);
                if (string.IsNullOrEmpty(videoId))
                    return OnError("lme_aniworld", refresh_proxy: true);

                var qualities = await invoke.GetDailymotionQualities(videoId);
                if (qualities == null || qualities.Count == 0)
                    return OnError("lme_aniworld", refresh_proxy: true);

                var streamQuality = new StreamQualityTpl();
                foreach (var (quality, url) in qualities)
                {
                    string proxiedUrl = BuildStreamUrl(init, url);
                    streamQuality.Append(proxiedUrl, quality);
                }

                if (!streamQuality.Any())
                    return OnError("lme_aniworld", refresh_proxy: true);

                var first = streamQuality.Firts();
                string json = VideoTpl.ToJson("play", first.link, title ?? string.Empty, streamquality: streamQuality);
                return UpdateService.Validate(Content(json, "application/json; charset=utf-8"));
            }
            else if (episodeSource.Type == StreamType.Mediadelivery)
            {
                // Mediadelivery — прямий URL
                string streamUrl = await invoke.GetMediadeliveryStreamUrl(episodeSource.Url);
                if (string.IsNullOrEmpty(streamUrl))
                    return OnError("lme_aniworld", refresh_proxy: true);

                string json = VideoTpl.ToJson("play", BuildStreamUrl(init, streamUrl), title ?? string.Empty);
                return UpdateService.Validate(Content(json, "application/json; charset=utf-8"));
            }

            return OnError("lme_aniworld", refresh_proxy: true);
        }

        /// <summary>
        /// Отримання назви сезону для відображення
        /// </summary>
        private static string GetSeasonDisplayName(AniWorldSearchResult res)
        {
            // Для SPECIAL показуємо "[SPECIAL] title"
            if (res.MediaType == "SPECIAL")
                return $"[SPECIAL] {res.Title}";

            // Для інших — намагаємося витягти номер з назви
            int seasonNum = ExtractSeasonNumber(res.Title);
            if (seasonNum > 0)
                return seasonNum.ToString();

            // Якщо не вдалося витягти номер — показуємо назву
            return res.Title;
        }

        /// <summary>
        /// Витягування номера сезону з назви
        /// </summary>
        private static int ExtractSeasonNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return 0;

            // Шукаємо "Сезон X" або "Season X"
            var match = Regex.Match(title, @"(?:Сезон|Season)\s*(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                return num;

            return 0;
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
            => StreamHelper.BuildStreamUrl(init, streamLink, ModInit.ApnHostProvided, (s, l) => HostStreamProxy(s, l));

        private static void OnLog(string message)
        {
            System.Console.WriteLine(message);
        }
    }
}
