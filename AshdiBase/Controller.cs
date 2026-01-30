using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using AshdiBase.Models;

namespace AshdiBase.Controllers
{
    public class Controller : BaseOnlineController
    {
        private readonly ProxyManager proxyManager;

        public Controller()
        {
            proxyManager = new ProxyManager(ModInit.AshdiBase);
        }

        [HttpGet]
        [Route("ashdi-base")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, int e = -1, bool play = false, bool rjson = false)
        {
            var init = await loadKit(ModInit.AshdiBase);
            if (await IsBadInitialization(init))
                return Forbid();

            await StatsService.StatsAsync(host);
            if (TouchService.Touch(host))
                return OnError(ErrorCodes.Touch, proxyManager);

            var invoke = new AshdiBaseInvoke(init, hybridCache, OnLog, proxyManager);

            var iframeInfo = await invoke.GetIframeInfo(imdb_id, kinopoisk_id);
            if (iframeInfo == null || string.IsNullOrEmpty(iframeInfo.Url))
                return OnError("ashdi-base", proxyManager);

            if (play)
            {
                var streams = await invoke.ParseAshdiSources(iframeInfo.Url);
                if (streams != null && streams.Count > 0)
                    return Redirect(BuildStreamUrl(init, streams.First().link));

                return Content("AshdiBase", "text/html; charset=utf-8");
            }

            if (iframeInfo.IsSerial)
            {
                var voices = await invoke.ParseAshdiSerial(iframeInfo.Url);
                if (voices == null || voices.Count == 0)
                    return OnError("ashdi-base", proxyManager);

                var structure = new SerialStructure { SerialUrl = iframeInfo.Url };
                foreach (var voice in voices)
                {
                    if (!string.IsNullOrEmpty(voice.DisplayName))
                        structure.Voices[voice.DisplayName] = voice;
                }

                var allSeasons = structure.Voices
                    .SelectMany(v => v.Value.Seasons.Keys)
                    .Distinct()
                    .OrderBy(sn => sn)
                    .ToList();

                if (s == -1)
                {
                    var seasonsWithEpisodes = allSeasons
                        .Where(season => structure.Voices.Values.Any(v =>
                            v.Seasons.ContainsKey(season) &&
                            v.Seasons[season].Any(ep => !string.IsNullOrEmpty(ep.File))))
                        .ToList();

                    if (!seasonsWithEpisodes.Any())
                        return OnError("ashdi-base", proxyManager);

                    var seasonTpl = new SeasonTpl(seasonsWithEpisodes.Count);
                    foreach (var season in seasonsWithEpisodes)
                    {
                        string link = $"{host}/ashdi-base?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={season}";
                        seasonTpl.Append($"{season}", link, season.ToString());
                    }

                    return rjson
                        ? Content(seasonTpl.ToJson(), "application/json; charset=utf-8")
                        : Content(seasonTpl.ToHtml(), "text/html; charset=utf-8");
                }

                var voicesForSeason = structure.Voices
                    .Where(v => v.Value.Seasons.ContainsKey(s))
                    .Select(v => new { DisplayName = v.Key, Info = v.Value })
                    .ToList();

                if (!voicesForSeason.Any())
                    return OnError("ashdi-base", proxyManager);

                if (string.IsNullOrEmpty(t))
                    t = voicesForSeason[0].DisplayName;

                var voiceTpl = new VoiceTpl();
                foreach (var voice in voicesForSeason)
                {
                    string voiceLink = $"{host}/ashdi-base?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&t={HttpUtility.UrlEncode(voice.DisplayName)}";
                    voiceTpl.Append(voice.DisplayName, voice.DisplayName == t, voiceLink);
                }

                if (!structure.Voices.ContainsKey(t) || !structure.Voices[t].Seasons.ContainsKey(s))
                    return OnError("ashdi-base", proxyManager);

                var episodes = structure.Voices[t].Seasons[s]
                    .Where(ep => !string.IsNullOrEmpty(ep.File))
                    .ToList();

                var episodeTpl = new EpisodeTpl();
                foreach (var ep in episodes)
                {
                    string link = BuildStreamUrl(init, ep.File);
                    episodeTpl.Append(
                        name: ep.Title,
                        title: title,
                        s: s.ToString(),
                        e: ep.Number.ToString(),
                        link: link
                    );
                }

                if (rjson)
                    return Content(episodeTpl.ToJson(voiceTpl), "application/json; charset=utf-8");

                return Content(voiceTpl.ToHtml() + episodeTpl.ToHtml(), "text/html; charset=utf-8");
            }

            string playLink = $"{host}/ashdi-base?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=0&play=true";
            var movieTpl = new MovieTpl(title, original_title, 1);
            movieTpl.Append(title, accsArgs(playLink), method: "play");
            return rjson
                ? Content(movieTpl.ToJson(), "application/json; charset=utf-8")
                : Content(movieTpl.ToHtml(), "text/html; charset=utf-8");
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
        {
            string link = accsArgs(streamLink);
            if (ApnHelper.IsEnabled(init))
            {
                if (ModInit.ApnHostProvided || ApnHelper.IsAshdiUrl(link))
                    return ApnHelper.WrapUrl(init, link);

                var noApn = (OnlinesSettings)init.Clone();
                noApn.apnstream = false;
                noApn.apn = null;
                return HostStreamProxy(noApn, link);
            }

            return HostStreamProxy(init, link);
        }
    }
}
