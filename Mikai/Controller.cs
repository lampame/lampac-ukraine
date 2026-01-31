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
        public async Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.Mikai);
            if (!init.enable)
                return Forbid();

            var invoke = new MikaiInvoke(init, hybridCache, OnLog, _proxyManager);
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
            var voices = BuildVoices(details);
            if (voices.Count == 0)
                return OnError("mikai", _proxyManager);

            string displayTitle = title ?? details.Details?.Names?.Name ?? original_title;

            if (isSerial)
            {
                const int seasonNumber = 1;
                if (s == -1)
                {
                    var seasonTpl = new SeasonTpl(1);
                    string link = $"{host}/mikai?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={seasonNumber}";
                    seasonTpl.Append($"{seasonNumber}", link, seasonNumber.ToString());

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

                var voiceTpl = new VoiceTpl();
                foreach (var voice in voicesForSeason)
                {
                    string voiceLink = $"{host}/mikai?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&t={HttpUtility.UrlEncode(voice.Key)}";
                    voiceTpl.Append(voice.Key, voice.Key == t, voiceLink);
                }

                if (!voices.ContainsKey(t) || !voices[t].Seasons.ContainsKey(s))
                    return OnError("mikai", _proxyManager);

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
                        string playUrl = HostStreamProxy(init, accsArgs(streamLink));
                        episodeTpl.Append(episodeName, displayTitle, s.ToString(), ep.Number.ToString(), playUrl);
                    }
                }

                if (rjson)
                    return Content(episodeTpl.ToJson(voiceTpl), "application/json; charset=utf-8");

                return Content(voiceTpl.ToHtml() + episodeTpl.ToHtml(), "text/html; charset=utf-8");
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
                    string playUrl = HostStreamProxy(init, accsArgs(episode.Url));
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

        private Dictionary<string, MikaiVoiceInfo> BuildVoices(MikaiAnime details)
        {
            var voices = new Dictionary<string, MikaiVoiceInfo>(StringComparer.OrdinalIgnoreCase);
            if (details?.Players == null)
                return voices;

            int totalProviders = details.Players.Sum(p => p?.Providers?.Count ?? 0);

            foreach (var player in details.Players)
            {
                if (player?.Providers == null || player.Providers.Count == 0)
                    continue;

                string teamName = player.Team?.Name;
                if (string.IsNullOrWhiteSpace(teamName))
                    teamName = "Озвучка";

                string baseName = player.IsSubs ? $"{teamName} (Субтитри)" : teamName;

                foreach (var provider in player.Providers)
                {
                    if (provider?.Episodes == null || provider.Episodes.Count == 0)
                        continue;

                    string displayName = baseName;
                    if (totalProviders > 1 && !string.IsNullOrWhiteSpace(provider.Name))
                        displayName = $"[{provider.Name}] {displayName}";

                    displayName = EnsureUniqueName(voices, displayName);

                    var voice = new MikaiVoiceInfo
                    {
                        DisplayName = displayName,
                        ProviderName = provider.Name,
                        IsSubs = player.IsSubs
                    };

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

                    voice.Seasons[1] = episodes;
                    voices[displayName] = voice;
                }
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

        private string BuildStreamUrl(OnlinesSettings init, string streamLink, List<HeadersModel> headers, bool forceProxy)
        {
            string link = accsArgs(streamLink);
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
