using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using LME.UAKino.Models;
using HtmlAgilityPack;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using LME.Shared.Engine;

namespace LME.UAKino
{
    public class UAKinoInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        public UAKinoInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        public async Task<List<SearchResult>> Search(string title, string original_title, int year, string imdb_id, CancellationToken ct = default)
        {
            string query = BuildSearchQuery(title, original_title, imdb_id);
            if (string.IsNullOrEmpty(query))
                return null;

            string memKey = $"UAKino:search:{query}";
            string negKey = $"neg:{memKey}";

            if (_hybridCache.TryGetValue(negKey, out string isNeg) && isNeg == "timeout")
                return null;

            // ponytail: single-flight — factory виконається рівно 1 раз при паралельних запитах
            if (_hybridCache.TryGetValue(memKey, out List<SearchResult> cached))
                return cached;

            return await SingleFlightCache.GetOrCreateAsync<List<SearchResult>>(memKey, async token =>
            {
                if (_hybridCache.TryGetValue(negKey, out string isNegInside) && isNegInside == "timeout")
                    return null;

                // double-check після отримання lock
                if (_hybridCache.TryGetValue(memKey, out List<SearchResult> hit))
                    return hit;

                try
                {
                    _onLog?.Invoke($"UAKino search: {query}");

                    string url = $"{_init.host}/engine/lazydev/dle_search/ajax.php";
                    string body = $"story={HttpUtility.UrlEncode(query)}&thisUrl=/ua/";

                    var headers = new List<HeadersModel>()
                    {
                        new HeadersModel("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.3 Safari/605.1.15"),
                        new HeadersModel("Referer", $"{_init.host}/ua/"),
                        new HeadersModel("X-Requested-With", "XMLHttpRequest"),
                        new HeadersModel("Origin", _init.host),
                        new HeadersModel("Accept", "*/*"),
                        new HeadersModel("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8")
                    };

                    string json = await Http.Post(_init.cors(url), body, headers: headers, proxy: _proxyManager.Get());
                    if (string.IsNullOrEmpty(json))
                        return null;

                    using var jsonDoc = JsonDocument.Parse(json);
                    if (!jsonDoc.RootElement.TryGetProperty("content", out JsonElement contentElem))
                        return null;

                    string html = contentElem.GetString();
                    if (string.IsNullOrEmpty(html))
                        return null;

                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(html);

                    var rawItems = ParseRawItems(htmlDoc);
                    var results = GroupByShow(rawItems);

                    if (results.Count > 0)
                        _hybridCache.Set(memKey, results, CacheHelper.CacheTime(20));

                    return results;
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                {
                    _onLog?.Invoke($"UAKino search timeout/cancelled error: {ex.Message}");
                    _hybridCache.Set(negKey, "timeout", TimeSpan.FromSeconds(60));
                    throw;
                }
                catch (Exception ex)
                {
                    _onLog?.Invoke($"UAKino search error: {ex.Message}");
                    return null;
                }
            }, ct);
        }

        /// <summary>
        /// Отримати плейлист (озвучки + епізоди) за news_id
        /// </summary>
        public async Task<List<VoiceGroup>> GetPlaylist(string newsId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(newsId))
                return null;

            string memKey = $"UAKino:playlist:{newsId}";
            string negKey = $"neg:{memKey}";

            if (_hybridCache.TryGetValue(negKey, out string isNeg) && isNeg == "timeout")
                return null;

            // ponytail: single-flight — найдорожчий виклик (парсинг плейлиста), гарантуємо 1 HTTP-запит
            if (_hybridCache.TryGetValue(memKey, out List<VoiceGroup> cached))
                return cached;

