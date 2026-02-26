using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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
        private static readonly Regex Quality4kRegex = new Regex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase);
        private static readonly Regex QualityFhdRegex = new Regex(@"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b", RegexOptions.IgnoreCase);

        private OnlinesSettings _init;
        private IHybridCache _hybridCache;
        private Action<string> _onLog;
        private ProxyManager _proxyManager;

        public UaTUTInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
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
            var response = await Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());

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
                var response = await Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());

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
                string sourceUrl = WithAshdiMultivoice(playerUrl);
                string requestUrl = sourceUrl;
                if (ApnHelper.IsAshdiUrl(sourceUrl) && ApnHelper.IsEnabled(_init) && string.IsNullOrWhiteSpace(_init.webcorshost))
                    requestUrl = ApnHelper.WrapUrl(_init, sourceUrl);

                _onLog($"UaTUT getting player data from: {requestUrl}");

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"),
                    new HeadersModel("Referer", sourceUrl.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase) ? "https://ashdi.vip/" : _init.apihost)
                };
                var response = await Http.Get(_init.cors(requestUrl), headers: headers, proxy: _proxyManager.Get());

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
                    playerData.Movies = new List<MovieVariant>()
                    {
                        new MovieVariant
                        {
                            File = playerData.File,
                            Quality = DetectQualityTag(playerData.File) ?? "auto",
                            Title = BuildMovieTitle("Основне джерело", playerData.File, 1)
                        }
                    };
                    _onLog($"UaTUT found direct file: {playerData.File}");

                    // Шукаємо poster
                    var posterMatch = Regex.Match(playerHtml, @"poster:[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    if (posterMatch.Success)
                        playerData.Poster = posterMatch.Groups[1].Value;

                    return playerData;
                }

                // Для серіалів шукаємо JSON структуру з сезонами та озвучками
                string jsonData = ExtractPlayerFileArray(playerHtml);
                if (!string.IsNullOrWhiteSpace(jsonData))
                {
                    string normalizedJson = WebUtility.HtmlDecode(jsonData)
                        .Replace("\\/", "/")
                        .Replace("\\'", "'")
                        .Replace("\\\"", "\"");

                    _onLog($"UaTUT found JSON data for series");

                    playerData.Movies = ParseMovieVariantsJson(normalizedJson);
                    playerData.File = playerData.Movies?.FirstOrDefault()?.File;
                    playerData.Voices = ParseVoicesJson(normalizedJson);
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

        private List<MovieVariant> ParseMovieVariantsJson(string jsonData)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<List<dynamic>>(jsonData);
                var movies = new List<MovieVariant>();
                if (data == null || data.Count == 0)
                    return movies;

                int index = 1;
                foreach (var item in data)
                {
                    string file = item?.file?.ToString();
                    if (string.IsNullOrWhiteSpace(file))
                        continue;

                    string rawTitle = item?.title?.ToString();
                    movies.Add(new MovieVariant
                    {
                        File = file,
                        Quality = DetectQualityTag($"{rawTitle} {file}") ?? "auto",
                        Title = BuildMovieTitle(rawTitle, file, index)
                    });
                    index++;
                }

                return movies;
            }
            catch (Exception ex)
            {
                _onLog($"UaTUT ParseMovieVariantsJson error: {ex.Message}");
                return new List<MovieVariant>();
            }
        }

        private static string WithAshdiMultivoice(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            if (url.IndexOf("ashdi.vip/vod/", StringComparison.OrdinalIgnoreCase) < 0)
                return url;

            if (url.IndexOf("multivoice", StringComparison.OrdinalIgnoreCase) >= 0)
                return url;

            return url.Contains("?") ? $"{url}&multivoice" : $"{url}?multivoice";
        }

        private static string BuildMovieTitle(string rawTitle, string file, int index)
        {
            string title = string.IsNullOrWhiteSpace(rawTitle) ? $"Варіант {index}" : StripMoviePrefix(WebUtility.HtmlDecode(rawTitle).Trim());
            string qualityTag = DetectQualityTag($"{title} {file}");
            if (string.IsNullOrWhiteSpace(qualityTag))
                return title;

            if (title.StartsWith("[4K]", StringComparison.OrdinalIgnoreCase) || title.StartsWith("[FHD]", StringComparison.OrdinalIgnoreCase))
                return title;

            return $"{qualityTag} {title}";
        }

        private static string DetectQualityTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (Quality4kRegex.IsMatch(value))
                return "[4K]";

            if (QualityFhdRegex.IsMatch(value))
                return "[FHD]";

            return null;
        }

        private static string StripMoviePrefix(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title;

            string normalized = Regex.Replace(title, @"\s+", " ").Trim();
            int sepIndex = normalized.LastIndexOf(" - ", StringComparison.Ordinal);
            if (sepIndex <= 0 || sepIndex >= normalized.Length - 3)
                return normalized;

            string prefix = normalized.Substring(0, sepIndex).Trim();
            string suffix = normalized.Substring(sepIndex + 3).Trim();
            if (string.IsNullOrWhiteSpace(suffix))
                return normalized;

            if (Regex.IsMatch(prefix, @"(19|20)\d{2}"))
                return suffix;

            return normalized;
        }

        private static string ExtractPlayerFileArray(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            int searchIndex = 0;
            while (searchIndex >= 0 && searchIndex < html.Length)
            {
                int fileIndex = html.IndexOf("file", searchIndex, StringComparison.OrdinalIgnoreCase);
                if (fileIndex < 0)
                    return null;

                int colonIndex = html.IndexOf(':', fileIndex);
                if (colonIndex < 0)
                    return null;

                int startIndex = colonIndex + 1;
                while (startIndex < html.Length && char.IsWhiteSpace(html[startIndex]))
                    startIndex++;

                if (startIndex < html.Length && (html[startIndex] == '\'' || html[startIndex] == '"'))
                {
                    startIndex++;
                    while (startIndex < html.Length && char.IsWhiteSpace(html[startIndex]))
                        startIndex++;
                }

                if (startIndex >= html.Length || html[startIndex] != '[')
                {
                    searchIndex = fileIndex + 4;
                    continue;
                }

                return ExtractBracketArray(html, startIndex);
            }

            return null;
        }

        private static string ExtractBracketArray(string text, int startIndex)
        {
            if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '[')
                return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            char quoteChar = '\0';

            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (ch == quoteChar)
                    {
                        inString = false;
                        quoteChar = '\0';
                    }

                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    inString = true;
                    quoteChar = ch;
                    continue;
                }

                if (ch == '[')
                {
                    depth++;
                    continue;
                }

                if (ch == ']')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(startIndex, i - startIndex + 1);
                }
            }

            return null;
        }
    }
}
