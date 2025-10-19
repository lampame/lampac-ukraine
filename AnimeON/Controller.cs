using System.Text.Json;
using Shared.Engine;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Web;
using System.Linq;
using Shared;
using Shared.Models.Templates;
using AnimeON.Models;
using System.Text.RegularExpressions;
using Shared.Models.Online.Settings;
using Shared.Models;
using HtmlAgilityPack;

namespace AnimeON.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller()
        {
            proxyManager = new ProxyManager(ModInit.AnimeON);
        }
        
        [HttpGet]
        [Route("animeon")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false)
        {
            var init = await loadKit(ModInit.AnimeON);
            if (!init.enable)
                return Forbid();

            var invoke = new AnimeONInvoke(init, hybridCache, OnLog, proxyManager);
            OnLog($"AnimeON Index: title={title}, original_title={original_title}, serial={serial}, s={s}, t={t}, year={year}, imdb_id={imdb_id}, kp={kinopoisk_id}");

            var seasons = await invoke.Search(imdb_id, kinopoisk_id, title, original_title, year);
            OnLog($"AnimeON: search results = {seasons?.Count ?? 0}");
            if (seasons == null || seasons.Count == 0)
                return OnError("animeon", proxyManager);

            // [Refactoring] Використовується агрегована структура (AggregateSerialStructure) — попередній збір allOptions не потрібний

            // [Refactoring] Перевірка allOptions видалена — використовується перевірка структури озвучок нижче

            if (serial == 1)
            {
                if (s == -1) // Крок 1: Вибір аніме (як сезони)
                {
                    var season_tpl = new SeasonTpl(seasons.Count);
                    for (int i = 0; i < seasons.Count; i++)
                    {
                        var anime = seasons[i];
                        string seasonName = anime.Season.ToString();
                        string link = $"{host}/animeon?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={i}";
                        season_tpl.Append(seasonName, link, anime.Season.ToString());
                    }
                    OnLog($"AnimeON: return seasons count={seasons.Count}");
                    return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
                }
                else // Крок 2/3: Вибір озвучки та епізодів
                {
                    if (s >= seasons.Count)
                        return OnError("animeon", proxyManager);

                    var selectedAnime = seasons[s];
                    var structure = await invoke.AggregateSerialStructure(selectedAnime.Id, selectedAnime.Season);
                    if (structure == null || !structure.Voices.Any())
                        return OnError("animeon", proxyManager);

                    OnLog($"AnimeON: voices found = {structure.Voices.Count}");
                    // Автовибір першої озвучки якщо t не задано
                    if (string.IsNullOrEmpty(t))
                        t = structure.Voices.Keys.First();

                    // Формуємо список озвучок
                    var voice_tpl = new VoiceTpl();
                    foreach (var voice in structure.Voices)
                    {
                        string voiceLink = $"{host}/animeon?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&t={HttpUtility.UrlEncode(voice.Key)}";
                        bool isActive = voice.Key == t;
                        voice_tpl.Append(voice.Key, isActive, voiceLink);
                    }

                    // Перевірка вибраної озвучки
                    if (!structure.Voices.ContainsKey(t))
                        return OnError("animeon", proxyManager);

                    var episode_tpl = new EpisodeTpl();
                    var selectedVoiceInfo = structure.Voices[t];

                    // Формуємо епізоди для вибраної озвучки
                    foreach (var ep in selectedVoiceInfo.Episodes.OrderBy(e => e.Number))
                    {
                        string episodeName = !string.IsNullOrEmpty(ep.Title) ? ep.Title : $"Епізод {ep.Number}";
                        string seasonStr = selectedAnime.Season.ToString();
                        string episodeStr = ep.Number.ToString();

                        string streamLink = !string.IsNullOrEmpty(ep.Hls) ? ep.Hls : ep.VideoUrl;
                        if (string.IsNullOrEmpty(streamLink))
                            continue;

                        if (selectedVoiceInfo.PlayerType == "moon")
                        {
                            // method=call з accsArgs(callUrl)
                            string callUrl = $"{host}/animeon/play?url={HttpUtility.UrlEncode(streamLink)}";
                            episode_tpl.Append(episodeName, title ?? original_title, seasonStr, episodeStr, accsArgs(callUrl), "call");
                        }
                        else
                        {
                            // Пряме відтворення через HostStreamProxy(init, accsArgs(streamLink))
                            string playUrl = HostStreamProxy(init, accsArgs(streamLink));
                            episode_tpl.Append(episodeName, title ?? original_title, seasonStr, episodeStr, playUrl);
                        }
                    }

                    // Повертаємо озвучки + епізоди разом
                    OnLog($"AnimeON: return episodes count={selectedVoiceInfo.Episodes.Count} for voice='{t}' season={selectedAnime.Season}");
                    if (rjson)
                        return Content(episode_tpl.ToJson(voice_tpl), "application/json; charset=utf-8");

                    return Content(voice_tpl.ToHtml() + episode_tpl.ToHtml(), "text/html; charset=utf-8");
                }
            }
            else // Фільм
            {
                var firstAnime = seasons.FirstOrDefault();
                if (firstAnime == null)
                    return OnError("animeon", proxyManager);

                var fundubs = await invoke.GetFundubs(firstAnime.Id);
                OnLog($"AnimeON: movie fundubs count = {fundubs?.Count ?? 0}");
                if (fundubs == null || fundubs.Count == 0)
                    return OnError("animeon", proxyManager);

                var tpl = new MovieTpl(title, original_title);

                foreach (var fundub in fundubs)
                {
                    if (fundub?.Fundub == null || fundub.Player == null || fundub.Player.Count == 0)
                        continue;

                    foreach (var player in fundub.Player)
                    {
                        var episodesData = await invoke.GetEpisodes(firstAnime.Id, player.Id, fundub.Fundub.Id);
                        if (episodesData == null || episodesData.Episodes == null || episodesData.Episodes.Count == 0)
                            continue;

                        var firstEp = episodesData.Episodes.FirstOrDefault();
                        if (firstEp == null)
                            continue;

                        string streamLink = !string.IsNullOrEmpty(firstEp.Hls) ? firstEp.Hls : firstEp.VideoUrl;
                        if (string.IsNullOrEmpty(streamLink))
                            continue;

                        string translationName = $"[{player.Name}] {fundub.Fundub.Name}";

                        if (player.Name?.ToLower() == "moon" && streamLink.Contains("moonanime.art/iframe/"))
                        {
                            string callUrl = $"{host}/animeon/play?url={HttpUtility.UrlEncode(streamLink)}";
                            tpl.Append(translationName, accsArgs(callUrl), "call");
                        }
                        else
                        {
                            tpl.Append(translationName, HostStreamProxy(init, accsArgs(streamLink)));
                        }
                    }
                }

                // Якщо не зібрали жодної опції — повертаємо помилку
                if (tpl.IsEmpty())
                    return OnError("animeon", proxyManager);

                OnLog("AnimeON: return movie options");
                return rjson ? Content(tpl.ToJson(), "application/json; charset=utf-8") : Content(tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        async Task<List<FundubModel>> GetFundubs(OnlinesSettings init, int animeId)
        {
            string fundubsUrl = $"{init.host}/api/player/fundubs/{animeId}";
            string fundubsJson = await Http.Get(fundubsUrl, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", init.host) });
            if (string.IsNullOrEmpty(fundubsJson))
                return null;

            var fundubsResponse = JsonSerializer.Deserialize<FundubsResponseModel>(fundubsJson);
            return fundubsResponse?.FunDubs;
        }

        async Task<EpisodeModel> GetEpisodes(OnlinesSettings init, int animeId, int playerId, int fundubId)
        {
            string episodesUrl = $"{init.host}/api/player/episodes/{animeId}?take=100&skip=-1&playerId={playerId}&fundubId={fundubId}";
            string episodesJson = await Http.Get(episodesUrl, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", init.host) });
            if (string.IsNullOrEmpty(episodesJson))
                return null;

            return JsonSerializer.Deserialize<EpisodeModel>(episodesJson);
        }

        async ValueTask<List<SearchModel>> search(OnlinesSettings init, string imdb_id, long kinopoisk_id, string title, string original_title, int year)
        {
            string memKey = $"AnimeON:search:{kinopoisk_id}:{imdb_id}";
            if (hybridCache.TryGetValue(memKey, out List<SearchModel> res))
                return res;

            try
            {
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", init.host) };
                
                async Task<List<SearchModel>> FindAnime(string query)
                {
                    if (string.IsNullOrEmpty(query))
                        return null;

                    string searchUrl = $"{init.host}/api/anime/search?text={HttpUtility.UrlEncode(query)}";
                    string searchJson = await Http.Get(searchUrl, headers: headers);
                    if (string.IsNullOrEmpty(searchJson))
                        return null;

                    var searchResponse = JsonSerializer.Deserialize<SearchResponseModel>(searchJson);
                    return searchResponse?.Result;
                }

                var searchResults = await FindAnime(title) ?? await FindAnime(original_title);
                if (searchResults == null)
                    return null;
                
                if (!string.IsNullOrEmpty(imdb_id))
                {
                    var seasons = searchResults.Where(a => a.ImdbId == imdb_id).ToList();
                    if (seasons.Count > 0)
                    {
                        hybridCache.Set(memKey, seasons, cacheTime(5));
                        return seasons;
                    }
                }
                
                // Fallback to first result if no imdb match
                var firstResult = searchResults.FirstOrDefault();
                if (firstResult != null)
                {
                    var list = new List<SearchModel> { firstResult };
                    hybridCache.Set(memKey, list, cacheTime(5));
                    return list;
                }

                return null;
            }
            catch (Exception ex)
            {
                OnLog($"AnimeON error: {ex.Message}");
            }
            
            return null;
        }

        [HttpGet("animeon/play")]
        public async Task<ActionResult> Play(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                OnLog("AnimeON Play: empty url");
                return OnError("animeon", proxyManager);
            }

            var init = await loadKit(ModInit.AnimeON);
            if (!init.enable)
                return Forbid();

            var invoke = new AnimeONInvoke(init, hybridCache, OnLog, proxyManager);
            OnLog($"AnimeON Play: url={url}");
            string streamLink = await invoke.ParseMoonAnimePage(url);

            if (string.IsNullOrEmpty(streamLink))
            {
                OnLog("AnimeON Play: cannot extract stream from iframe");
                return OnError("animeon", proxyManager);
            }

            OnLog("AnimeON Play: redirect to proxied stream");
            return Redirect(HostStreamProxy(init, accsArgs(streamLink)));
        }
    }
}
