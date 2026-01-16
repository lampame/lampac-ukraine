using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using StarLight.Models;

namespace StarLight.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller()
        {
            proxyManager = new ProxyManager(ModInit.StarLight);
        }

        [HttpGet]
        [Route("starlight")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, int s = -1, bool rjson = false, string href = null)
        {
            var init = await loadKit(ModInit.StarLight);
            if (!init.enable)
                return Forbid();

            await StatsService.StatsAsync(host);
            if (TouchService.Touch(host))
            {
                return OnError(ErrorCodes.Touch, proxyManager);
            }

            var invoke = new StarLightInvoke(init, hybridCache, OnLog, proxyManager);

            string itemUrl = href;
            if (string.IsNullOrEmpty(itemUrl))
            {
                var searchResults = await invoke.Search(title, original_title);
                if (searchResults == null || searchResults.Count == 0)
                    return OnError("starlight", proxyManager);

                if (searchResults.Count > 1)
                {
                    var similar_tpl = new SimilarTpl(searchResults.Count);
                    foreach (var res in searchResults)
                    {
                        string link = $"{host}/starlight?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(res.Href)}";
                        similar_tpl.Append(res.Title, string.Empty, string.Empty, link, string.Empty);
                    }

                    return rjson ? Content(similar_tpl.ToJson(), "application/json; charset=utf-8") : Content(similar_tpl.ToHtml(), "text/html; charset=utf-8");
                }

                itemUrl = searchResults[0].Href;
            }

            var project = await invoke.GetProject(itemUrl);
            if (project == null)
                return OnError("starlight", proxyManager);

            if (serial == 1 && project.Seasons.Count > 0)
            {
                if (s == -1)
                {
                    var season_tpl = new SeasonTpl(project.Seasons.Count);
                    for (int i = 0; i < project.Seasons.Count; i++)
                    {
                        var seasonInfo = project.Seasons[i];
                        string seasonName = string.IsNullOrEmpty(seasonInfo.Title) ? $"Сезон {i + 1}" : seasonInfo.Title;
                        string link = $"{host}/starlight?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={i}&href={HttpUtility.UrlEncode(itemUrl)}";
                        season_tpl.Append(seasonName, link, i.ToString());
                    }

                    return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
                }

                if (s < 0 || s >= project.Seasons.Count)
                    return OnError("starlight", proxyManager);

                var season = project.Seasons[s];
                string seasonSlug = season.Slug;
                var episodes = invoke.GetEpisodes(project, seasonSlug);
                if (episodes == null || episodes.Count == 0)
                    return OnError("starlight", proxyManager);

                var episode_tpl = new EpisodeTpl();
                int index = 1;
                string seasonNumber = GetSeasonNumber(season, s);
                var orderedEpisodes = episodes
                    .Select(ep => new { Episode = ep, Number = GetEpisodeNumber(ep), Date = GetEpisodeDate(ep) })
                    .OrderBy(ep => ep.Number ?? int.MaxValue)
                    .ThenBy(ep => ep.Date ?? DateTime.MaxValue)
                    .Select(ep => ep.Episode)
                    .ToList();

                foreach (var ep in orderedEpisodes)
                {
                    if (string.IsNullOrEmpty(ep.Hash))
                        continue;

                    string episodeName = string.IsNullOrEmpty(ep.Title) ? $"Епізод {index}" : ep.Title;
                    string callUrl = $"{host}/starlight/play?hash={HttpUtility.UrlEncode(ep.Hash)}&title={HttpUtility.UrlEncode(title ?? original_title)}";
                    episode_tpl.Append(episodeName, title ?? original_title, seasonNumber, index.ToString("D2"), accsArgs(callUrl), "call");
                    index++;
                }

                return rjson ? Content(episode_tpl.ToJson(), "application/json; charset=utf-8") : Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
            }
            else
            {
                string hash = project.Hash;
                if (string.IsNullOrEmpty(hash) && project.Episodes.Count > 0)
                    hash = project.Episodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.Hash))?.Hash;

                if (string.IsNullOrEmpty(hash))
                    return OnError("starlight", proxyManager);

                string callUrl = $"{host}/starlight/play?hash={HttpUtility.UrlEncode(hash)}&title={HttpUtility.UrlEncode(title ?? original_title)}";
                var movie_tpl = new MovieTpl(title, original_title, 1);
                movie_tpl.Append(string.IsNullOrEmpty(title) ? "StarLight" : title, accsArgs(callUrl), "call");

                return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        [HttpGet]
        [Route("starlight/play")]
        async public Task<ActionResult> Play(string hash, string title)
        {
            if (string.IsNullOrEmpty(hash))
                return OnError("starlight", proxyManager);

            var init = await loadKit(ModInit.StarLight);
            if (!init.enable)
                return Forbid();

            await StatsService.StatsAsync(host);
            if (TouchService.Touch(host))
            {
                return OnError(ErrorCodes.Touch);
            }

            var invoke = new StarLightInvoke(init, hybridCache, OnLog, proxyManager);
            var result = await invoke.ResolveStream(hash);
            if (result == null || string.IsNullOrEmpty(result.Stream))
                return OnError("starlight", proxyManager);

            string streamUrl = HostStreamProxy(init, accsArgs(result.Stream), proxy: proxyManager.Get());
            string jsonResult = $"{{\"method\":\"play\",\"url\":\"{streamUrl}\",\"title\":\"{title ?? result.Name ?? ""}\"}}";
            return Content(jsonResult, "application/json; charset=utf-8");
        }

        private static string GetSeasonNumber(SeasonInfo season, int fallbackIndex)
        {
            if (season?.Title == null)
                return (fallbackIndex + 1).ToString();

            var digits = new string(season.Title.Where(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(digits) ? (fallbackIndex + 1).ToString() : digits;
        }

        private static int? GetEpisodeNumber(EpisodeInfo episode)
        {
            if (episode == null)
                return null;

            if (episode.Number.HasValue)
                return episode.Number.Value;

            if (string.IsNullOrEmpty(episode.Title))
                return null;

            var title = episode.Title;
            var markers = new[] { "випуск", "серия", "серія" };
            foreach (var marker in markers)
            {
                var markerIndex = title.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex <= 0)
                    continue;

                var prefix = title.Substring(0, markerIndex);
                var matches = Regex.Matches(prefix, "\\d+");
                if (matches.Count == 0)
                    continue;

                var last = matches[matches.Count - 1].Value;
                if (int.TryParse(last, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    return number;
            }

            return null;
        }

        private static DateTime? GetEpisodeDate(EpisodeInfo episode)
        {
            if (episode == null || string.IsNullOrEmpty(episode.Date))
                return null;

            if (DateTime.TryParseExact(episode.Date, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;

            return DateTime.TryParse(episode.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ? dt : null;
        }
    }
}
