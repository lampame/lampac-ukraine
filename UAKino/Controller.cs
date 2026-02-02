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
using UAKino.Models;

namespace UAKino.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.UAKino);
        }

        [HttpGet]
        [Route("uakino")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, bool rjson = false, string href = null)
        {
            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.UAKino);
            if (!init.enable)
                return Forbid();

            var invoke = new UAKinoInvoke(init, hybridCache, OnLog, proxyManager);

            string itemUrl = href;
            if (string.IsNullOrEmpty(itemUrl))
            {
                var searchResults = await invoke.Search(title, original_title, serial);
                if (searchResults == null || searchResults.Count == 0)
                    return OnError("uakino", proxyManager);

                if (searchResults.Count > 1)
                {
                    var similar_tpl = new SimilarTpl(searchResults.Count);
                    foreach (var res in searchResults)
                    {
                        string link = $"{host}/uakino?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(res.Url)}";
                        similar_tpl.Append(res.Title, string.Empty, string.Empty, link, res.Poster);
                    }

                    return rjson ? Content(similar_tpl.ToJson(), "application/json; charset=utf-8") : Content(similar_tpl.ToHtml(), "text/html; charset=utf-8");
                }

                itemUrl = searchResults[0].Url;
            }

            if (serial == 1)
            {
                var playlist = await invoke.GetPlaylist(itemUrl);
                if (playlist == null || playlist.Count == 0)
                    return OnError("uakino", proxyManager);

                var voiceGroups = playlist
                    .GroupBy(p => string.IsNullOrEmpty(p.Voice) ? "Основне" : p.Voice)
                    .Select(g => (key: g.Key, episodes: g.ToList()))
                    .ToList();

                if (voiceGroups.Count == 0)
                    return OnError("uakino", proxyManager);

                if (string.IsNullOrEmpty(t))
                    t = voiceGroups.First().key;

                var voice_tpl = new VoiceTpl();
                foreach (var voice in voiceGroups)
                {
                    string voiceLink = $"{host}/uakino?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&t={HttpUtility.UrlEncode(voice.key)}&href={HttpUtility.UrlEncode(itemUrl)}";
                    voice_tpl.Append(voice.key, voice.key == t, voiceLink);
                }

                var selected = voiceGroups.FirstOrDefault(v => v.key == t);
                if (selected.episodes == null || selected.episodes.Count == 0)
                    return OnError("uakino", proxyManager);

                var episode_tpl = new EpisodeTpl();
                int index = 1;
                foreach (var ep in selected.episodes.OrderBy(e => UAKinoInvoke.TryParseEpisodeNumber(e.Title) ?? int.MaxValue))
                {
                    int episodeNumber = UAKinoInvoke.TryParseEpisodeNumber(ep.Title) ?? index;
                    string episodeName = string.IsNullOrEmpty(ep.Title) ? $"Епізод {episodeNumber}" : ep.Title;
                    string callUrl = $"{host}/uakino/play?url={HttpUtility.UrlEncode(ep.Url)}&title={HttpUtility.UrlEncode(title ?? original_title)}";
                    episode_tpl.Append(episodeName, title ?? original_title, "1", episodeNumber.ToString("D2"), accsArgs(callUrl), "call");
                    index++;
                }

                episode_tpl.Append(voice_tpl);
                if (rjson)
                    return Content(episode_tpl.ToJson(), "application/json; charset=utf-8");

                return Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
            }
            else
            {
                string playerUrl = await invoke.GetPlayerUrl(itemUrl);
                if (string.IsNullOrEmpty(playerUrl))
                {
                    var playlist = await invoke.GetPlaylist(itemUrl);
                    playerUrl = playlist?.FirstOrDefault()?.Url;
                }

                if (string.IsNullOrEmpty(playerUrl))
                    return OnError("uakino", proxyManager);

                var movie_tpl = new MovieTpl(title, original_title);
                string callUrl = $"{host}/uakino/play?url={HttpUtility.UrlEncode(playerUrl)}&title={HttpUtility.UrlEncode(title ?? original_title)}";
                movie_tpl.Append(string.IsNullOrEmpty(title) ? "UAKino" : title, accsArgs(callUrl), "call");

                return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        [HttpGet]
        [Route("uakino/play")]
        async public Task<ActionResult> Play(string url, string title)
        {
            await UpdateService.ConnectAsync(host);

            if (string.IsNullOrEmpty(url))
                return OnError("uakino", proxyManager);

            var init = await loadKit(ModInit.UAKino);
            if (!init.enable)
                return Forbid();

            var invoke = new UAKinoInvoke(init, hybridCache, OnLog, proxyManager);
            var result = await invoke.ParsePlayer(url);
            if (result == null || string.IsNullOrEmpty(result.File))
                return OnError("uakino", proxyManager);

            string streamUrl = BuildStreamUrl(init, result.File);
            string jsonResult = $"{{\"method\":\"play\",\"url\":\"{streamUrl}\",\"title\":\"{title ?? ""}\"}}";
            return UpdateService.Validate(Content(jsonResult, "application/json; charset=utf-8"));
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
                return HostStreamProxy(noApn, link, proxy: proxyManager.Get());
            }

            return HostStreamProxy(init, link, proxy: proxyManager.Get());
        }
    }
}
