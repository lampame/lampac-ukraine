using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using UafilmME.Models;

namespace UafilmME.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.UafilmME);
        }

        [HttpGet]
        [Route("lite/uafilmme")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.UafilmME);
            if (!init.enable)
                return Forbid();

            var invoke = new UafilmMEInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            if (checksearch)
            {
                if (!IsCheckOnlineSearchEnabled())
                    return OnError("uafilmme", refresh_proxy: true);

                var searchResults = await invoke.Search(title, original_title, year);
                if (searchResults != null && searchResults.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("uafilmme", refresh_proxy: true);
            }

            long titleId = 0;
            long.TryParse(href, out titleId);

            if (titleId <= 0)
            {
                var searchResults = await invoke.Search(title, original_title, year);
                if (searchResults == null || searchResults.Count == 0)
                {
                    OnLog("UafilmME: пошук нічого не повернув.");
                    return OnError("uafilmme", refresh_proxy: true);
                }

                var best = invoke.SelectBestSearchResult(searchResults, id, imdb_id, title, original_title, year, serial);
                var ordered = searchResults
                    .OrderByDescending(r => r.MatchScore)
                    .ThenByDescending(r => r.Year)
                    .ToList();

                var second = ordered.Skip(1).FirstOrDefault();
                if (!IsConfidentMatch(best, second, id, imdb_id, serial))
                {
                    var similarTpl = new SimilarTpl(ordered.Count);
                    foreach (var item in ordered.Take(60))
                    {
                        string details = item.IsSeries ? "Серіал" : "Фільм";
                        string itemYear = item.Year > 1900 ? item.Year.ToString() : string.Empty;
                        string link = $"{host}/lite/uafilmme?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={item.Id}";
                        similarTpl.Append(item.Name, itemYear, details, link, item.Poster);
                    }

                    OnLog($"UafilmME: кілька схожих збігів, повертаю SimilarTpl ({ordered.Count}).");
                    return rjson
                        ? Content(similarTpl.ToJson(), "application/json; charset=utf-8")
                        : Content(similarTpl.ToHtml(), "text/html; charset=utf-8");
                }

                titleId = best?.Id ?? 0;
            }

            if (titleId <= 0)
            {
                OnLog("UafilmME: не вдалося визначити title_id.");
                return OnError("uafilmme", refresh_proxy: true);
            }

            if (serial == 1)
            {
                if (s == -1)
                {
                    var seasons = await invoke.GetAllSeasons(titleId);
                    if (seasons == null || seasons.Count == 0)
                    {
                        OnLog($"UafilmME: сезони не знайдено для title_id={titleId}.");
                        return OnError("uafilmme", refresh_proxy: true);
                    }

                    var seasonTpl = new SeasonTpl(seasons.Count);
                    foreach (var season in seasons)
                    {
                        string seasonName = season.EpisodesCount > 0
                            ? $"Сезон {season.Number} ({season.EpisodesCount} еп.)"
                            : $"Сезон {season.Number}";

                        string link = $"{host}/lite/uafilmme?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={season.Number}&href={titleId}";
                        seasonTpl.Append(seasonName, link, season.Number.ToString());
                    }

                    return rjson
                        ? Content(seasonTpl.ToJson(), "application/json; charset=utf-8")
                        : Content(seasonTpl.ToHtml(), "text/html; charset=utf-8");
                }

                if (s <= 0)
                {
                    OnLog($"UafilmME: некоректний номер сезону s={s}.");
                    return OnError("uafilmme", refresh_proxy: true);
                }

                var episodes = await invoke.GetSeasonEpisodes(titleId, s);
                if (episodes == null || episodes.Count == 0)
                {
                    OnLog($"UafilmME: епізоди не знайдено для title_id={titleId}, season={s}.");
                    return OnError("uafilmme", refresh_proxy: true);
                }

                var episodeTpl = new EpisodeTpl();
                int appended = 0;
                int fallbackEpisodeNumber = 1;

                foreach (var episode in episodes)
                {
                    if (episode.PrimaryVideoId <= 0)
                        continue;

                    int episodeNumber = episode.EpisodeNumber > 0 ? episode.EpisodeNumber : fallbackEpisodeNumber;
                    string episodeName = !string.IsNullOrWhiteSpace(episode.Name)
                        ? episode.Name
                        : $"Епізод {episodeNumber}";

                    string callUrl = $"{host}/lite/uafilmme/play?video_id={episode.PrimaryVideoId}&title_id={titleId}&s={s}&e={episodeNumber}&title={HttpUtility.UrlEncode(title ?? original_title)}";
                    episodeTpl.Append(episodeName, title ?? original_title, s.ToString(), episodeNumber.ToString("D2"), accsArgs(callUrl), "call");

                    fallbackEpisodeNumber = Math.Max(fallbackEpisodeNumber, episodeNumber + 1);
                    appended++;
                }

                if (appended == 0)
                {
                    OnLog($"UafilmME: у сезоні {s} немає епізодів з playable video_id.");
                    return OnError("uafilmme", refresh_proxy: true);
                }

                return rjson
                    ? Content(episodeTpl.ToJson(), "application/json; charset=utf-8")
                    : Content(episodeTpl.ToHtml(), "text/html; charset=utf-8");
            }
            else
            {
                var videos = await invoke.GetMovieVideos(titleId);
                if (videos == null || videos.Count == 0)
                {
                    OnLog($"UafilmME: не знайдено відео для фільму title_id={titleId}.");
                    return OnError("uafilmme", refresh_proxy: true);
                }

                var movieTpl = new MovieTpl(title, original_title, videos.Count);
                int index = 1;
                foreach (var video in videos)
                {
                    string label = BuildVideoLabel(video, index);
                    string callUrl = $"{host}/lite/uafilmme/play?video_id={video.Id}&title_id={titleId}&title={HttpUtility.UrlEncode(title ?? original_title)}";
                    movieTpl.Append(label, accsArgs(callUrl), "call");
                    index++;
                }

                return rjson
                    ? Content(movieTpl.ToJson(), "application/json; charset=utf-8")
                    : Content(movieTpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        [HttpGet]
        [Route("lite/uafilmme/play")]
        async public Task<ActionResult> Play(long video_id, long title_id = 0, int s = 0, int e = 0, string title = null)
        {
            await UpdateService.ConnectAsync(host);

            if (video_id <= 0)
                return OnError("uafilmme", refresh_proxy: true);

            var init = loadKit(ModInit.UafilmME);
            if (!init.enable)
                return Forbid();

            var invoke = new UafilmMEInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);
            var watch = await invoke.GetWatch(video_id);
            var videos = invoke.CollectPlayableVideos(watch);
            if (videos == null || videos.Count == 0)
            {
                OnLog($"UafilmME Play: watch/{video_id} не повернув playable stream.");
                return OnError("uafilmme", refresh_proxy: true);
            }

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", init.host)
            };

            var streamQuality = new StreamQualityTpl();
            foreach (var video in videos)
            {
                string streamUrl = BuildStreamUrl(init, video.Src, headers, forceProxy: true);
                if (string.IsNullOrWhiteSpace(streamUrl))
                    continue;

                string label = BuildVideoLabel(video, 0);
                streamQuality.Append(streamUrl, label);
            }

            var first = streamQuality.Firts();
            if (string.IsNullOrWhiteSpace(first.link))
            {
                OnLog($"UafilmME Play: не вдалося зібрати streamquality для video_id={video_id}.");
                return OnError("uafilmme", refresh_proxy: true);
            }

            string videoTitle = !string.IsNullOrWhiteSpace(title)
                ? title
                : videos.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v.Name))?.Name ?? string.Empty;

            return UpdateService.Validate(
                Content(
                    VideoTpl.ToJson("play", first.link, videoTitle, streamquality: streamQuality),
                    "application/json; charset=utf-8"
                )
            );
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink, List<HeadersModel> headers, bool forceProxy)
        {
            string link = StripLampacArgs(streamLink?.Trim());
            if (string.IsNullOrEmpty(link))
                return link;

            if (ApnHelper.IsEnabled(init))
            {
                if (ModInit.ApnHostProvided || ApnHelper.IsAshdiUrl(link))
                    return ApnHelper.WrapUrl(init, link);

                var noApn = (OnlinesSettings)init.Clone();
                noApn.apnstream = false;
                noApn.apn = null;
                return HostStreamProxy(noApn, link, headers: headers, force_streamproxy: forceProxy, proxy: proxyManager.Get());
            }

            return HostStreamProxy(init, link, headers: headers, force_streamproxy: forceProxy, proxy: proxyManager.Get());
        }

        private static bool IsConfidentMatch(UafilmSearchItem best, UafilmSearchItem second, long tmdbId, string imdbId, int serial)
        {
            if (best == null)
                return false;

            bool sameTmdb = tmdbId > 0 && best.TmdbId == tmdbId;
            bool sameImdb = !string.IsNullOrWhiteSpace(imdbId)
                && !string.IsNullOrWhiteSpace(best.ImdbId)
                && string.Equals(best.ImdbId.Trim(), imdbId.Trim(), StringComparison.OrdinalIgnoreCase);

            if (sameTmdb || sameImdb)
                return true;

            if (serial == 1 && !best.IsSeries)
                return false;

            int secondScore = second?.MatchScore ?? int.MinValue;
            return best.MatchScore >= 65 && best.MatchScore - secondScore >= 10;
        }

        private static string BuildVideoLabel(UafilmVideoItem video, int index)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(video?.Name))
                parts.Add(video.Name.Trim());

            if (!string.IsNullOrWhiteSpace(video?.Quality))
                parts.Add(video.Quality.Trim());

            if (!string.IsNullOrWhiteSpace(video?.Language))
                parts.Add(video.Language.Trim());

            if (parts.Count == 0)
                return index > 0 ? $"Варіант {index}" : "Потік";

            return string.Join(" • ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
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
