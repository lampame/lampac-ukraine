using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models;
using UaTUT.Models;

namespace UaTUT
{
    public class UaTUTInvoke
    {
        private OnlinesSettings _init;
        private HybridCache _hybridCache;
        private Action<string> _onLog;
        private ProxyManager _proxyManager;

        public UaTUTInvoke(OnlinesSettings init, HybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<List<SearchResult>> Search(string query, string imdbId = null)
        {
            try
            {
                string searchUrl = $"{_init.apihost}/search.php";

                // Поступовий пошук: спочатку по imdbId, потім по назві
                if (!string.IsNullOrEmpty(imdbId))
                {
                    var imdbResults = await PerformSearch(searchUrl, imdbId);
                    if (imdbResults?.Any() == true)
                        return imdbResults;
                }

                // Пошук по назві
                if (!string.IsNullOrEmpty(query))
                {
                    var titleResults = await PerformSearch(searchUrl, query);
                    return titleResults ?? new List<SearchResult>();
                }

                return new List<SearchResult>();
            }
            catch (Exception ex)
            {
                _onLog($"UaTUT Search error: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private async Task<List<SearchResult>> PerformSearch(string searchUrl, string query)
        {
            string url = $"{searchUrl}?q={HttpUtility.UrlEncode(query)}";
            _onLog($"UaTUT searching: {url}");

            var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36") };
            var response = await Http.Get(url, headers: headers, proxy: _proxyManager.Get());

            if (string.IsNullOrEmpty(response))
                return null;

            try
            {
                var results = JsonConvert.DeserializeObject<List<SearchResult>>(response);
                _onLog($"UaTUT found {results?.Count ?? 0} results for query: {query}");
                return results;
            }
            catch (Exception ex)
            {
                _onLog($"UaTUT parse error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetMoviePageContent(string movieId)
        {
            try
            {
                string url = $"{_init.apihost}/{movieId}";
                _onLog($"UaTUT getting movie page: {url}");

                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36") };
                var response = await Http.Get(url, headers: headers, proxy: _proxyManager.Get());

                return response;
            }
            catch (Exception ex)
            {
                _onLog($"UaTUT GetMoviePageContent error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetPlayerUrl(string moviePageContent)
        {
            try
            {
                // Шукаємо iframe з id="vip-player" та class="tab-content"
                var match = Regex.Match(moviePageContent, @"<iframe[^>]*id=[""']vip-player[""'][^>]*src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string playerUrl = match.Groups[1].Value;
                    _onLog($"UaTUT found player URL: {playerUrl}");
                    return playerUrl;
                }

                _onLog("UaTUT: vip-player iframe not found");
                return null;
            }
            catch (Exception ex)
            {
                _onLog($"UaTUT GetPlayerUrl error: {ex.Message}");
                return null;
            }
        }

        public async Task<PlayerData> GetPlayerData(string playerUrl)
        {
            try
            {
                _onLog($"UaTUT getting player data from: {playerUrl}");

                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36") };
                var response = await Http.Get(playerUrl, headers: headers, proxy: _proxyManager.Get());

                if (string.IsNullOrEmpty(response))
                    return null;

                return ParsePlayerData(response);
            }
            catch (Exception ex)
            {
                _onLog($"UaTUT GetPlayerData error: {ex.Message}");
                return null;
            }
        }

        private PlayerData ParsePlayerData(string playerHtml)
        {
            try
            {
                var playerData = new PlayerData();

                // Для фільмів шукаємо прямий file
                var fileMatch = Regex.Match(playerHtml, @"file:'([^']+)'", RegexOptions.IgnoreCase);
                if (fileMatch.Success && !fileMatch.Groups[1].Value.StartsWith("["))
                {
                    playerData.File = fileMatch.Groups[1].Value;
                    _onLog($"UaTUT found direct file: {playerData.File}");

                    // Шукаємо poster
                    var posterMatch = Regex.Match(playerHtml, @"poster:[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    if (posterMatch.Success)
                        playerData.Poster = posterMatch.Groups[1].Value;

                    return playerData;
                }

                // Для серіалів шукаємо JSON структуру з сезонами та озвучками
                var jsonMatch = Regex.Match(playerHtml, @"file:'(\[.*?\])'", RegexOptions.Singleline);
                if (jsonMatch.Success)
                {
                    string jsonData = jsonMatch.Groups[1].Value;
                    _onLog($"UaTUT found JSON data for series");

                    playerData.Voices = ParseVoicesJson(jsonData);
                    return playerData;
                }

                _onLog("UaTUT: No player data found");
                return null;
            }
            catch (Exception ex)
            {
                _onLog($"UaTUT ParsePlayerData error: {ex.Message}");
                return null;
            }
        }

        private List<Voice> ParseVoicesJson(string jsonData)
        {
            try
            {
                // Декодуємо JSON структуру озвучок
                dynamic voicesData = JsonConvert.DeserializeObject(jsonData);
                var voices = new List<Voice>();

                if (voicesData != null)
                {
                    foreach (var voiceGroup in voicesData)
                    {
                        var voice = new Voice
                        {
                            Name = voiceGroup.title?.ToString(),
                            Seasons = new List<Season>()
                        };

                        if (voiceGroup.folder != null)
                        {
                            foreach (var seasonData in voiceGroup.folder)
                            {
                                var season = new Season
                                {
                                    Title = seasonData.title?.ToString(),
                                    Episodes = new List<Episode>()
                                };

                                if (seasonData.folder != null)
                                {
                                    foreach (var episodeData in seasonData.folder)
                                    {
                                        var episode = new Episode
                                        {
                                            Title = episodeData.title?.ToString(),
                                            File = episodeData.file?.ToString(),
                                            Id = episodeData.id?.ToString(),
                                            Poster = episodeData.poster?.ToString(),
                                            Subtitle = episodeData.subtitle?.ToString()
                                        };
                                        season.Episodes.Add(episode);
                                    }
                                }

                                voice.Seasons.Add(season);
                            }
                        }

                        voices.Add(voice);
                    }
                }

                _onLog($"UaTUT parsed {voices.Count} voices");
                return voices;
            }
            catch (Exception ex)
            {
                _onLog($"UaTUT ParseVoicesJson error: {ex.Message}");
                return new List<Voice>();
            }
        }
    }
}
