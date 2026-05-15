using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using LME.UAKino.Models;
using HtmlAgilityPack;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;

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

        public async Task<List<SearchResult>> Search(string title, string original_title, int year, string imdb_id)
        {
            string query = BuildSearchQuery(title, original_title, imdb_id);
            if (string.IsNullOrEmpty(query))
                return null;

            string memKey = $"UAKino:search:{query}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchResult> cached))
                return cached;

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
                    _hybridCache.Set(memKey, results, cacheTime(20));

                return results;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UAKino search error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримати плейлист (озвучки + епізоди) за news_id
        /// </summary>
        public async Task<List<VoiceGroup>> GetPlaylist(string newsId)
        {
            if (string.IsNullOrEmpty(newsId))
                return null;

            string memKey = $"UAKino:playlist:{newsId}";
            if (_hybridCache.TryGetValue(memKey, out List<VoiceGroup> cached))
                return cached;

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

                string json = await HttpGet(url, headers);
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
                    _hybridCache.Set(memKey, voices, cacheTime(30));

                return voices;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UAKino playlist error: {ex.Message}");
                return null;
            }
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
            if (voiceItems != null)
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

                    if (!string.IsNullOrEmpty(dataId))
                        targetVoice = voices.FirstOrDefault(v => v.DataId == dataId);

                    if (targetVoice == null && !string.IsNullOrEmpty(voiceAttr))
                        targetVoice = voices.FirstOrDefault(v =>
                            v.Name.Equals(voiceAttr, StringComparison.OrdinalIgnoreCase));

                    targetVoice ??= voices.FirstOrDefault();

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

        private Task<string> HttpGet(string url, List<HeadersModel> headers)
        {
            if (_httpHydra != null)
                return _httpHydra.Get(url, newheaders: headers);

            return Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());
        }

        public static TimeSpan cacheTime(int multiaccess, OnlinesSettings init = null)
        {
            int ctime = init != null && init.cache_time > 0 ? init.cache_time : multiaccess;
            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }
    }
}
