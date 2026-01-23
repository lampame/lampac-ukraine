using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Bamboo.Models;
using HtmlAgilityPack;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;

namespace Bamboo
{
    public class BambooInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public BambooInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<List<SearchResult>> Search(string title, string original_title)
        {
            string query = !string.IsNullOrEmpty(title) ? title : original_title;
            if (string.IsNullOrEmpty(query))
                return null;

            string memKey = $"Bamboo:search:{query}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchResult> cached))
                return cached;

            try
            {
                string searchUrl = $"{_init.host}/index.php?do=search&subaction=search&story={HttpUtility.UrlEncode(query)}";

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog?.Invoke($"Bamboo search: {searchUrl}");
                string html = await Http.Get(searchUrl, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var results = new List<SearchResult>();
                var nodes = doc.DocumentNode.SelectNodes("//li[contains(@class,'slide-item')]");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        string itemTitle = CleanText(node.SelectSingleNode(".//h6")?.InnerText);
                        string href = ExtractHref(node);
                        string poster = ExtractPoster(node);

                        if (string.IsNullOrEmpty(itemTitle) || string.IsNullOrEmpty(href))
                            continue;

                        results.Add(new SearchResult
                        {
                            Title = itemTitle,
                            Url = href,
                            Poster = poster
                        });
                    }
                }

                if (results.Count > 0)
                    _hybridCache.Set(memKey, results, cacheTime(20, init: _init));

                return results;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Bamboo search error: {ex.Message}");
                return null;
            }
        }

        public async Task<SeriesEpisodes> GetSeriesEpisodes(string href)
        {
            if (string.IsNullOrEmpty(href))
                return null;

            string memKey = $"Bamboo:series:{href}";
            if (_hybridCache.TryGetValue(memKey, out SeriesEpisodes cached))
                return cached;

            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog?.Invoke($"Bamboo series page: {href}");
                string html = await Http.Get(href, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var result = new SeriesEpisodes();
                bool foundBlocks = false;

                var blocks = doc.DocumentNode.SelectNodes("//div[contains(@class,'mt-4')]");
                if (blocks != null)
                {
                    foreach (var block in blocks)
                    {
                        string header = CleanText(block.SelectSingleNode(".//h3[contains(@class,'my-4')]")?.InnerText);
                        var episodes = ParseEpisodeSpans(block);
                        if (episodes.Count == 0)
                            continue;

                        foundBlocks = true;
                        if (!string.IsNullOrEmpty(header) && header.Contains("Субтитри", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Sub.AddRange(episodes);
                        }
                        else if (!string.IsNullOrEmpty(header) && header.Contains("Озвучення", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Dub.AddRange(episodes);
                        }
                    }
                }

                if (!foundBlocks || (result.Sub.Count == 0 && result.Dub.Count == 0))
                {
                    var fallback = ParseEpisodeSpans(doc.DocumentNode);
                    if (fallback.Count > 0)
                        result.Dub.AddRange(fallback);
                }

                if (result.Sub.Count == 0)
                {
                    var fallback = ParseEpisodeSpans(doc.DocumentNode);
                    if (fallback.Count > 0)
                        result.Sub.AddRange(fallback);
                }

                _hybridCache.Set(memKey, result, cacheTime(30, init: _init));
                return result;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Bamboo series error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<StreamInfo>> GetMovieStreams(string href)
        {
            if (string.IsNullOrEmpty(href))
                return null;

            string memKey = $"Bamboo:movie:{href}";
            if (_hybridCache.TryGetValue(memKey, out List<StreamInfo> cached))
                return cached;

            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog?.Invoke($"Bamboo movie page: {href}");
                string html = await Http.Get(href, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var streams = new List<StreamInfo>();
                var nodes = doc.DocumentNode.SelectNodes("//span[contains(@class,'mr-3') and @data-file]");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        string dataFile = node.GetAttributeValue("data-file", "");
                        if (string.IsNullOrEmpty(dataFile))
                            continue;

                        string title = node.GetAttributeValue("data-title", "");
                        title = string.IsNullOrEmpty(title) ? CleanText(node.InnerText) : title;

                        streams.Add(new StreamInfo
                        {
                            Title = title,
                            Url = NormalizeUrl(dataFile)
                        });
                    }
                }

                if (streams.Count > 0)
                    _hybridCache.Set(memKey, streams, cacheTime(30, init: _init));

                return streams;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Bamboo movie error: {ex.Message}");
                return null;
            }
        }

        private List<EpisodeInfo> ParseEpisodeSpans(HtmlNode scope)
        {
            var episodes = new List<EpisodeInfo>();
            var nodes = scope.SelectNodes(".//span[@data-file]");
            if (nodes == null)
                return episodes;

            foreach (var node in nodes)
            {
                string dataFile = node.GetAttributeValue("data-file", "");
                if (string.IsNullOrEmpty(dataFile))
                    continue;

                string title = node.GetAttributeValue("data-title", "");
                if (string.IsNullOrEmpty(title))
                    title = CleanText(node.InnerText);

                int? episodeNum = ExtractEpisodeNumber(title);

                episodes.Add(new EpisodeInfo
                {
                    Title = string.IsNullOrEmpty(title) ? "Episode" : title,
                    Url = NormalizeUrl(dataFile),
                    Episode = episodeNum
                });
            }

            return episodes;
        }

        private string ExtractHref(HtmlNode node)
        {
            var link = node.SelectSingleNode(".//a[contains(@class,'hover-buttons')]")
                ?? node.SelectSingleNode(".//a[@href]");
            if (link == null)
                return string.Empty;

            string href = link.GetAttributeValue("href", "");
            return NormalizeUrl(href);
        }

        private string ExtractPoster(HtmlNode node)
        {
            var img = node.SelectSingleNode(".//img");
            if (img == null)
                return string.Empty;

            string src = img.GetAttributeValue("src", "");
            if (string.IsNullOrEmpty(src))
                src = img.GetAttributeValue("data-src", "");

            return NormalizeUrl(src);
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

        public static TimeSpan cacheTime(int multiaccess, int home = 5, int mikrotik = 2, OnlinesSettings init = null, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);

            int ctime = AppInit.conf.mikrotik ? mikrotik : AppInit.conf.multiaccess ? init != null && init.cache_time > 0 ? init.cache_time : multiaccess : home;
            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }
    }
}
