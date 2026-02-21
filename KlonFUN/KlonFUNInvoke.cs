using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using KlonFUN.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;

namespace KlonFUN
{
    public class KlonFUNInvoke
    {
        private static readonly Regex DirectFileRegex = new Regex(@"file\s*:\s*['""](?<url>https?://[^'"">\s]+\.m3u8[^'"">\s]*)['""]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex YearRegex = new Regex(@"(19|20)\d{2}", RegexOptions.IgnoreCase);
        private static readonly Regex NumberRegex = new Regex(@"(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex Quality4kRegex = new Regex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase);
        private static readonly Regex QualityFhdRegex = new Regex(@"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b", RegexOptions.IgnoreCase);

        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public KlonFUNInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
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
                        _hybridCache.Set(cacheKey, byImdb, cacheTime(20, init: _init));
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
                    _hybridCache.Set(cacheKey, results, cacheTime(20, init: _init));
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
                string html = await Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());
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

                _hybridCache.Set(cacheKey, result, cacheTime(40, init: _init));
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
                string playerHtml = await GetPlayerHtml(WithAshdiMultivoice(playerUrl));
                if (string.IsNullOrWhiteSpace(playerHtml))
                    return null;

                var streams = new List<MovieStream>();

                JArray playerArray = ParsePlayerArray(playerHtml);
                if (playerArray != null)
                {
                    int index = 1;
                    foreach (JObject item in playerArray.OfType<JObject>())
                    {
                        string link = item.Value<string>("file");
                        if (string.IsNullOrWhiteSpace(link))
                            continue;

                        string voiceTitle = FormatMovieTitle(item.Value<string>("title"), link, index);

                        streams.Add(new MovieStream
                        {
                            Title = voiceTitle,
                            Link = link
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
                            Title = FormatMovieTitle("Основне джерело", directMatch.Groups["url"].Value, 1),
                            Link = directMatch.Groups["url"].Value
                        });
                    }
                }

                if (streams.Count > 0)
                {
                    _hybridCache.Set(cacheKey, streams, cacheTime(30, init: _init));
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

                JArray playerArray = ParsePlayerArray(playerHtml);
                if (playerArray == null || playerArray.Count == 0)
                    return null;

                var structure = new SerialStructure();
                var voiceCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (JObject voiceObj in playerArray.OfType<JObject>())
                {
                    var seasonsRaw = voiceObj["folder"] as JArray;
                    if (seasonsRaw == null || seasonsRaw.Count == 0)
                        continue;

                    string baseName = CleanText(voiceObj.Value<string>("title"));
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
                    foreach (JObject seasonObj in seasonsRaw.OfType<JObject>())
                    {
                        string seasonTitle = seasonObj.Value<string>("title");
                        int seasonNumber = ParseNumber(seasonTitle, seasonFallback);

                        var episodesRaw = seasonObj["folder"] as JArray;
                        if (episodesRaw == null || episodesRaw.Count == 0)
                        {
                            seasonFallback++;
                            continue;
                        }

                        var episodes = new List<SerialEpisode>();
                        int episodeFallback = 1;

                        foreach (JObject episodeObj in episodesRaw.OfType<JObject>())
                        {
                            string link = episodeObj.Value<string>("file");
                            if (string.IsNullOrWhiteSpace(link))
                                continue;

                            string episodeTitle = CleanText(episodeObj.Value<string>("title"));
                            int episodeNumber = ParseNumber(episodeTitle, episodeFallback);

                            episodes.Add(new SerialEpisode
                            {
                                Number = episodeNumber,
                                Title = string.IsNullOrWhiteSpace(episodeTitle) ? $"Серія {episodeNumber}" : episodeTitle,
                                Link = link
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

                    _hybridCache.Set(cacheKey, structure, cacheTime(30, init: _init));
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
                string html = await Http.Post(_init.cors(_init.host), form, headers: headers, proxy: _proxyManager.Get());
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
                    _hybridCache.Set(cacheKey, results, cacheTime(20, init: _init));
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
            return await Http.Get(_init.cors(requestUrl), headers: headers, proxy: _proxyManager.Get());
        }

        private static JArray ParsePlayerArray(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            string json = ExtractFileArray(html);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            json = WebUtility.HtmlDecode(json).Replace("\\/", "/");

            try
            {
                return JsonConvert.DeserializeObject<JArray>(json);
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

        private static string FormatMovieTitle(string rawTitle, string streamUrl, int index)
        {
            string title = StripMoviePrefix(CleanText(rawTitle));
            if (string.IsNullOrWhiteSpace(title))
                title = $"Варіант {index}";

            string tag = DetectQualityTag($"{title} {streamUrl}");
            if (string.IsNullOrWhiteSpace(tag))
                return title;

            if (title.StartsWith("[4K]", StringComparison.OrdinalIgnoreCase) || title.StartsWith("[FHD]", StringComparison.OrdinalIgnoreCase))
                return title;

            return $"{tag} {title}";
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

        private static string DetectQualityTag(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            if (Quality4kRegex.IsMatch(source))
                return "[4K]";

            if (QualityFhdRegex.IsMatch(source))
                return "[FHD]";

            return null;
        }

        public static TimeSpan cacheTime(int multiaccess, int home = 5, int mikrotik = 2, OnlinesSettings init = null, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);

            int ctime = AppInit.conf.mikrotik
                ? mikrotik
                : AppInit.conf.multiaccess
                    ? init != null && init.cache_time > 0 ? init.cache_time : multiaccess
                    : home;

            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }
    }
}
