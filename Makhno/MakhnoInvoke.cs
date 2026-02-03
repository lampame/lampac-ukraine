using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Makhno.Models;

namespace Makhno
{
    public class MakhnoInvoke
    {
        private const string WormholeHost = "http://wormhole.lampame.v6.rocks/";
        private const string AshdiHost = "https://ashdi.vip";

        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public MakhnoInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<string> GetWormholePlay(string imdbId)
        {
            if (string.IsNullOrWhiteSpace(imdbId))
                return null;

            string url = $"{WormholeHost}?imdb_id={imdbId}";
            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent)
                };

                string response = await Http.Get(url, timeoutSeconds: 4, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrWhiteSpace(response))
                    return null;

                var payload = JsonConvert.DeserializeObject<WormholeResponse>(response);
                return string.IsNullOrWhiteSpace(payload?.play) ? null : payload.play;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno wormhole error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<SearchResult>> SearchUaTUT(string query, string imdbId = null)
        {
            try
            {
                string searchUrl = $"{_init.apihost}/search.php";

                if (!string.IsNullOrEmpty(imdbId))
                {
                    var imdbResults = await PerformSearch(searchUrl, imdbId);
                    if (imdbResults?.Any() == true)
                        return imdbResults;
                }

                if (!string.IsNullOrEmpty(query))
                {
                    var titleResults = await PerformSearch(searchUrl, query);
                    return titleResults ?? new List<SearchResult>();
                }

                return new List<SearchResult>();
            }
            catch (Exception ex)
            {
                _onLog($"Makhno UaTUT search error: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private async Task<List<SearchResult>> PerformSearch(string searchUrl, string query)
        {
            string url = $"{searchUrl}?q={WebUtility.UrlEncode(query)}";
            _onLog($"Makhno UaTUT searching: {url}");

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", Http.UserAgent)
            };

            var response = await Http.Get(url, headers: headers, proxy: _proxyManager.Get());

            if (string.IsNullOrEmpty(response))
                return null;

            try
            {
                var results = JsonConvert.DeserializeObject<List<SearchResult>>(response);
                _onLog($"Makhno UaTUT found {results?.Count ?? 0} results for query: {query}");
                return results;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno UaTUT parse error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetMoviePageContent(string movieId)
        {
            try
            {
                string url = $"{_init.apihost}/{movieId}";
                _onLog($"Makhno UaTUT getting movie page: {url}");

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent)
                };
                var response = await Http.Get(url, headers: headers, proxy: _proxyManager.Get());

                return response;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno UaTUT GetMoviePageContent error: {ex.Message}");
                return null;
            }
        }

        public string GetPlayerUrl(string moviePageContent)
        {
            try
            {
                if (string.IsNullOrEmpty(moviePageContent))
                    return null;

                var match = Regex.Match(moviePageContent, @"<iframe[^>]*id=[""']vip-player[""'][^>]*src=[""']([^""']+)", RegexOptions.IgnoreCase);
                if (match.Success)
                    return NormalizePlayerUrl(match.Groups[1].Value);

                match = Regex.Match(moviePageContent, @"<iframe[^>]*id=[""']alt-player[""'][^>]*src=[""']([^""']+)", RegexOptions.IgnoreCase);
                if (match.Success)
                    return NormalizePlayerUrl(match.Groups[1].Value);

                var iframeMatches = Regex.Matches(moviePageContent, @"<iframe[^>]*(?:id=[""']([^""']+)[""'])?[^>]*src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                foreach (Match iframe in iframeMatches)
                {
                    string iframeId = iframe.Groups[1].Value?.ToLowerInvariant();
                    string src = iframe.Groups[2].Value;
                    if (string.IsNullOrEmpty(src))
                        continue;

                    if (!string.IsNullOrEmpty(iframeId) && iframeId.Contains("player"))
                        return NormalizePlayerUrl(src);

                    if (src.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase) ||
                        src.Contains("zetvideo.net", StringComparison.OrdinalIgnoreCase) ||
                        src.Contains("player", StringComparison.OrdinalIgnoreCase))
                        return NormalizePlayerUrl(src);
                }

                var urlMatch = Regex.Match(moviePageContent, @"(https?://[^""'\s>]+/(?:vod|serial)/\d+[^""'\s>]*)", RegexOptions.IgnoreCase);
                if (urlMatch.Success)
                    return NormalizePlayerUrl(urlMatch.Groups[1].Value);

                return null;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno UaTUT GetPlayerUrl error: {ex.Message}");
                return null;
            }
        }

        private string NormalizePlayerUrl(string src)
        {
            if (string.IsNullOrEmpty(src))
                return null;

            if (src.StartsWith("//"))
                return $"https:{src}";

            return src;
        }

        public async Task<PlayerData> GetPlayerData(string playerUrl)
        {
            if (string.IsNullOrEmpty(playerUrl))
                return null;

            try
            {
                string requestUrl = playerUrl;
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent)
                };

                if (playerUrl.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase))
                {
                    headers.Add(new HeadersModel("Referer", "https://ashdi.vip/"));
                }

                if (ApnHelper.IsAshdiUrl(playerUrl) && ApnHelper.IsEnabled(_init))
                    requestUrl = ApnHelper.WrapUrl(_init, playerUrl);

                _onLog($"Makhno getting player data from: {requestUrl}");

                var response = await Http.Get(requestUrl, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(response))
                    return null;

                return ParsePlayerData(response);
            }
            catch (Exception ex)
            {
                _onLog($"Makhno GetPlayerData error: {ex.Message}");
                return null;
            }
        }

        private PlayerData ParsePlayerData(string html)
        {
            try
            {
                if (string.IsNullOrEmpty(html))
                    return null;

                var fileMatch = Regex.Match(html, @"file:'([^']+)'", RegexOptions.IgnoreCase);
                if (!fileMatch.Success)
                    fileMatch = Regex.Match(html, @"file:\s*""([^""]+)""", RegexOptions.IgnoreCase);

                if (fileMatch.Success && !fileMatch.Groups[1].Value.StartsWith("["))
                {
                    var posterMatch = Regex.Match(html, @"poster:[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    return new PlayerData
                    {
                        File = fileMatch.Groups[1].Value,
                        Poster = posterMatch.Success ? posterMatch.Groups[1].Value : null,
                        Voices = new List<Voice>()
                    };
                }

                var m3u8Match = Regex.Match(html, @"(https?://[^""'\s>]+\.m3u8[^""'\s>]*)", RegexOptions.IgnoreCase);
                if (m3u8Match.Success)
                {
                    return new PlayerData
                    {
                        File = m3u8Match.Groups[1].Value,
                        Poster = null,
                        Voices = new List<Voice>()
                    };
                }

                var sourceMatch = Regex.Match(html, @"<source[^>]*src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (sourceMatch.Success)
                {
                    return new PlayerData
                    {
                        File = sourceMatch.Groups[1].Value,
                        Poster = null,
                        Voices = new List<Voice>()
                    };
                }

                string jsonData = ExtractPlayerJson(html);
                if (jsonData == null)
                    _onLog("Makhno ParsePlayerData: file array not found");
                else
                    _onLog($"Makhno ParsePlayerData: file array length={jsonData.Length}");
                if (!string.IsNullOrEmpty(jsonData))
                {
                    var voices = ParseVoicesJson(jsonData);
                    _onLog($"Makhno ParsePlayerData: voices={voices?.Count ?? 0}");
                    return new PlayerData
                    {
                        File = null,
                        Poster = null,
                        Voices = voices
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno ParsePlayerData error: {ex.Message}");
                return null;
            }
        }

        private List<Voice> ParseVoicesJson(string jsonData)
        {
            try
            {
                var voicesArray = JsonConvert.DeserializeObject<List<JObject>>(jsonData);
                var voices = new List<Voice>();

                if (voicesArray == null)
                    return voices;

                foreach (var voiceGroup in voicesArray)
                {
                    var voice = new Voice
                    {
                        Name = voiceGroup["title"]?.ToString(),
                        Seasons = new List<Season>()
                    };

                    var seasons = voiceGroup["folder"] as JArray;
                    if (seasons != null)
                    {
                        foreach (var seasonGroup in seasons)
                        {
                            string seasonTitle = seasonGroup["title"]?.ToString() ?? string.Empty;
                            var episodes = new List<Episode>();

                            var episodesArray = seasonGroup["folder"] as JArray;
                            if (episodesArray != null)
                            {
                                foreach (var episode in episodesArray)
                                {
                                    episodes.Add(new Episode
                                    {
                                        Id = episode["id"]?.ToString(),
                                        Title = episode["title"]?.ToString(),
                                        File = episode["file"]?.ToString(),
                                        Poster = episode["poster"]?.ToString(),
                                        Subtitle = episode["subtitle"]?.ToString()
                                    });
                                }
                            }

                            episodes = episodes
                                .OrderBy(item => ExtractEpisodeNumber(item.Title) is null)
                                .ThenBy(item => ExtractEpisodeNumber(item.Title) ?? 0)
                                .ToList();

                            voice.Seasons.Add(new Season
                            {
                                Title = seasonTitle,
                                Episodes = episodes
                            });
                        }
                    }

                    voices.Add(voice);
                }

                return voices;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno ParseVoicesJson error: {ex.Message}");
                return new List<Voice>();
            }
        }

        private string ExtractPlayerJson(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var startIndex = FindFileArrayStart(html);
            if (startIndex < 0)
                return null;

            string jsonArray = ExtractBracketArray(html, startIndex);
            if (string.IsNullOrEmpty(jsonArray))
                return null;

            return jsonArray
                .Replace("\\'", "'")
                .Replace("\\\"", "\"");
        }

        private int FindFileArrayStart(string html)
        {
            int playerStart = html.IndexOf("Playerjs", StringComparison.OrdinalIgnoreCase);
            if (playerStart >= 0)
            {
                int playerIndex = FindFileArrayStartInRange(html, playerStart);
                if (playerIndex >= 0)
                    return playerIndex;
            }

            int index = FindFileArrayIndex(html, "file:'[");
            if (index >= 0)
                return index;

            index = FindFileArrayIndex(html, "file:\"[");
            if (index >= 0)
                return index;

            var match = Regex.Match(html, @"file\s*:\s*'?\[", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Index + match.Value.LastIndexOf('[');

            return -1;
        }

        private int FindFileArrayStartInRange(string html, int startIndex)
        {
            int searchStart = startIndex;
            int searchEnd = Math.Min(html.Length, startIndex + 200000);

            int tokenIndex = IndexOfIgnoreCase(html, "file:'[", searchStart, searchEnd);
            if (tokenIndex >= 0)
                return html.IndexOf('[', tokenIndex);

            tokenIndex = IndexOfIgnoreCase(html, "file:\"[", searchStart, searchEnd);
            if (tokenIndex >= 0)
                return html.IndexOf('[', tokenIndex);

            tokenIndex = IndexOfIgnoreCase(html, "file", searchStart, searchEnd);
            if (tokenIndex >= 0)
            {
                int bracketIndex = html.IndexOf('[', tokenIndex);
                if (bracketIndex >= 0 && bracketIndex < searchEnd)
                    return bracketIndex;
            }

            return -1;
        }

        private int IndexOfIgnoreCase(string text, string value, int startIndex, int endIndex)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < endIndex)
                return index;

            return -1;
        }

        private int FindFileArrayIndex(string html, string token)
        {
            int tokenIndex = html.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0)
                return -1;

            int bracketIndex = html.IndexOf('[', tokenIndex);
            return bracketIndex;
        }

        private string ExtractBracketArray(string text, int startIndex)
        {
            if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '[')
                return null;

            int depth = 0;
            bool inString = false;
            bool escape = false;
            char quoteChar = '\0';

            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escape = true;
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

        private int? ExtractEpisodeNumber(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var match = Regex.Match(value, @"(\d+)");
            if (!match.Success)
                return null;

            if (int.TryParse(match.Groups[1].Value, out int num))
                return num;

            return null;
        }

        public string ExtractAshdiPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var match = Regex.Match(value, @"https?://(?:www\.)?ashdi\.vip/((?:vod|serial)/\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;

            match = Regex.Match(value, @"\b((?:vod|serial)/\d+)\b", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;

            return null;
        }

        public string BuildAshdiUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return path;

            return $"{AshdiHost}/{path.TrimStart('/')}";
        }

        public async Task<string> GetAshdiPath(string movieId)
        {
            if (string.IsNullOrWhiteSpace(movieId))
                return null;

            var page = await GetMoviePageContent(movieId);
            if (string.IsNullOrWhiteSpace(page))
                return null;

            var playerUrl = GetPlayerUrl(page);
            var path = ExtractAshdiPath(playerUrl);
            if (!string.IsNullOrWhiteSpace(path))
                return path;

            return ExtractAshdiPath(page);
        }

        public SearchResult SelectUaTUTItem(List<SearchResult> items, string imdbId, int? year, string title, string titleEn)
        {
            if (items == null || items.Count == 0)
                return null;

            var candidates = items.Where(item => ImdbMatch(item, imdbId) && YearMatch(item, year)).ToList();
            if (candidates.Count == 1)
                return candidates[0];
            if (candidates.Count > 1)
                return null;

            candidates = items.Where(item => ImdbMatch(item, imdbId) && TitleMatch(item, title, titleEn)).ToList();
            if (candidates.Count == 1)
                return candidates[0];
            if (candidates.Count > 1)
                return null;

            candidates = items.Where(item => YearMatch(item, year) && TitleMatch(item, title, titleEn)).ToList();
            if (candidates.Count == 1)
                return candidates[0];

            return null;
        }

        private bool ImdbMatch(SearchResult item, string imdbId)
        {
            if (string.IsNullOrWhiteSpace(imdbId) || item == null)
                return false;

            return string.Equals(item.ImdbId?.Trim(), imdbId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private bool YearMatch(SearchResult item, int? year)
        {
            if (year == null || item == null)
                return false;

            var itemYear = YearInt(item.Year);
            return itemYear.HasValue && itemYear.Value == year.Value;
        }

        private bool TitleMatch(SearchResult item, string title, string titleEn)
        {
            if (item == null)
                return false;

            string itemTitle = NormalizeTitle(item.Title);
            string itemTitleEn = NormalizeTitle(item.TitleEn);
            string targetTitle = NormalizeTitle(title);
            string targetTitleEn = NormalizeTitle(titleEn);

            return (itemTitle.Length > 0 && targetTitle.Length > 0 && itemTitle == targetTitle)
                || (itemTitle.Length > 0 && targetTitleEn.Length > 0 && itemTitle == targetTitleEn)
                || (itemTitleEn.Length > 0 && targetTitle.Length > 0 && itemTitleEn == targetTitle)
                || (itemTitleEn.Length > 0 && targetTitleEn.Length > 0 && itemTitleEn == targetTitleEn);
        }

        private string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string text = value.ToLowerInvariant();
            text = Regex.Replace(text, @"[^\w\s]+", " ");
            text = Regex.Replace(text, @"\b(season|сезон|частина|part|ova|special|movie|film)\b", " ");
            text = Regex.Replace(text, @"\b(\d+)(st|nd|rd|th)\b", "$1");
            text = Regex.Replace(text, @"\b\d+\b", " ");
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        private int? YearInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (int.TryParse(value.Trim(), out int result))
                return result;

            return null;
        }

        public async Task<(JObject item, string mediaType)?> FetchTmdbByImdb(string imdbId, int? year)
        {
            if (string.IsNullOrWhiteSpace(imdbId))
                return null;

            try
            {
                string apiKey = AppInit.conf?.tmdb?.api_key;
                if (string.IsNullOrWhiteSpace(apiKey))
                    return null;

                string tmdbUrl = $"http://{AppInit.conf.listen.localhost}:{AppInit.conf.listen.port}/tmdb/api/3/find/{imdbId}?external_source=imdb_id&api_key={apiKey}&language=en-US";

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent)
                };

                JObject payload = await Http.Get<JObject>(tmdbUrl, timeoutSeconds: 6, headers: headers);
                if (payload == null)
                    return null;

                var movieResults = payload["movie_results"] as JArray ?? new JArray();
                var tvResults = payload["tv_results"] as JArray ?? new JArray();

                var candidates = new List<(JObject item, string mediaType)>();
                foreach (var item in movieResults.OfType<JObject>())
                    candidates.Add((item, "movie"));
                foreach (var item in tvResults.OfType<JObject>())
                    candidates.Add((item, "tv"));

                if (candidates.Count == 0)
                    return null;

                if (year.HasValue)
                {
                    string yearText = year.Value.ToString();
                    foreach (var candidate in candidates)
                    {
                        string dateValue = candidate.mediaType == "movie"
                            ? candidate.item.Value<string>("release_date")
                            : candidate.item.Value<string>("first_air_date");

                        if (!string.IsNullOrWhiteSpace(dateValue) && dateValue.StartsWith(yearText, StringComparison.Ordinal))
                            return candidate;
                    }
                }

                return candidates[0];
            }
            catch (Exception ex)
            {
                _onLog($"Makhno TMDB fetch failed: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> PostWormholeAsync(object payload)
        {
            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("Content-Type", "application/json"),
                    new HeadersModel("User-Agent", Http.UserAgent)
                };

                string json = JsonConvert.SerializeObject(payload, Formatting.None);
                await Http.Post(WormholeHost, json, timeoutSeconds: 6, headers: headers, proxy: _proxyManager.Get());
                return true;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno wormhole insert failed: {ex.Message}");
                return false;
            }
        }

        private class WormholeResponse
        {
            public string play { get; set; }
        }
    }
}
