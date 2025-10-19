using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models;
using System.Text.Json;
using System.Linq;
using AnimeON.Models;
using Shared.Engine;

namespace AnimeON
{
    public class AnimeONInvoke
    {
        private OnlinesSettings _init;
        private HybridCache _hybridCache;
        private Action<string> _onLog;
        private ProxyManager _proxyManager;

        public AnimeONInvoke(OnlinesSettings init, HybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<List<SearchModel>> Search(string imdb_id, long kinopoisk_id, string title, string original_title, int year)
        {
            string memKey = $"AnimeON:search:{kinopoisk_id}:{imdb_id}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchModel> res))
                return res;

            try
            {
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) };
                
                async Task<List<SearchModel>> FindAnime(string query)
                {
                    if (string.IsNullOrEmpty(query))
                        return null;

                    string searchUrl = $"{_init.host}/api/anime/search?text={System.Web.HttpUtility.UrlEncode(query)}";
                    _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {searchUrl}");
                    string searchJson = await Http.Get(searchUrl, headers: headers, proxy: _proxyManager.Get());
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
                        _hybridCache.Set(memKey, seasons, cacheTime(5));
                        return seasons;
                    }
                }
                
                // Fallback to first result if no imdb match
                var firstResult = searchResults.FirstOrDefault();
                if (firstResult != null)
                {
                    var list = new List<SearchModel> { firstResult };
                    _hybridCache.Set(memKey, list, cacheTime(5));
                    return list;
                }

                return null;
            }
            catch (Exception ex)
            {
                _onLog($"AnimeON error: {ex.Message}");
            }
            
            return null;
        }

        public async Task<List<FundubModel>> GetFundubs(int animeId)
        {
            string fundubsUrl = $"{_init.host}/api/player/fundubs/{animeId}";
            _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {fundubsUrl}");
            string fundubsJson = await Http.Get(fundubsUrl, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) }, proxy: _proxyManager.Get());
            if (string.IsNullOrEmpty(fundubsJson))
                return null;

            var fundubsResponse = JsonSerializer.Deserialize<FundubsResponseModel>(fundubsJson);
            return fundubsResponse?.FunDubs;
        }

        public async Task<EpisodeModel> GetEpisodes(int animeId, int playerId, int fundubId)
        {
            string episodesUrl = $"{_init.host}/api/player/episodes/{animeId}?take=100&skip=-1&playerId={playerId}&fundubId={fundubId}";
            _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {episodesUrl}");
            string episodesJson = await Http.Get(episodesUrl, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) }, proxy: _proxyManager.Get());
            if (string.IsNullOrEmpty(episodesJson))
                return null;

            return JsonSerializer.Deserialize<EpisodeModel>(episodesJson);
        }

        public async Task<string> ParseMoonAnimePage(string url)
        {
            try
            {
                string requestUrl = $"{url}?player=animeon.club";
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", "https://animeon.club/")
                };

                _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
                string html = await Http.Get(requestUrl, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return null;

                var match = System.Text.RegularExpressions.Regex.Match(html, @"file:\s*""([^""]+\.m3u8)""");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                _onLog($"AnimeON ParseMoonAnimePage error: {ex.Message}");
            }

            return null;
        }

        public static TimeSpan cacheTime(int multiaccess, int home = 5, int mikrotik = 2, OnlinesSettings init = null, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);

            int ctime = AppInit.conf.mikrotik ? mikrotik : AppInit.conf.multiaccess ? init != null && init.cache_time > 0 ? init.cache_time : multiaccess : home;
            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }
        public async Task<AnimeON.Models.AnimeONAggregatedStructure> AggregateSerialStructure(int animeId, int season)
        {
            string memKey = $"AnimeON:aggregated:{animeId}:{season}";
            if (_hybridCache.TryGetValue(memKey, out AnimeON.Models.AnimeONAggregatedStructure cached))
                return cached;

            try
            {
                var structure = new AnimeON.Models.AnimeONAggregatedStructure
                {
                    AnimeId = animeId,
                    Season = season,
                    Voices = new Dictionary<string, AnimeON.Models.AnimeONVoiceInfo>()
                };

                var fundubs = await GetFundubs(animeId);
                if (fundubs == null || fundubs.Count == 0)
                    return null;

                foreach (var fundub in fundubs)
                {
                    if (fundub?.Fundub == null || fundub.Player == null)
                        continue;

                    foreach (var player in fundub.Player)
                    {
                        string display = $"[{player.Name}] {fundub.Fundub.Name}";

                        var episodesData = await GetEpisodes(animeId, player.Id, fundub.Fundub.Id);
                        if (episodesData?.Episodes == null || episodesData.Episodes.Count == 0)
                            continue;

                        var voiceInfo = new AnimeON.Models.AnimeONVoiceInfo
                        {
                            Name = fundub.Fundub.Name,
                            PlayerType = player.Name?.ToLower(),
                            DisplayName = display,
                            PlayerId = player.Id,
                            FundubId = fundub.Fundub.Id,
                            Episodes = episodesData.Episodes
                                .OrderBy(ep => ep.EpisodeNum)
                                .Select(ep => new AnimeON.Models.AnimeONEpisodeInfo
                                {
                                    Number = ep.EpisodeNum,
                                    Title = ep.Name,
                                    Hls = ep.Hls,
                                    VideoUrl = ep.VideoUrl,
                                    EpisodeId = ep.Id
                                })
                                .ToList()
                        };

                        structure.Voices[display] = voiceInfo;
                    }
                }

                if (!structure.Voices.Any())
                    return null;

                _hybridCache.Set(memKey, structure, cacheTime(20, init: _init));
                return structure;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AnimeON AggregateSerialStructure error: {ex.Message}");
                return null;
            }
        }
    }
}