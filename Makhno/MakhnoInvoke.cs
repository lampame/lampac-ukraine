using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
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
        private const string WormholeHost = "https://wh.lme.isroot.in/";
        private const string AshdiHost = "https://ashdi.vip";
        private const string KlonHost = "https://klon.fun";
        private static readonly Regex Quality4kRegex = new Regex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase);
        private static readonly Regex QualityFhdRegex = new Regex(@"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b", RegexOptions.IgnoreCase);
        private static readonly Regex YearRegex = new Regex(@"(19|20)\d{2}", RegexOptions.IgnoreCase);

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

                string response = await Http.Get(_init.cors(url), timeoutSeconds: 4, headers: headers, proxy: _proxyManager.Get());
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

        public async Task<List<KlonSearchResult>> SearchKlonFUN(string imdbId, string title, string originalTitle)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(imdbId))
                {
                    var byImdb = await SearchKlonFUNByQuery(imdbId);
                    if (byImdb?.Count > 0)
                    {
                        _onLog($"Makhno KlonFUN: знайдено {byImdb.Count} результат(ів) за imdb_id={imdbId}");
                        return byImdb;
                    }
                }

                var queries = new[] { originalTitle, title }
                    .Where(q => !string.IsNullOrWhiteSpace(q))
                    .Select(q => q.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var results = new List<KlonSearchResult>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string query in queries)
                {
                    var partial = await SearchKlonFUNByQuery(query);
                    if (partial == null || partial.Count == 0)
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
                    _onLog($"Makhno KlonFUN: знайдено {results.Count} результат(ів) за назвою");
                    return results;
                }
            }
            catch (Exception ex)
            {
                _onLog($"Makhno KlonFUN: помилка пошуку - {ex.Message}");
            }

            return new List<KlonSearchResult>();
        }

        public KlonSearchResult SelectKlonFUNItem(List<KlonSearchResult> items, int? year, string title, string titleEn)
        {
            if (items == null || items.Count == 0)
                return null;

            if (items.Count == 1)
                return items[0];

            var byYearAndTitle = items
                .Where(item => YearMatch(item, year) && TitleMatch(item?.Title, title, titleEn))
                .ToList();

            if (byYearAndTitle.Count == 1)
                return byYearAndTitle[0];
            if (byYearAndTitle.Count > 1)
                return null;

            var byTitle = items
                .Where(item => TitleMatch(item?.Title, title, titleEn))
                .ToList();

            if (byTitle.Count == 1)
                return byTitle[0];
            if (byTitle.Count > 1)
                return null;

            var byYear = items
                .Where(item => YearMatch(item, year))
                .ToList();

            if (byYear.Count == 1)
                return byYear[0];

            return null;
        }

        public async Task<string> GetKlonFUNPlayerUrl(string itemUrl)
        {
            if (string.IsNullOrWhiteSpace(itemUrl))
                return null;

            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent),
                    new HeadersModel("Referer", KlonHost)
                };

                string html = await Http.Get(_init.cors(itemUrl), headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                string playerUrl = doc.DocumentNode
                    .SelectSingleNode("//div[contains(@class,'film-player')]//iframe")
                    ?.GetAttributeValue("data-src", null);

                if (string.IsNullOrWhiteSpace(playerUrl))
                {
                    playerUrl = doc.DocumentNode
                        .SelectSingleNode("//div[contains(@class,'film-player')]//iframe")
                        ?.GetAttributeValue("src", null);
                }

                if (string.IsNullOrWhiteSpace(playerUrl))
                {
                    playerUrl = doc.DocumentNode
                        .SelectSingleNode("//iframe[contains(@src,'ashdi.vip') or contains(@src,'zetvideo.net') or contains(@src,'/vod/') or contains(@src,'/serial/')]")
                        ?.GetAttributeValue("src", null);
                }

                return NormalizeUrl(KlonHost, playerUrl);
            }
            catch (Exception ex)
            {
                _onLog($"Makhno KlonFUN: помилка отримання плеєра - {ex.Message}");
                return null;
            }
        }

        public bool IsSerialPlayerUrl(string playerUrl)
        {
            return !string.IsNullOrWhiteSpace(playerUrl)
                && playerUrl.IndexOf("/serial/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<List<KlonSearchResult>> SearchKlonFUNByQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent),
                    new HeadersModel("Referer", KlonHost)
                };

                string form = $"do=search&subaction=search&story={WebUtility.UrlEncode(query)}";
                string html = await Http.Post(_init.cors(KlonHost), form, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var results = new List<KlonSearchResult>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'short-news__slide-item')]");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        string href = node.SelectSingleNode(".//a[contains(@class,'short-news__small-card__link')]")
                            ?.GetAttributeValue("href", null)
                            ?? node.SelectSingleNode(".//a[contains(@class,'card-link__style')]")
                                ?.GetAttributeValue("href", null);

                        href = NormalizeUrl(KlonHost, href);
                        if (string.IsNullOrWhiteSpace(href) || !seen.Add(href))
                            continue;

                        string itemTitle = CleanText(node.SelectSingleNode(".//div[contains(@class,'card-link__text')]")?.InnerText);
                        if (string.IsNullOrWhiteSpace(itemTitle))
                        {
                            itemTitle = CleanText(node.SelectSingleNode(".//a[contains(@class,'card-link__style')]")?.InnerText);
                        }

                        string poster = node.SelectSingleNode(".//img[contains(@class,'card-poster__img')]")
                            ?.GetAttributeValue("data-src", null);
                        if (string.IsNullOrWhiteSpace(poster))
                        {
                            poster = node.SelectSingleNode(".//img[contains(@class,'card-poster__img')]")
                                ?.GetAttributeValue("src", null);
                        }

                        string meta = CleanText(node.SelectSingleNode(".//div[contains(@class,'subscribe-label-module')]")?.InnerText);
                        int itemYear = 0;
                        if (!string.IsNullOrWhiteSpace(meta))
                        {
                            var yearMatch = YearRegex.Match(meta);
                            if (yearMatch.Success)
                                int.TryParse(yearMatch.Value, out itemYear);
                        }

                        if (string.IsNullOrWhiteSpace(itemTitle))
                            continue;

                        results.Add(new KlonSearchResult
                        {
                            Title = itemTitle,
                            Url = href,
                            Poster = NormalizeUrl(KlonHost, poster),
                            Year = itemYear
                        });
                    }
                }

                if (results.Count == 0)
                {
                    var anchors = doc.DocumentNode.SelectNodes("//a[.//span[contains(@class,'searchheading')]]");
                    if (anchors != null)
                    {
                        foreach (var anchor in anchors)
                        {
                            string href = NormalizeUrl(KlonHost, anchor.GetAttributeValue("href", null));
                            if (string.IsNullOrWhiteSpace(href) || !seen.Add(href))
                                continue;

                            string itemTitle = CleanText(anchor.SelectSingleNode(".//span[contains(@class,'searchheading')]")?.InnerText);
                            if (string.IsNullOrWhiteSpace(itemTitle))
                                continue;

                            results.Add(new KlonSearchResult
                            {
                                Title = itemTitle,
                                Url = href,
                                Poster = string.Empty,
                                Year = 0
                            });
                        }
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno KlonFUN: помилка запиту '{query}' - {ex.Message}");
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

            var response = await Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());

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
                var response = await Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());

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

        private static string NormalizeUrl(string host, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            string value = WebUtility.HtmlDecode(url.Trim());

            if (value.StartsWith("//"))
                return $"https:{value}";

            if (value.StartsWith("/"))
                return host.TrimEnd('/') + value;

            if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return host.TrimEnd('/') + "/" + value.TrimStart('/');

            return value;
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return HtmlEntity.DeEntitize(value).Trim();
        }

        public async Task<PlayerData> GetPlayerData(string playerUrl)
        {
            if (string.IsNullOrEmpty(playerUrl))
                return null;

            try
            {
                string sourceUrl = WithAshdiMultivoice(playerUrl);
                string requestUrl = sourceUrl;
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent)
                };

                if (sourceUrl.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase))
                {
                    headers.Add(new HeadersModel("Referer", "https://ashdi.vip/"));
                }

                if (ApnHelper.IsAshdiUrl(sourceUrl) && ApnHelper.IsEnabled(_init) && string.IsNullOrWhiteSpace(_init.webcorshost))
                    requestUrl = ApnHelper.WrapUrl(_init, sourceUrl);

                _onLog($"Makhno getting player data from: {requestUrl}");

                var response = await Http.Get(_init.cors(requestUrl), headers: headers, proxy: _proxyManager.Get());
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
                    string file = fileMatch.Groups[1].Value;
                    var posterMatch = Regex.Match(html, @"poster:[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    return new PlayerData
                    {
                        File = file,
                        Poster = posterMatch.Success ? posterMatch.Groups[1].Value : null,
                        Voices = new List<Voice>(),
                        Movies = new List<MovieVariant>()
                        {
                            new MovieVariant
                            {
                                File = file,
                                Quality = DetectQualityTag(file) ?? "auto",
                                Title = BuildMovieTitle("Основне джерело", file, 1)
                            }
                        }
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
                    var movies = ParseMovieVariantsJson(jsonData);
                    _onLog($"Makhno ParsePlayerData: voices={voices?.Count ?? 0}");
                    return new PlayerData
                    {
                        File = movies.FirstOrDefault()?.File,
                        Poster = null,
                        Voices = voices,
                        Movies = movies
                    };
                }

                var m3u8Match = Regex.Match(html, @"(https?://[^""'\s>]+\.m3u8[^""'\s>]*)", RegexOptions.IgnoreCase);
                if (m3u8Match.Success)
                {
                    _onLog("Makhno ParsePlayerData: fallback m3u8 match");
                    return new PlayerData
                    {
                        File = m3u8Match.Groups[1].Value,
                        Poster = null,
                        Voices = new List<Voice>(),
                        Movies = new List<MovieVariant>()
                        {
                            new MovieVariant
                            {
                                File = m3u8Match.Groups[1].Value,
                                Quality = DetectQualityTag(m3u8Match.Groups[1].Value) ?? "auto",
                                Title = BuildMovieTitle("Основне джерело", m3u8Match.Groups[1].Value, 1)
                            }
                        }
                    };
                }

                var sourceMatch = Regex.Match(html, @"<source[^>]*src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (sourceMatch.Success)
                {
                    _onLog("Makhno ParsePlayerData: fallback source match");
                    return new PlayerData
                    {
                        File = sourceMatch.Groups[1].Value,
                        Poster = null,
                        Voices = new List<Voice>(),
                        Movies = new List<MovieVariant>()
                        {
                            new MovieVariant
                            {
                                File = sourceMatch.Groups[1].Value,
                                Quality = DetectQualityTag(sourceMatch.Groups[1].Value) ?? "auto",
                                Title = BuildMovieTitle("Основне джерело", sourceMatch.Groups[1].Value, 1)
                            }
                        }
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

        private List<MovieVariant> ParseMovieVariantsJson(string jsonData)
        {
            try
            {
                var voicesArray = JsonConvert.DeserializeObject<List<JObject>>(jsonData);
                var movies = new List<MovieVariant>();
                if (voicesArray == null || voicesArray.Count == 0)
                    return movies;

                int index = 1;
                foreach (var item in voicesArray)
                {
                    string file = item?["file"]?.ToString();
                    if (string.IsNullOrWhiteSpace(file))
                        continue;

                    string rawTitle = item["title"]?.ToString();
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
                _onLog($"Makhno ParseMovieVariantsJson error: {ex.Message}");
                return new List<MovieVariant>();
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

        private bool YearMatch(KlonSearchResult item, int? year)
        {
            if (year == null || item == null || item.Year <= 0)
                return false;

            return item.Year == year.Value;
        }

        private bool TitleMatch(SearchResult item, string title, string titleEn)
        {
            if (item == null)
                return false;

            return TitleMatch(item.Title, item.TitleEn, title, titleEn);
        }

        private bool TitleMatch(string itemTitle, string title, string titleEn)
        {
            return TitleMatch(itemTitle, null, title, titleEn);
        }

        private bool TitleMatch(string itemTitleRaw, string itemTitleEnRaw, string title, string titleEn)
        {
            string itemTitle = NormalizeTitle(itemTitleRaw);
            string itemTitleEn = NormalizeTitle(itemTitleEnRaw);
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

        public async Task<(JObject item, string mediaType)?> FetchTmdbByImdb(string imdbId, int? year, bool isSerial)
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

                string preferredMediaType = isSerial ? "tv" : "movie";
                var orderedCandidates = candidates
                    .Where(c => c.mediaType == preferredMediaType)
                    .Concat(candidates.Where(c => c.mediaType != preferredMediaType))
                    .ToList();

                if (year.HasValue)
                {
                    string yearText = year.Value.ToString();
                    foreach (var candidate in orderedCandidates)
                    {
                        string dateValue = candidate.mediaType == "movie"
                            ? candidate.item.Value<string>("release_date")
                            : candidate.item.Value<string>("first_air_date");

                        if (!string.IsNullOrWhiteSpace(dateValue) && dateValue.StartsWith(yearText, StringComparison.Ordinal))
                            return candidate;
                    }
                }

                return orderedCandidates[0];
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
                await Http.Post(_init.cors(WormholeHost), json, timeoutSeconds: 6, headers: headers, proxy: _proxyManager.Get());
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
