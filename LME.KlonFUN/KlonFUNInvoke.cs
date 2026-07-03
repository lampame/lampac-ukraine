using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using LME.KlonFUN.Models;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;

namespace LME.KlonFUN
{
    public class KlonFUNInvoke
    {
        private static readonly Regex DirectFileRegex = new Regex(@"file\s*:\s*['""](?<url>https?://[^'"">\s]+\.m3u8[^'"">\s]*)['""]", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YearRegex = new Regex(@"(19|20)\d{2}", RegexOptions.Compiled);

        private static readonly Regex NumberRegex = new Regex(@"(\d+)", RegexOptions.Compiled);

        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        public KlonFUNInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        public async Task<List<SearchResult>> Search(string imdbId, string title, string originalTitle)
        {
            string cacheKey = $"KlonFUN:search:{imdbId}:{title}:{originalTitle}";
            if (_hybridCache.TryGetValue(cacheKey, out List<SearchResult> cached))
                return cached;

            try
            {
                if (!string.IsNullOrWhiteSpace(imdbId))
                {
                    var byImdb = await SearchByQuery(imdbId);
                    if (byImdb?.Count > 0)
                    {
                        _hybridCache.Set(cacheKey, byImdb, CacheHelper.CacheTime(20, init: _init));
                        _onLog?.Invoke($"KlonFUN: знайдено {byImdb.Count} результат(ів) за imdb_id={imdbId}");
                        return byImdb;
                    }
                }

                var queries = new[] { originalTitle, title }
                    .Where(q => !string.IsNullOrWhiteSpace(q))
                    .Select(q => q.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var results = new List<SearchResult>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var query in queries)
                {
                    var partial = await SearchByQuery(query);
                    if (partial == null)
                        continue;

                    foreach (var item in partial)
                    {
                        if (!string.IsNullOrWhiteSpace(item?.Url) && seen.Add(item.Url))
                            results.Add(item);
                    }

                    if (results.Count > 0)
                        break;
                }

                if (results.Count > 0)
                {
                    _hybridCache.Set(cacheKey, results, CacheHelper.CacheTime(20, init: _init));
                    _onLog?.Invoke($"KlonFUN: знайдено {results.Count} результат(ів) за назвою");
                    return results;
                }
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"KlonFUN: помилка пошуку - {ex.Message}");
            }

            return null;
        }

        public async Task<KlonItem> GetItem(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string cacheKey = $"KlonFUN:item:{url}";
            if (_hybridCache.TryGetValue(cacheKey, out KlonItem cached))
                return cached;

            try
            {
                var headers = DefaultHeaders();
                string html = await HttpHelper.GetAsync(_httpHydra, _init, url, headers, _proxyManager);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                string title = CleanText(doc.DocumentNode.SelectSingleNode("//h1[contains(@class,'seo-h1__position')]")?.InnerText);

                string poster = doc.DocumentNode
                    .SelectSingleNode("//img[contains(@class,'cover-image')]")
                    ?.GetAttributeValue("data-src", null);

                if (string.IsNullOrWhiteSpace(poster))
                {
                    poster = doc.DocumentNode
                        .SelectSingleNode("//img[contains(@class,'cover-image')]")
                        ?.GetAttributeValue("src", null);
                }

                poster = NormalizeUrl(poster);

                string playerUrl = doc.DocumentNode
                    .SelectSingleNode("//div[contains(@class,'film-player')]//iframe")
                    ?.GetAttributeValue("data-src", null);

                if (string.IsNullOrWhiteSpace(playerUrl))
                {
                    playerUrl = doc.DocumentNode
                        .SelectSingleNode("//div[contains(@class,'film-player')]//iframe")
                        ?.GetAttributeValue("src", null);
                }

                playerUrl = NormalizeUrl(playerUrl);

                int year = 0;
                var yearNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'table__category') and contains(.,'Рік')]/following-sibling::div");
                if (yearNode != null)
                {
                    var yearMatch = YearRegex.Match(yearNode.InnerText ?? string.Empty);
                    if (yearMatch.Success)
                        int.TryParse(yearMatch.Value, out year);
                }