            return await SingleFlightCache.GetOrCreateAsync<List<VoiceGroup>>(memKey, async token =>
            {
                if (_hybridCache.TryGetValue(negKey, out string isNegInside) && isNegInside == "timeout")
                    return null;

                if (_hybridCache.TryGetValue(memKey, out List<VoiceGroup> hit))
                    return hit;

                try
                {
                    _onLog?.Invoke($"UAKino playlist: {newsId}");

                    string url = $"{_init.host}/engine/ajax/playlists.php?news_id={newsId}&xfield=playlist";

                    var headers = new List<HeadersModel>()
                    {
                        new HeadersModel("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.3 Safari/605.1.15"),
                        new HeadersModel("Referer", $"{_init.host}/{newsId}-"),
                        new HeadersModel("X-Requested-With", "XMLHttpRequest"),
                        new HeadersModel("Accept", "application/json, text/javascript, */*; q=0.01")
                    };

                    string json = await HttpHelper.GetAsync(_httpHydra, _init, url, headers, _proxyManager);
                    if (string.IsNullOrEmpty(json))
                        return null;

                    using var jsonDoc = JsonDocument.Parse(json);
                    if (!jsonDoc.RootElement.TryGetProperty("response", out JsonElement responseElem))
                        return null;

                    string html = responseElem.GetString();
                    if (string.IsNullOrEmpty(html))
                        return null;

                    var voices = ParsePlaylistHtml(html);
                    if (voices.Count > 0)
                        _hybridCache.Set(memKey, voices, CacheHelper.CacheTime(30));

                    return voices;
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                {
                    _onLog?.Invoke($"UAKino playlist timeout/cancelled error: {ex.Message}");
                    _hybridCache.Set(negKey, "timeout", TimeSpan.FromSeconds(60));
                    throw;
                }
                catch (Exception ex)
                {
                    _onLog?.Invoke($"UAKino playlist error: {ex.Message}");
                    return null;
                }
            }, ct);
        }

        /// <summary>
        /// Fallback: отримати стрім з HTML сторінки фільму коли playlist API недоступний
        /// Парсить &lt;link itemprop="video" value="..."&gt; або &lt;iframe id="pre" src="..."&gt;
        /// </summary>
        public async Task<string> GetPageFallbackUrl(string pageUrl)
        {
            if (string.IsNullOrEmpty(pageUrl))
                return null;

            try
            {
                _onLog?.Invoke($"UAKino page fallback: {pageUrl}");

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.3 Safari/605.1.15"),
                    new HeadersModel("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                    new HeadersModel("Referer", _init.host)
                };

                string html = await HttpHelper.GetAsync(_httpHydra, _init, pageUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Спершу пробуємо <link itemprop="video" value="...">
                var linkTag = doc.DocumentNode.SelectSingleNode("//link[@itemprop='video']");
                if (linkTag != null)
                {
                    string value = linkTag.GetAttributeValue("value", "");
                    if (!string.IsNullOrEmpty(value))
                        return NormalizeUrl(value);
                }

                // Fallback до <iframe id="pre" src="...">
                var iframeTag = doc.DocumentNode.SelectSingleNode("//iframe[@id='pre']");
                if (iframeTag != null)
                {
                    string src = iframeTag.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src))
                        return NormalizeUrl(src);
                }

                return null;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UAKino page fallback error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Резолв Ashdi VOD сторінки: отримати реальний .m3u8 стрім з Playerjs file:'...'
        /// </summary>
        public async Task<string> ResolveAshdiVod(string vodUrl)
        {
            if (string.IsNullOrEmpty(vodUrl) || !ApnHelper.IsAshdiUrl(vodUrl))
                return vodUrl;

            try
            {
                string fetchUrl = vodUrl;
                // Не додаємо ?multivoice — кожен VOD має свій унікальний стрім
                // ?multivoice змішує всі голоси в один масив

                _onLog?.Invoke($"UAKino resolve Ashdi: {fetchUrl}");

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent),
                    new HeadersModel("Referer", "https://ashdi.vip/")
                };

                if (ApnHelper.IsEnabled(_init) && string.IsNullOrWhiteSpace(_init.webcorshost))
                    fetchUrl = ApnHelper.WrapUrl(_init, fetchUrl);

                string html = await HttpHelper.GetAsync(_httpHydra, _init, fetchUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(html))
                    return vodUrl;

                // Спершу простий pattern file:'url'
                var fileMatch = Regex.Match(html, @"file:\s*'([^']+)'", RegexOptions.IgnoreCase);
                if (!fileMatch.Success)
                    fileMatch = Regex.Match(html, @"file:\s*""([^""]+)""", RegexOptions.IgnoreCase);

                if (fileMatch.Success)
                {
                    string resolvedUrl = fileMatch.Groups[1].Value;
                    if (!string.IsNullOrEmpty(resolvedUrl) && !resolvedUrl.StartsWith("["))
                    {
                        _onLog?.Invoke($"UAKino resolved Ashdi: {resolvedUrl}");
                        return resolvedUrl;
                    }
                }

                // Складний масив — знаходимо file:'[' і витягуємо збалансований JSON
                int arrayStart = FindAshdiJsonArray(html);
                if (arrayStart >= 0)
                {
                    string jsonArray = ExtractBalancedBrackets(html, arrayStart);
                    if (!string.IsNullOrEmpty(jsonArray))
                    {
                        try
                        {
                            using var arr = JsonDocument.Parse(jsonArray);
                            if (arr.RootElement.ValueKind == JsonValueKind.Array && arr.RootElement.GetArrayLength() > 0)
                            {
                                string firstFile = arr.RootElement[0].GetProperty("file").GetString();
                                if (!string.IsNullOrEmpty(firstFile))
                                {
                                    _onLog?.Invoke($"UAKino resolved Ashdi (array): {firstFile}");
                                    return firstFile;
                                }
                            }
                        }
                        catch { }
                    }
                }

                return vodUrl;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UAKino resolve Ashdi error: {ex.Message}");
                return vodUrl;
            }
        }

