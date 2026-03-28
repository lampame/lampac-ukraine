using Microsoft.AspNetCore.Mvc;
using NMoonAnime.Models;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace NMoonAnime.Controllers
{
    public class Controller : BaseOnlineController
    {
        private readonly ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.NMoonAnime);
        }

        [HttpGet]
        [Route("lite/nmoonanime")]
        public async Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string mal_id, string t, int s = -1, bool rjson = false, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.NMoonAnime);
            if (!init.enable)
                return Forbid();

            var invoke = new NMoonAnimeInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);
            string effectiveMalId = ResolveMalId(mal_id, kinopoisk_id, source);

            if (checksearch)
            {
                if (!IsCheckOnlineSearchEnabled())
                    return OnError("nmoonanime", refresh_proxy: true);

                var checkResults = await invoke.Search(imdb_id, effectiveMalId, title, year);
                if (checkResults != null && checkResults.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("nmoonanime", refresh_proxy: true);
            }

            OnLog($"NMoonAnime: назва={title}, source={source}, imdb={imdb_id}, kinopoisk_id(як mal_id)={kinopoisk_id}, mal_id_ефективний={effectiveMalId}, рік={year}, серіал={serial}, сезон={s}, озвучка={t}");

            var seasons = await invoke.Search(imdb_id, effectiveMalId, title, year);
            if (seasons == null || seasons.Count == 0)
                return OnError("nmoonanime", refresh_proxy: true);

            bool isSeries = serial == 1;
            NMoonAnimeSeasonContent firstSeasonData = null;

            if (serial == -1)
            {
                firstSeasonData = await invoke.GetSeasonContent(seasons[0]);
                if (firstSeasonData == null || firstSeasonData.Voices.Count == 0)
                    return OnError("nmoonanime", refresh_proxy: true);

                isSeries = firstSeasonData.IsSeries;
            }

            if (isSeries)
            {
                return await RenderSerial(invoke, seasons, imdb_id, kinopoisk_id, title, original_title, year, effectiveMalId, s, t, rjson);
            }

            return await RenderMovie(invoke, seasons, title, original_title, firstSeasonData, rjson);
        }

        [HttpGet("lite/nmoonanime/play")]
        public async Task<ActionResult> Play(string file, string title = null)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.NMoonAnime);
            if (!init.enable)
                return Forbid();

            if (string.IsNullOrWhiteSpace(file))
                return OnError("nmoonanime", refresh_proxy: true);

            var invoke = new NMoonAnimeInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);
            var streams = invoke.ParseStreams(file);
            if (streams == null || streams.Count == 0)
                return OnError("nmoonanime", refresh_proxy: true);

            if (streams.Count == 1)
            {
                string singleUrl = BuildStreamUrl(init, streams[0].Url);
                string singleJson = VideoTpl.ToJson("play", singleUrl, title ?? string.Empty, quality: streams[0].Quality ?? "auto");
                return UpdateService.Validate(Content(singleJson, "application/json; charset=utf-8"));
            }

            var streamQuality = new StreamQualityTpl();
            foreach (var stream in streams)
            {
                string streamUrl = BuildStreamUrl(init, stream.Url);
                streamQuality.Append(streamUrl, stream.Quality);
            }

            if (!streamQuality.Any())
                return OnError("nmoonanime", refresh_proxy: true);

            var first = streamQuality.Firts();
            string json = VideoTpl.ToJson("play", first.link, title ?? string.Empty, streamquality: streamQuality);
            return UpdateService.Validate(Content(json, "application/json; charset=utf-8"));
        }

        private async Task<ActionResult> RenderSerial(
            NMoonAnimeInvoke invoke,
            List<NMoonAnimeSeasonRef> seasons,
            string imdbId,
            long kinopoiskId,
            string title,
            string originalTitle,
            int year,
            string malId,
            int selectedSeason,
            string selectedVoice,
            bool rjson)
        {
            var orderedSeasons = seasons
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Url))
                .OrderBy(s => s.SeasonNumber)
                .ToList();

            if (orderedSeasons.Count == 0)
                return OnError("nmoonanime", refresh_proxy: true);

            if (selectedSeason == -1)
            {
                var seasonTpl = new SeasonTpl(orderedSeasons.Count);
                foreach (var season in orderedSeasons)
                {
                    int seasonNumber = season.SeasonNumber <= 0 ? 1 : season.SeasonNumber;
                    string seasonName = $"Сезон {seasonNumber}";
                    string seasonLink = BuildIndexUrl(imdbId, kinopoiskId, title, originalTitle, year, 1, malId, seasonNumber, selectedVoice);
                    seasonTpl.Append(seasonName, seasonLink, seasonNumber);
                }

                return rjson
                    ? Content(seasonTpl.ToJson(), "application/json; charset=utf-8")
                    : Content(seasonTpl.ToHtml(), "text/html; charset=utf-8");
            }

            var currentSeason = orderedSeasons.FirstOrDefault(s => s.SeasonNumber == selectedSeason) ?? orderedSeasons[0];
            var seasonData = await invoke.GetSeasonContent(currentSeason);
            if (seasonData == null)
                return OnError("nmoonanime", refresh_proxy: true);

            var voices = seasonData.Voices
                .Where(v => v != null && v.Episodes != null && v.Episodes.Count > 0)
                .ToList();

            if (voices.Count == 0)
                return OnError("nmoonanime", refresh_proxy: true);

            int activeVoiceIndex = ParseVoiceIndex(selectedVoice, voices.Count);
            var voiceTpl = new VoiceTpl(voices.Count);
            for (int i = 0; i < voices.Count; i++)
            {
                string voiceName = string.IsNullOrWhiteSpace(voices[i].Name) ? $"Озвучка {i + 1}" : voices[i].Name;
                string voiceLink = BuildIndexUrl(imdbId, kinopoiskId, title, originalTitle, year, 1, malId, currentSeason.SeasonNumber, i.ToString());
                voiceTpl.Append(voiceName, i == activeVoiceIndex, voiceLink);
            }

            var selectedVoiceData = voices[activeVoiceIndex];
            var episodes = selectedVoiceData.Episodes
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.File))
                .OrderBy(e => e.Number <= 0 ? int.MaxValue : e.Number)
                .ThenBy(e => e.Name)
                .ToList();

            if (episodes.Count == 0)
                return OnError("nmoonanime", refresh_proxy: true);

            string displayTitle = !string.IsNullOrWhiteSpace(title)
                ? title
                : !string.IsNullOrWhiteSpace(originalTitle)
                    ? originalTitle
                    : "NMoonAnime";

            var episodeTpl = new EpisodeTpl(episodes.Count);
            foreach (var episode in episodes)
            {
                int episodeNumber = episode.Number <= 0 ? 1 : episode.Number;
                string episodeName = string.IsNullOrWhiteSpace(episode.Name) ? $"Епізод {episodeNumber}" : episode.Name;
                string callUrl = $"{host}/lite/nmoonanime/play?file={HttpUtility.UrlEncode(episode.File)}&title={HttpUtility.UrlEncode(displayTitle)}";
                episodeTpl.Append(episodeName, displayTitle, currentSeason.SeasonNumber.ToString(), episodeNumber.ToString(), accsArgs(callUrl), "call");
            }

            episodeTpl.Append(voiceTpl);

            return rjson
                ? Content(episodeTpl.ToJson(), "application/json; charset=utf-8")
                : Content(episodeTpl.ToHtml(), "text/html; charset=utf-8");
        }

        private async Task<ActionResult> RenderMovie(
            NMoonAnimeInvoke invoke,
            List<NMoonAnimeSeasonRef> seasons,
            string title,
            string originalTitle,
            NMoonAnimeSeasonContent firstSeasonData,
            bool rjson)
        {
            var currentSeason = seasons
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Url))
                .OrderBy(s => s.SeasonNumber)
                .FirstOrDefault();

            if (currentSeason == null)
                return OnError("nmoonanime", refresh_proxy: true);

            NMoonAnimeSeasonContent seasonData = firstSeasonData;
            if (seasonData == null || !string.Equals(seasonData.Url, currentSeason.Url, StringComparison.OrdinalIgnoreCase))
                seasonData = await invoke.GetSeasonContent(currentSeason);

            if (seasonData == null || seasonData.Voices.Count == 0)
                return OnError("nmoonanime", refresh_proxy: true);

            string displayTitle = !string.IsNullOrWhiteSpace(title)
                ? title
                : !string.IsNullOrWhiteSpace(originalTitle)
                    ? originalTitle
                    : "NMoonAnime";

            var movieTpl = new MovieTpl(displayTitle, originalTitle);
            int fallbackIndex = 1;

            foreach (var voice in seasonData.Voices)
            {
                if (voice == null)
                    continue;

                string file = !string.IsNullOrWhiteSpace(voice.MovieFile)
                    ? voice.MovieFile
                    : voice.Episodes?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.File))?.File;

                if (string.IsNullOrWhiteSpace(file))
                    continue;

                string voiceName = string.IsNullOrWhiteSpace(voice.Name) ? $"Озвучка {fallbackIndex}" : voice.Name;
                string callUrl = $"{host}/lite/nmoonanime/play?file={HttpUtility.UrlEncode(file)}&title={HttpUtility.UrlEncode(displayTitle)}";
                movieTpl.Append(voiceName, accsArgs(callUrl), "call");
                fallbackIndex++;
            }

            if (movieTpl.IsEmpty)
                return OnError("nmoonanime", refresh_proxy: true);

            return rjson
                ? Content(movieTpl.ToJson(), "application/json; charset=utf-8")
                : Content(movieTpl.ToHtml(), "text/html; charset=utf-8");
        }

        private string BuildIndexUrl(string imdbId, long kinopoiskId, string title, string originalTitle, int year, int serial, string malId, int season, string voice)
        {
            var url = new StringBuilder();
            url.Append($"{host}/lite/nmoonanime?imdb_id={HttpUtility.UrlEncode(imdbId)}");
            url.Append($"&kinopoisk_id={kinopoiskId}");
            url.Append($"&title={HttpUtility.UrlEncode(title)}");
            url.Append($"&original_title={HttpUtility.UrlEncode(originalTitle)}");
            url.Append($"&year={year}");
            url.Append($"&serial={serial}");

            if (!string.IsNullOrWhiteSpace(malId))
                url.Append($"&mal_id={HttpUtility.UrlEncode(malId)}");

            if (season > 0)
                url.Append($"&s={season}");

            if (!string.IsNullOrWhiteSpace(voice))
                url.Append($"&t={HttpUtility.UrlEncode(voice)}");

            return url.ToString();
        }

        private int ParseVoiceIndex(string voiceValue, int totalVoices)
        {
            if (totalVoices <= 0)
                return 0;

            if (!int.TryParse(voiceValue, out int index))
                return 0;

            if (index < 0 || index >= totalVoices)
                return 0;

            return index;
        }

        private static string ResolveMalId(string malId, long kinopoiskId, string source)
        {
            if (!string.IsNullOrWhiteSpace(source) && source.Equals("tmdb", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!string.IsNullOrWhiteSpace(malId))
                return malId.Trim();

            if (!string.IsNullOrWhiteSpace(source) && source.Equals("hikka", StringComparison.OrdinalIgnoreCase) && kinopoiskId > 0)
                return kinopoiskId.ToString();

            return null;
        }

        private string BuildStreamUrl(OnlinesSettings init, string streamLink)
        {
            string link = StripLampacArgs(streamLink?.Trim());
            if (string.IsNullOrEmpty(link))
                return link;

            var headers = new List<HeadersModel>
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://moonanime.art/")
            };

            if (ApnHelper.IsEnabled(init))
            {
                if (ModInit.ApnHostProvided)
                    return ApnHelper.WrapUrl(init, link);

                var noApn = (OnlinesSettings)init.Clone();
                noApn.apnstream = false;
                noApn.apn = null;
                return HostStreamProxy(noApn, link, headers: headers, proxy: proxyManager.Get());
            }

            return HostStreamProxy(init, link, headers: headers, proxy: proxyManager.Get());
        }

        private static string StripLampacArgs(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            string cleaned = Regex.Replace(
                url,
                @"([?&])(account_email|uid|nws_id)=[^&]*",
                "$1",
                RegexOptions.IgnoreCase
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