                var result = new KlonItem
                {
                    Url = url,
                    Title = title,
                    Poster = poster,
                    PlayerUrl = playerUrl,
                    IsSerialPlayer = IsSerialPlayer(playerUrl),
                    Year = year
                };

                _hybridCache.Set(cacheKey, result, CacheHelper.CacheTime(40, init: _init));
                return result;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"KlonFUN: помилка читання сторінки {url} - {ex.Message}");
                return null;
            }
        }

        public async Task<List<MovieStream>> GetMovieStreams(string playerUrl)
        {
            if (string.IsNullOrWhiteSpace(playerUrl))
                return null;

            string cacheKey = $"KlonFUN:movie:{playerUrl}";
            if (_hybridCache.TryGetValue(cacheKey, out List<MovieStream> cached))
                return cached;

            try
            {
                    string playerHtml = await GetPlayerHtml(ApnExtensions.WithAshdiMultivoice(playerUrl));
                if (string.IsNullOrWhiteSpace(playerHtml))
                    return null;

                var streams = new List<MovieStream>();

                JsonArray playerArray = ParsePlayerArray(playerHtml);
                if (playerArray != null)
                {
                    int index = 1;
                    foreach (JsonObject item in playerArray.OfType<JsonObject>())
                    {
                        string link = (string?)item["file"];
                        if (string.IsNullOrWhiteSpace(link))
                            continue;

                        string voiceTitle = QualityHelper.BuildDisplayTitle((string?)item["title"], link, index);

                        streams.Add(new MovieStream
                        {
                            Title = voiceTitle,
                            Link = link,
                            Subtitles = ApnHelper.ParseSubtitles((string?)item["subtitle"])
                        });

                        index++;
                    }
                }

                if (streams.Count == 0)
                {
                    var directMatch = DirectFileRegex.Match(playerHtml);
                    if (directMatch.Success)
                    {
                        streams.Add(new MovieStream
                        {
                            Title = QualityHelper.BuildDisplayTitle("Основне джерело", directMatch.Groups["url"].Value, 1),
                            Link = directMatch.Groups["url"].Value,
                            Subtitles = ApnHelper.ParseSubtitles(ApnHelper.ExtractPlayerSubtitle(playerHtml))
                        });
                    }
                }

                if (streams.Count > 0)
                {
                    _hybridCache.Set(cacheKey, streams, CacheHelper.CacheTime(30, init: _init));
                    return streams;
                }
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"KlonFUN: помилка парсингу плеєра фільму - {ex.Message}");
            }

            return null;
        }

        public async Task<SerialStructure> GetSerialStructure(string playerUrl)
        {
            if (string.IsNullOrWhiteSpace(playerUrl))
                return null;

            string cacheKey = $"KlonFUN:serial:{playerUrl}";
            if (_hybridCache.TryGetValue(cacheKey, out SerialStructure cached))
                return cached;

            try
            {
                string playerHtml = await GetPlayerHtml(playerUrl);
                if (string.IsNullOrWhiteSpace(playerHtml))
                    return null;

                JsonArray playerArray = ParsePlayerArray(playerHtml);
                if (playerArray == null || playerArray.Count == 0)
                    return null;

                var structure = new SerialStructure();
                var voiceCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (JsonObject voiceObj in playerArray.OfType<JsonObject>())
                {
                    var seasonsRaw = voiceObj["folder"] as JsonArray;
                    if (seasonsRaw == null || seasonsRaw.Count == 0)
                        continue;

                    string baseName = CleanText((string?)voiceObj["title"]);
                    if (string.IsNullOrWhiteSpace(baseName))
                        baseName = "Озвучення";

                    string displayName = BuildUniqueVoiceName(baseName, voiceCounter);

                    var voice = new SerialVoice
                    {
                        Key = displayName,
                        DisplayName = displayName,
                        Seasons = new Dictionary<int, List<SerialEpisode>>()
                    };

                    int seasonFallback = 1;
                    foreach (JsonObject seasonObj in seasonsRaw.OfType<JsonObject>())
                    {
                        string seasonTitle = (string?)seasonObj["title"];
                        int seasonNumber = ParseNumber(seasonTitle, seasonFallback);

                        var episodesRaw = seasonObj["folder"] as JsonArray;
                        if (episodesRaw == null || episodesRaw.Count == 0)
                        {
                            seasonFallback++;
                            continue;
                        }

                        var episodes = new List<SerialEpisode>();
                        int episodeFallback = 1;

                        foreach (JsonObject episodeObj in episodesRaw.OfType<JsonObject>())
                        {
                            string link = (string?)episodeObj["file"];
                            if (string.IsNullOrWhiteSpace(link))
                                continue;

                            string episodeTitle = CleanText((string?)episodeObj["title"]);
                            int episodeNumber = ParseNumber(episodeTitle, episodeFallback);

                            episodes.Add(new SerialEpisode
                            {
                                Number = episodeNumber,
                                Title = string.IsNullOrWhiteSpace(episodeTitle) ? $"Серія {episodeNumber}" : episodeTitle,
                                Link = link,
                                Subtitles = ApnHelper.ParseSubtitles((string?)episodeObj["subtitle"])
                            });

                            episodeFallback++;
                        }

                        if (episodes.Count > 0)
                            voice.Seasons[seasonNumber] = episodes.OrderBy(e => e.Number).ToList();

                        seasonFallback++;
                    }

                    if (voice.Seasons.Count > 0)
                        structure.Voices.Add(voice);
                }

                if (structure.Voices.Count > 0)
                {
                    structure.Voices = structure.Voices
                        .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    _hybridCache.Set(cacheKey, structure, CacheHelper.CacheTime(30, init: _init));
                    return structure;
                }
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"KlonFUN: помилка парсингу структури серіалу - {ex.Message}");
            }

            return null;
        }

        public bool IsSerialPlayer(string playerUrl)
        {
            return !string.IsNullOrWhiteSpace(playerUrl)
                && playerUrl.IndexOf("/serial/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<List<SearchResult>> SearchByQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            string cacheKey = $"KlonFUN:query:{query}";
            if (_hybridCache.TryGetValue(cacheKey, out List<SearchResult> cached))
                return cached;

            try
            {
                var headers = DefaultHeaders();

                string form = $"do=search&subaction=search&story={HttpUtility.UrlEncode(query)}";
                string html = await HttpPost(_init.host, form, headers);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var results = new List<SearchResult>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'short-news__slide-item')]");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        string href = node.SelectSingleNode(".//a[contains(@class,'short-news__small-card__link')]")?.GetAttributeValue("href", null)
                            ?? node.SelectSingleNode(".//a[contains(@class,'card-link__style')]")?.GetAttributeValue("href", null);

                        href = NormalizeUrl(href);
                        if (string.IsNullOrWhiteSpace(href) || !seen.Add(href))
                            continue;

                        string title = CleanText(node.SelectSingleNode(".//div[contains(@class,'card-link__text')]")?.InnerText);
                        if (string.IsNullOrWhiteSpace(title))
                            title = CleanText(node.SelectSingleNode(".//a[contains(@class,'card-link__style')]")?.InnerText);

                        string poster = node.SelectSingleNode(".//img[contains(@class,'card-poster__img')]")?.GetAttributeValue("data-src", null);
                        if (string.IsNullOrWhiteSpace(poster))
                            poster = node.SelectSingleNode(".//img[contains(@class,'card-poster__img')]")?.GetAttributeValue("src", null);

                        string meta = CleanText(node.SelectSingleNode(".//div[contains(@class,'subscribe-label-module')]")?.InnerText);
                        int year = 0;
                        if (!string.IsNullOrWhiteSpace(meta))
                        {
                            var yearMatch = YearRegex.Match(meta);
                            if (yearMatch.Success)
                                int.TryParse(yearMatch.Value, out year);
                        }

                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            results.Add(new SearchResult
                            {
                                Title = title,
                                Url = href,
                                Poster = NormalizeUrl(poster),
                                Year = year
                            });
                        }
                    }
                }

                if (results.Count == 0)
                {
                    // Резервний парсер для спрощеної HTML-відповіді (наприклад, AJAX search).
                    var anchors = doc.DocumentNode.SelectNodes("//a[.//span[contains(@class,'searchheading')]]");
                    if (anchors != null)
                    {
                        foreach (var anchor in anchors)
                        {
                            string href = NormalizeUrl(anchor.GetAttributeValue("href", null));
                            if (string.IsNullOrWhiteSpace(href) || !seen.Add(href))
                                continue;

                            string title = CleanText(anchor.SelectSingleNode(".//span[contains(@class,'searchheading')]")?.InnerText);
                            if (string.IsNullOrWhiteSpace(title))
                                continue;

                            results.Add(new SearchResult
                            {
                                Title = title,
                                Url = href,
                                Poster = string.Empty,
                                Year = 0
                            });
                        }
                    }
                }

                if (results.Count > 0)
                {
                    _hybridCache.Set(cacheKey, results, CacheHelper.CacheTime(20, init: _init));
                    return results;
                }
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"KlonFUN: помилка запиту пошуку '{query}' - {ex.Message}");
            }

            return null;
        }

        private async Task<string> GetPlayerHtml(string playerUrl)
        {
            if (string.IsNullOrWhiteSpace(playerUrl))
                return null;

            string requestUrl = playerUrl;
            if (ApnHelper.IsAshdiUrl(playerUrl) && ApnHelper.IsEnabled(_init) && string.IsNullOrWhiteSpace(_init.webcorshost))
                requestUrl = ApnHelper.WrapUrl(_init, playerUrl);

            var headers = DefaultHeaders();
            return await HttpHelper.GetAsync(_httpHydra, _init, requestUrl, headers, _proxyManager);
        }

        private static JsonArray ParsePlayerArray(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            string json = ExtractFileArray(html);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            json = WebUtility.HtmlDecode(json).Replace("\\/", "/");

            try
            {
                return JsonNode.Parse(json) as JsonArray;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractFileArray(string html)
        {
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

                int depth = 0;
                bool inString = false;
                bool escaped = false;

                for (int i = startIndex; i < html.Length; i++)
                {
                    char ch = html[i];

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

                        if (ch == '"')
                            inString = false;

                        continue;
                    }

                    if (ch == '"')
                    {
                        inString = true;
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
                            return html.Substring(startIndex, i - startIndex + 1);
                    }
                }

                return null;
            }

            return null;
        }

        private List<HeadersModel> DefaultHeaders()
        {
            return new List<HeadersModel>
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host)
            };
        }

        private string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            string value = WebUtility.HtmlDecode(url.Trim());

            if (value.StartsWith("//"))
                return "https:" + value;

            if (value.StartsWith("/"))
                return _init.host.TrimEnd('/') + value;

            if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return _init.host.TrimEnd('/') + "/" + value.TrimStart('/');

            return value;
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return HtmlEntity.DeEntitize(value).Trim();
        }

        private static int ParseNumber(string value, int fallback)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var match = NumberRegex.Match(value);
                if (match.Success && int.TryParse(match.Value, out int parsed) && parsed > 0)
                    return parsed;
            }

            return fallback;
        }

        private static string BuildUniqueVoiceName(string baseName, Dictionary<string, int> voiceCounter)
        {
            if (!voiceCounter.TryGetValue(baseName, out int count))
            {
                voiceCounter[baseName] = 1;
                return baseName;
            }

            count++;
            voiceCounter[baseName] = count;
            return $"{baseName} #{count}";
        }

        private Task<string> HttpPost(string url, string data, List<HeadersModel> headers)
        {
            if (_httpHydra != null)
                return _httpHydra.Post(url, data, newheaders: headers);

            return Http.Post(_init.cors(url), data, headers: headers, proxy: _proxyManager.Get());
        }
    }
}