        /// <summary>
        /// Резолв Ashdi VOD з ?multivoice: повертає ВСІ стріми з JSON масиву
        /// або один стрім якщо файл один (не масив)
        /// </summary>
        public async Task<List<(string file, string title)>> ResolveAshdiVodAll(string vodUrl)
        {
            var result = new List<(string file, string title)>();

            if (string.IsNullOrEmpty(vodUrl) || !ApnHelper.IsAshdiUrl(vodUrl))
            {
                if (!string.IsNullOrEmpty(vodUrl))
                    result.Add((vodUrl, null));
                return result;
            }

            try
            {
                _onLog?.Invoke($"UAKino resolve Ashdi all: {vodUrl}");

                // Для ?multivoice — Ашді повертає всі стріми в одному масиві
                string fetchUrl = vodUrl;
                if (!fetchUrl.Contains("multivoice"))
                    fetchUrl += (fetchUrl.Contains("?") ? "&" : "?") + "multivoice";

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent),
                    new HeadersModel("Referer", "https://ashdi.vip/")
                };

                if (ApnHelper.IsEnabled(_init) && string.IsNullOrWhiteSpace(_init.webcorshost))
                    fetchUrl = ApnHelper.WrapUrl(_init, fetchUrl);

                string html = await HttpHelper.GetAsync(_httpHydra, _init, fetchUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(html))
                {
                    result.Add((vodUrl, null));
                    return result;
                }

                // 1. Спершу простий pattern file:'url' (одиничний стрім, не масив)
                var fileMatch = Regex.Match(html, @"file:\s*'([^']+)'", RegexOptions.IgnoreCase);
                if (!fileMatch.Success)
                    fileMatch = Regex.Match(html, @"file:\s*""([^""]+)""", RegexOptions.IgnoreCase);

                if (fileMatch.Success)
                {
                    string resolvedUrl = fileMatch.Groups[1].Value;
                    if (!string.IsNullOrEmpty(resolvedUrl) && !resolvedUrl.StartsWith("["))
                    {
                        _onLog?.Invoke($"UAKino resolved Ashdi: {resolvedUrl}");
                        result.Add((resolvedUrl, null));
                        return result;
                    }
                }

