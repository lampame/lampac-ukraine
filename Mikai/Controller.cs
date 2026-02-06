using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Mikai.Models;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace Mikai.Controllers
{
    public class Controller : BaseOnlineController
    {
        private readonly ProxyManager _proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            _proxyManager = new ProxyManager(ModInit.Mikai);
        }

        [HttpGet]
        [Route("mikai")]
        public async Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.Mikai);
            if (!init.enable)
                return Forbid();

            var invoke = new MikaiInvoke(init, hybridCache, OnLog, _proxyManager);

            if (checksearch)
            {
                if (AppInit.conf?.online?.checkOnlineSearch != true)
                    return OnError("mikai", _proxyManager);

                var checkResults = await invoke.Search(title, original_title, year);
                if (checkResults != null && checkResults.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("mikai", _proxyManager);
            }

            OnLog($"Mikai Index: title={title}, original_title={original_title}, serial={serial}, s={s}, t={t}, year={year}");

            var searchResults = await invoke.Search(title, original_title, year);
            if (searchResults == null || searchResults.Count == 0)
                return OnError("mikai", _proxyManager);

            var selected = searchResults.FirstOrDefault();
            if (selected == null)
                return OnError("mikai", _proxyManager);

            var details = await invoke.GetDetails(selected.Id);
            if (details == null || details.Players == null || details.Players.Count == 0)
                return OnError("mikai", _proxyManager);

            bool isSerial = serial == 1 || (serial == -1 && !string.Equals(details.Format, "movie", StringComparison.OrdinalIgnoreCase));
            var seasonDetails = await CollectSeasonDetails(details, invoke);
            var voices = BuildVoices(seasonDetails);
            if (voices.Count == 0)
                return OnError("mikai", _proxyManager);

            string displayTitle = title ?? details.Details?.Names?.Name ?? original_title;

            if (isSerial)
            {
                MikaiVoiceInfo voiceForSeasons = null;
                bool restrictByVoice = !string.IsNullOrEmpty(t) && voices.TryGetValue(t, out voiceForSeasons);
                var seasonNumbers = restrictByVoice
                    ? GetSeasonSet(voiceForSeasons).OrderBy(n => n).ToList()
                    : voices.Values
                        .SelectMany(v => GetSeasonSet(v))
                        .Distinct()
                        .OrderBy(n => n)
                        .ToList();

                if (seasonNumbers.Count == 0)
                    return OnError("mikai", _proxyManager);

                if (s == -1)
                {
                    var seasonTpl = new SeasonTpl(seasonNumbers.Count);
                    foreach (var seasonNumber in seasonNumbers)
                    {
                        string link = $"{host}/mikai?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={seasonNumber}";
                        if (restrictByVoice)
                            link += $"&t={HttpUtility.UrlEncode(t)}";
                        seasonTpl.Append($"{seasonNumber}", link, seasonNumber.ToString());
                    }

                    return rjson
                        ? Content(seasonTpl.ToJson(), "application/json; charset=utf-8")
                        : Content(seasonTpl.ToHtml(), "text/html; charset=utf-8");
                }

                var voicesForSeason = voices
                    .Where(v => v.Value.Seasons.ContainsKey(s))
                    .ToList();

                if (!voicesForSeason.Any())
                    return OnError("mikai", _proxyManager);

                if (string.IsNullOrEmpty(t))
                    t = voicesForSeason[0].Key;
                else if (!voices.ContainsKey(t))
                    t = voicesForSeason[0].Key;

                var voiceTpl = new VoiceTpl();
                var selectedVoiceInfo = voices[t];
                var selectedSeasonSet = GetSeasonSet(selectedVoiceInfo);
                foreach (var voice in voicesForSeason)
                {
                    var targetSeasonSet = GetSeasonSet(voice.Value);
                    bool sameSeasonSet = targetSeasonSet.SetEquals(selectedSeasonSet);
                    string voiceLink = $"{host}/mikai?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1";
                    if (sameSeasonSet)
                        voiceLink += $"&s={s}&t={HttpUtility.UrlEncode(voice.Key)}";
                    else
                        voiceLink += $"&s=-1&t={HttpUtility.UrlEncode(voice.Key)}";
                    voiceTpl.Append(voice.Key, voice.Key == t, voiceLink);
                }

                if (!voices.ContainsKey(t) || !voices[t].Seasons.ContainsKey(s))
                {
                    string redirectUrl = $"{host}/mikai?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s=-1&t={HttpUtility.UrlEncode(t)}";
                    return Redirect(redirectUrl);
                }

                var episodeTpl = new EpisodeTpl();
                foreach (var ep in voices[t].Seasons[s].OrderBy(e => e.Number))
                {
                    string episodeName = string.IsNullOrEmpty(ep.Title) ? $"Епізод {ep.Number}" : ep.Title;
                    string streamLink = ep.Url;

                    if (string.IsNullOrEmpty(streamLink))
                        continue;

                    if (NeedsResolve(voices[t].ProviderName, streamLink))
                    {
                        string callUrl = $"{host}/mikai/play?url={HttpUtility.UrlEncode(streamLink)}&title={HttpUtility.UrlEncode(displayTitle)}";
                        episodeTpl.Append(episodeName, displayTitle, s.ToString(), ep.Number.ToString(), accsArgs(callUrl), "call");
                    }
                    else
                    {
                        string playUrl = BuildStreamUrl(init, streamLink, headers: null, forceProxy: false);
                        episodeTpl.Append(episodeName, displayTitle, s.ToString(), ep.Number.ToString(), playUrl);
                    }
                }

                episodeTpl.Append(voiceTpl);
                if (rjson)
                    return Content(episodeTpl.ToJson(), "application/json; charset=utf-8");

                return Content(episodeTpl.ToHtml(), "text/html; charset=utf-8");
            }

            var movieTpl = new MovieTpl(displayTitle, original_title);
            foreach (var voice in voices.Values)
            {
                var episode = voice.Seasons.Values.SelectMany(v => v).OrderBy(e => e.Number).FirstOrDefault();
                if (episode == null || string.IsNullOrEmpty(episode.Url))
                    continue;

                if (NeedsResolve(voice.ProviderName, episode.Url))
                {
                    string callUrl = $"{host}/mikai/play?url={HttpUtility.UrlEncode(episode.Url)}&title={HttpUtility.UrlEncode(displayTitle)}";
                    movieTpl.Append(voice.DisplayName, accsArgs(callUrl), "call");
                }
                else
                {
                    string playUrl = BuildStreamUrl(init, episode.Url, headers: null, forceProxy: false);
                    movieTpl.Append(voice.DisplayName, playUrl);
                }
            }

            if (movieTpl.data == null || movieTpl.data.Count == 0)
                return OnError("mikai", _proxyManager);

            return rjson
                ? Content(movieTpl.ToJson(), "application/json; charset=utf-8")
                : Content(movieTpl.ToHtml(), "text/html; charset=utf-8");
        }

        [HttpGet("mikai/play")]
        public async Task<ActionResult> Play(string url, string title = null)
        {
            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.Mikai);
            if (!init.enable)
                return Forbid();

            if (string.IsNullOrEmpty(url))
                return OnError("mikai", _proxyManager);

            var invoke = new MikaiInvoke(init, hybridCache, OnLog, _proxyManager);
            OnLog($"Mikai Play: url={url}");

            string streamLink = await invoke.ResolveVideoUrl(url);
            if (string.IsNullOrEmpty(streamLink))
                return OnError("mikai", _proxyManager);

            List<HeadersModel> streamHeaders = null;
            bool forceProxy = false;
            if (streamLink.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase))
            {
                streamHeaders = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", "https://ashdi.vip/")
                };
                forceProxy = true;
            }

            string streamUrl = BuildStreamUrl(init, streamLink, streamHeaders, forceProxy);
            string jsonResult = $"{{\"method\":\"play\",\"url\":\"{streamUrl}\",\"title\":\"{title ?? string.Empty}\"}}";
            return UpdateService.Validate(Content(jsonResult, "application/json; charset=utf-8"));
        }

        private async Task<List<MikaiAnime>> CollectSeasonDetails(MikaiAnime details, MikaiInvoke invoke)
        {
            var seasonDetails = new List<MikaiAnime>();
            if (details == null)
                return seasonDetails;

            seasonDetails.Add(details);

            if (details.Relations == null || details.Relations.Count == 0)
                return seasonDetails;

            var relationIds = details.Relations
                .Where(r => ShouldIncludeRelation(r?.RelationType))
                .Select(r => r?.Anime?.Id ?? 0)
                .Where(id => id > 0 && id != details.Id)
                .Distinct()
                .ToList();

            foreach (var relationId in relationIds)
            {
                var relationDetails = await invoke.GetDetails(relationId);
                if (relationDetails?.Players == null || relationDetails.Players.Count == 0)
                    continue;

                seasonDetails.Add(relationDetails);
            }

            return OrderSeasonDetails(seasonDetails);
        }

        private static bool ShouldIncludeRelation(string relationType)
        {
            if (string.IsNullOrWhiteSpace(relationType))
                return false;

            return relationType.Equals("other", StringComparison.OrdinalIgnoreCase) ||
                   relationType.Equals("parent", StringComparison.OrdinalIgnoreCase) ||
                   relationType.Equals("sequel", StringComparison.OrdinalIgnoreCase) ||
                   relationType.Equals("prequel", StringComparison.OrdinalIgnoreCase);
        }

        private static List<MikaiAnime> OrderSeasonDetails(List<MikaiAnime> seasonDetails)
        {
            return seasonDetails
                .Where(d => d != null)
                .GroupBy(d => d.Id)
                .Select(g => g.First())
                .OrderBy(d => d.Year > 0 ? d.Year : int.MaxValue)
                .ThenBy(d =>
                {
                    if (DateTime.TryParse(d.StartDate, out var parsed))
                        return parsed;

                    return DateTime.MaxValue;
                })
                .ThenBy(d => d.Id)
                .ToList();
        }

        private Dictionary<string, MikaiVoiceInfo> BuildVoices(List<MikaiAnime> seasonDetails)
        {
            var voices = new Dictionary<string, MikaiVoiceInfo>(StringComparer.OrdinalIgnoreCase);
            if (seasonDetails == null || seasonDetails.Count == 0)
                return voices;

            var voiceKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int seasonNumber = 1;

            foreach (var details in seasonDetails)
            {
                if (details?.Players == null || details.Players.Count == 0)
                {
                    seasonNumber++;
                    continue;
                }

                int totalProviders = details.Players.Sum(p => p?.Providers?.Count ?? 0);

                foreach (var player in details.Players)
                {
                    if (player?.Providers == null || player.Providers.Count == 0)
                        continue;

                    string teamName = player.Team?.Name;
                    if (string.IsNullOrWhiteSpace(teamName))
                        teamName = "Озвучка";

                    string baseName = player.IsSubs ? $"{teamName} (Субтитри)" : teamName;

                    int providerIndex = 0;
                    foreach (var provider in player.Providers)
                    {
                        providerIndex++;
                        if (provider?.Episodes == null || provider.Episodes.Count == 0)
                            continue;

                        string displayName = baseName;
                        if (totalProviders > 1 && !string.IsNullOrWhiteSpace(provider.Name))
                            displayName = $"[{provider.Name}] {displayName}";

                        string providerKey = string.IsNullOrWhiteSpace(provider.Name) ? $"provider-{providerIndex}" : provider.Name;
                        string voiceKey = $"{providerKey}|{teamName}|{player.IsSubs}";

                        if (!voiceKeyMap.TryGetValue(voiceKey, out var voiceName))
                        {
                            displayName = EnsureUniqueName(voices, displayName);
                            voiceKeyMap[voiceKey] = displayName;
                            voices[displayName] = new MikaiVoiceInfo
                            {
                                DisplayName = displayName,
                                ProviderName = provider.Name,
                                IsSubs = player.IsSubs
                            };
                            voiceName = displayName;
                        }

                        var voice = voices[voiceName];

                        var episodes = new List<MikaiEpisodeInfo>();
                        int fallbackIndex = 1;
                        foreach (var ep in provider.Episodes.OrderBy(e => e.Number))
                        {
                            if (string.IsNullOrWhiteSpace(ep.PlayLink))
                                continue;

                            int number = ep.Number > 0 ? ep.Number : fallbackIndex++;
                            episodes.Add(new MikaiEpisodeInfo
                            {
                                Number = number,
                                Title = $"Епізод {number}",
                                Url = ep.PlayLink
                            });
                        }

                        if (episodes.Count == 0)
                            continue;

                        voice.Seasons[seasonNumber] = episodes;
                    }
                }

                seasonNumber++;
            }

            return voices;
        }

        private static string EnsureUniqueName(Dictionary<string, MikaiVoiceInfo> voices, string name)
        {
            if (!voices.ContainsKey(name))
                return name;

            int index = 2;
            string candidate = $"{name} {index}";
            while (voices.ContainsKey(candidate))
            {
                index++;
                candidate = $"{name} {index}";
            }

            return candidate;
        }

        private static bool NeedsResolve(string providerName, string streamLink)
        {
            if (!string.IsNullOrEmpty(providerName))
            {
                if (providerName.Equals("ASHDI", StringComparison.OrdinalIgnoreCase) ||
                    providerName.Equals("MOONANIME", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return streamLink.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase) ||
                   streamLink.Contains("moonanime.art", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<int> GetSeasonSet(MikaiVoiceInfo voice)
        {
            if (voice?.Seasons == null || voice.Seasons.Count == 0)
                return new HashSet<int>();

            return voice.Seasons
                .Where(kv => kv.Value != null && kv.Value.Any(ep => !string.IsNullOrEmpty(ep.Url)))
                .Select(kv => kv.Key)
                .ToHashSet();
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

        private string BuildStreamUrl(OnlinesSettings init, string streamLink, List<HeadersModel> headers, bool forceProxy)
        {
            string link = streamLink?.Trim();
            if (string.IsNullOrEmpty(link))
                return link;

            link = StripLampacArgs(link);

            if (ApnHelper.IsEnabled(init))
            {
                if (ModInit.ApnHostProvided || ApnHelper.IsAshdiUrl(link))
                    return ApnHelper.WrapUrl(init, link);

                var noApn = (OnlinesSettings)init.Clone();
                noApn.apnstream = false;
                noApn.apn = null;
                return HostStreamProxy(noApn, link, headers: headers, force_streamproxy: forceProxy);
            }

            return HostStreamProxy(init, link, headers: headers, force_streamproxy: forceProxy);
        }
    }
}