                // 2. JSON масив (file:'[{...}]')
                int arrayStart = FindAshdiJsonArray(html);
                if (arrayStart >= 0)
                {
                    string jsonArray = ExtractBalancedBrackets(html, arrayStart);
                    if (!string.IsNullOrEmpty(jsonArray))
                    {
                        try
                        {
                            using var arr = JsonDocument.Parse(jsonArray);
                            if (arr.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in arr.RootElement.EnumerateArray())
                                {
                                    string file = item.GetProperty("file").GetString();
                                    string title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                                    if (!string.IsNullOrEmpty(file))
                                        result.Add((file, title?.Trim()));
                                }
                            }
                        }
                        catch { }
                    }
                }

                // 3. Прямий .m3u8 в HTML (якщо file pattern не знайдено)
                if (result.Count == 0)
                {
                    var m3u8Match = Regex.Match(html, @"(https?://[^""'\s>]+\.m3u8[^""'\s>]*)", RegexOptions.IgnoreCase);
                    if (m3u8Match.Success)
                    {
                        _onLog?.Invoke($"UAKino resolved Ashdi (m3u8 fallback): {m3u8Match.Groups[1].Value}");
                        result.Add((m3u8Match.Groups[1].Value, null));
                        return result;
                    }
                }

                // 4. Якщо нічого не знайдено — повертаємо оригінал як fallback
                if (result.Count == 0)
                    result.Add((vodUrl, null));

                return result;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UAKino resolve Ashdi all error: {ex.Message}");
                result.Add((vodUrl, null));
                return result;
            }
        }

        /// <summary>
        /// Знайти позицію JSON масиву `[{...}]` після `file:'`
        /// </summary>
        private static int FindAshdiJsonArray(string html)
        {
            int idx = html.IndexOf("file:'[", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                idx = html.IndexOf("file:\"[", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return -1;

            int bracket = html.IndexOf('[', idx);
            return bracket;
        }

        /// <summary>
        /// Витягнути збалансований вміст між [ ] з урахуванням вкладеності та рядків
        /// </summary>
        private static string ExtractBalancedBrackets(string text, int startIndex)
        {
            if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '[')
                return null;

            int depth = 0;
            bool inString = false;
            char quote = '\0';

            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    if (ch == '\\')
                    {
                        i++; // пропускаємо екранований символ
                        continue;
                    }
                    if (ch == quote)
                        inString = false;
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    inString = true;
                    quote = ch;
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

        /// <summary>
        /// Витягнути news_id з URL контенту
        /// </summary>
        public static string ExtractNewsId(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            var match = Regex.Match(url, @"[?/](\d+)-[^/]*\.html");
            if (match.Success)
                return match.Groups[1].Value;

            return null;
        }

        // ===================== Парсинг результатів пошуку =====================

        /// <summary>Сирий елемент з HTML пошуку, до групування</summary>
        private class RawSearchItem
        {
            public string Title { get; set; }
            public string OriginalTitle { get; set; }
            public string Url { get; set; }
            public string Poster { get; set; }
            public int? Year { get; set; }
            public string NewsId { get; set; }
        }

        private List<RawSearchItem> ParseRawItems(HtmlDocument doc)
        {
            var items = new List<RawSearchItem>();
            var nodes = doc.DocumentNode.SelectNodes("//a[@class='search-result-link']");
            if (nodes == null)
                return items;

            foreach (var node in nodes)
            {
                try
                {
                    string href = node.GetAttributeValue("href", "");
                    if (string.IsNullOrEmpty(href))
                        continue;

                    var imgNode = node.SelectSingleNode(".//img[@class='search-poster']");
                    string poster = imgNode?.GetAttributeValue("src", "") ?? "";

                    var titleNode = node.SelectSingleNode(".//span[@class='searchheading']");
                    string title = CleanText(titleNode?.InnerText);

                    var origTitleNode = node.SelectSingleNode(".//span[@class='search-orig-title']");
                    string origTitle = CleanText(origTitleNode?.InnerText);

                    var infoNode = node.SelectSingleNode(".//div[@class='search-extend-info']");
                    int? year = null;
                    if (infoNode != null)
                    {
                        var yearSpan = infoNode.SelectSingleNode("./span[1]");
                        string yearText = CleanText(yearSpan?.InnerText);
                        if (!string.IsNullOrEmpty(yearText) && int.TryParse(yearText.Trim(), out int parsedYear))
                            year = parsedYear;
                    }

                    // Фільтр: пропускаємо новини/трейлери — без року та без оригінальної назви
                    if (!IsRealContent(title, origTitle, year))
                        continue;

                    string newsId = ExtractNewsId(href);

                    items.Add(new RawSearchItem
                    {
                        Title = title,
                        OriginalTitle = origTitle,
                        Url = NormalizeUrl(href),
                        Poster = NormalizeUrl(poster),
                        Year = year,
                        NewsId = newsId
                    });
                }
                catch
                {
                    continue;
                }
            }

            return items;
        }

        /// <summary>Фільтр: реальний контент (не новина/трейлер)</summary>
        private static bool IsRealContent(string title, string origTitle, int? year)
        {
            // Є рік — контент
            if (year.HasValue)
                return true;

            // Є оригінальна назва — контент
            if (!string.IsNullOrEmpty(origTitle))
                return true;

            // Дуже довга назва без року — скоріше новина
            if (!string.IsNullOrEmpty(title) && title.Length > 50)
                return false;

            return false;
        }

        /// <summary>Групування сирих елементів по назві шоу. Кожна група = один SearchResult зі списком сезонів</summary>
        private List<SearchResult> GroupByShow(List<RawSearchItem> rawItems)
        {
            if (rawItems.Count == 0)
                return new List<SearchResult>();

            var groups = new Dictionary<string, List<RawSearchItem>>();

            foreach (var item in rawItems)
            {
                string cleanTitle = CleanShowTitle(item.Title);
                string key = $"{cleanTitle.ToLowerInvariant()}|{(item.OriginalTitle ?? "").ToLowerInvariant()}";

                if (!groups.ContainsKey(key))
                    groups[key] = new List<RawSearchItem>();

                groups[key].Add(item);
            }

            var results = new List<SearchResult>();

            foreach (var kvp in groups)
            {
                var items = kvp.Value;
                var first = items[0];
                string showTitle = CleanShowTitle(first.Title);

                var sr = new SearchResult
                {
                    Title = showTitle,
                    OriginalTitle = first.OriginalTitle,
                    Poster = first.Poster
                };

                foreach (var item in items)
                {
                    int? seasonNum = ExtractSeasonNumber(item.Title);
                    if (seasonNum.HasValue)
                    {
                        sr.Seasons.Add(new SeasonEntry
                        {
                            SeasonNumber = seasonNum.Value,
                            NewsId = item.NewsId,
                            Url = item.Url,
                            Year = item.Year
                        });
                    }
                    else
                    {
                        // Фільм або контент без сезону
                        sr.Seasons.Add(new SeasonEntry
                        {
                            SeasonNumber = 1,
                            NewsId = item.NewsId,
                            Url = item.Url,
                            Year = item.Year
                        });
                        sr.Year = item.Year;
                    }
                }

                // Сортуємо сезони за номером
                sr.Seasons = sr.Seasons.OrderBy(s => s.SeasonNumber).ToList();

                results.Add(sr);
            }

            return results;
        }

        /// <summary>Витягти чисту назву шоу (без "N сезон" суфіксу)</summary>
        private static string CleanShowTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return title;

            return Regex.Replace(title, @"\s*\d+\s*сезон\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        /// <summary>Витягти номер сезону з назви</summary>
        private static int? ExtractSeasonNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return null;

            var match = Regex.Match(title, @"(\d+)\s*сезон", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                return num;

            return null;
        }

        // ===================== Парсинг плейлиста =====================

        private List<VoiceGroup> ParsePlaylistHtml(string html)
        {
            var voices = new List<VoiceGroup>();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var playerDiv = doc.DocumentNode.SelectSingleNode("//div[@class='playlists-player']");
            if (playerDiv == null)
            {
                return ParseEpisodesFlat(doc.DocumentNode);
            }

            // Парсимо голоси (озвучки) з вкладки playlists-lists
            var voiceItems = playerDiv.SelectNodes(".//div[@class='playlists-lists']//ul/li");
            bool hasVoiceTabs = voiceItems != null && voiceItems.Count > 0;
            if (hasVoiceTabs)
            {
                foreach (var li in voiceItems)
                {
                    string dataId = li.GetAttributeValue("data-id", "");
                    string text = CleanText(li.InnerText);
                    string voiceName = Regex.Replace(text, @"\s*\(\d+[\d,\s-]*\)\s*$", "").Trim();
                    if (string.IsNullOrEmpty(voiceName))
                        voiceName = text;

                    if (!string.IsNullOrEmpty(dataId))
                    {
                        voices.Add(new VoiceGroup
                        {
                            Name = voiceName,
                            DataId = dataId,
                            Episodes = new List<EpisodeItem>()
                        });
                    }
                }
            }

            // Парсимо епізоди з playlists-videos
            var episodeItems = playerDiv.SelectNodes(".//div[@class='playlists-videos']//ul/li[@data-file]");
            if (episodeItems != null)
            {
                foreach (var li in episodeItems)
                {
                    string fileUrl = li.GetAttributeValue("data-file", "");
                    string dataId = li.GetAttributeValue("data-id", "");
                    string voiceAttr = li.GetAttributeValue("data-voice", "");
                    string text = CleanText(li.InnerText);

                    VoiceGroup targetVoice = null;

                    if (!hasVoiceTabs)
                    {
                        // Фільм: вкладок голосів нема — кожен li це окремий стрім (версія)
                        // Групуємо за data-voice або створюємо нову групу
                        string groupName = !string.IsNullOrEmpty(voiceAttr) ? voiceAttr : text;
                        targetVoice = voices.FirstOrDefault(v =>
                            v.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
                        if (targetVoice == null)
                        {
                            targetVoice = new VoiceGroup
                            {
                                Name = groupName,
                                DataId = dataId,
                                Episodes = new List<EpisodeItem>()
                            };
                            voices.Add(targetVoice);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(dataId))
                            targetVoice = voices.FirstOrDefault(v => v.DataId == dataId);

                        if (targetVoice == null && !string.IsNullOrEmpty(voiceAttr))
                            targetVoice = voices.FirstOrDefault(v =>
                                v.Name.Equals(voiceAttr, StringComparison.OrdinalIgnoreCase));

                        targetVoice ??= voices.FirstOrDefault();
                    }

                    int? epNum = ExtractEpisodeNumber(text);

                    var episode = new EpisodeItem
                    {
                        Title = string.IsNullOrEmpty(text) ? $"Епізод {epNum ?? 1}" : text,
                        FileUrl = NormalizeUrl(fileUrl),
                        EpisodeNumber = epNum
                    };

                    if (targetVoice != null)
                        targetVoice.Episodes.Add(episode);
                }
            }

            return voices;
        }

        private List<VoiceGroup> ParseEpisodesFlat(HtmlNode scope)
        {
            var voices = new List<VoiceGroup>();
            var items = scope.SelectNodes("//li[@data-file]");
            if (items == null)
                return voices;

            var defaultVoice = new VoiceGroup
            {
                Name = "Озвучення",
                DataId = "0_0",
                Episodes = new List<EpisodeItem>()
            };

            foreach (var li in items)
            {
                string fileUrl = li.GetAttributeValue("data-file", "");
                string text = CleanText(li.InnerText);
                int? epNum = ExtractEpisodeNumber(text);

                defaultVoice.Episodes.Add(new EpisodeItem
                {
                    Title = string.IsNullOrEmpty(text) ? "Фільм" : text,
                    FileUrl = NormalizeUrl(fileUrl),
                    EpisodeNumber = epNum
                });
            }

            if (defaultVoice.Episodes.Count > 0)
                voices.Add(defaultVoice);

            return voices;
        }

        // ===================== Допоміжні методи =====================

        private static string BuildSearchQuery(string title, string original_title, string imdb_id)
        {
            if (!string.IsNullOrEmpty(imdb_id) && imdb_id.StartsWith("tt"))
                return imdb_id;

            if (!string.IsNullOrEmpty(title))
                return title;

            if (!string.IsNullOrEmpty(original_title))
                return original_title;

            return null;
        }

        private string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;

            if (url.StartsWith("//"))
                return $"https:{url}";

            if (url.StartsWith("/"))
                return $"{_init.host}{url}";

            return url;
        }

        private static int? ExtractEpisodeNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return null;

            var match = Regex.Match(title, @"(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
                return value;

            return null;
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return HtmlEntity.DeEntitize(value).Trim();
        }


    }
}
