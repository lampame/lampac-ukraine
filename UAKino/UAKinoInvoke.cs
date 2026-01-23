using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using UAKino.Models;

namespace UAKino
{
    public class UAKinoInvoke
    {
        private const string PlaylistPath = "/engine/ajax/playlists.php";
        private const string PlaylistField = "playlist";
        private const string BlacklistRegex = "(/news/)|(/franchise/)";
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public UAKinoInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<List<SearchResult>> Search(string title, string original_title, int serial)
        {
            var queries = new List<string>();
            if (!string.IsNullOrEmpty(title))
                queries.Add(title);
            if (!string.IsNullOrEmpty(original_title) && !queries.Contains(original_title))
                queries.Add(original_title);

            if (queries.Count == 0)
                return null;

            string memKey = $"UAKino:search:{string.Join("|", queries)}:{serial}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchResult> cached))
                return cached;

            var results = new List<SearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var query in queries)
            {
                try
                {
                    string searchUrl = $"{_init.host}/index.php?do=search&subaction=search&story={HttpUtility.UrlEncode(query)}";
                    var headers = new List<HeadersModel>()
                    {
                        new HeadersModel("User-Agent", "Mozilla/5.0"),
                        new HeadersModel("Referer", _init.host)
                    };

                    _onLog?.Invoke($"UAKino search: {searchUrl}");
                    string html = await GetString(searchUrl, headers);
                    if (string.IsNullOrEmpty(html))
                        continue;

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'movie-item') and contains(@class,'short-item')]");
                    if (nodes == null)
                        continue;

                    foreach (var node in nodes)
                    {
                        var titleNode = node.SelectSingleNode(".//a[contains(@class,'movie-title')] | .//a[contains(@class,'full-movie')]");
                        string itemTitle = CleanText(titleNode?.InnerText);
                        string href = NormalizeUrl(titleNode?.GetAttributeValue("href", ""));
                        if (string.IsNullOrEmpty(itemTitle))
                        {
                            var altTitle = node.SelectSingleNode(".//div[contains(@class,'full-movie-title')]");
                            itemTitle = CleanText(altTitle?.InnerText);
                        }

                        if (string.IsNullOrEmpty(itemTitle) || string.IsNullOrEmpty(href) || IsBlacklisted(href))
                            continue;

                        if (serial == 1 && !IsSeriesUrl(href))
                            continue;
                        if (serial == 0 && !IsMovieUrl(href))
                            continue;

                        string seasonText = CleanText(node.SelectSingleNode(".//div[contains(@class,'full-season')]")?.InnerText);
                        if (!string.IsNullOrEmpty(seasonText) && !itemTitle.Contains(seasonText, StringComparison.OrdinalIgnoreCase))
                            itemTitle = $"{itemTitle} ({seasonText})";

                        string poster = ExtractPoster(node);

                        if (seen.Contains(href))
                            continue;
                        seen.Add(href);

                        results.Add(new SearchResult
                        {
                            Title = itemTitle,
                            Url = href,
                            Poster = poster,
                            Season = seasonText
                        });
                    }

                    if (results.Count > 0)
                        break;
                }
                catch (Exception ex)
                {
                    _onLog?.Invoke($"UAKino search error: {ex.Message}");
                }
            }

            if (results.Count > 0)
                _hybridCache.Set(memKey, results, cacheTime(20, init: _init));

            return results;
        }

        public async Task<List<PlaylistItem>> GetPlaylist(string href)
        {
            string newsId = ExtractNewsId(href);
            if (string.IsNullOrEmpty(newsId))
                return null;

            string memKey = $"UAKino:playlist:{newsId}";
            if (_hybridCache.TryGetValue(memKey, out List<PlaylistItem> cached))
                return cached;

            string url = BuildPlaylistUrl(newsId);
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", href ?? _init.host),
                new HeadersModel("X-Requested-With", "XMLHttpRequest")
            };

            try
            {
                _onLog?.Invoke($"UAKino playlist: {url}");
                string payload = await GetString(url, headers);
                if (string.IsNullOrEmpty(payload))
                    return null;

                using var document = JsonDocument.Parse(payload);
                if (!document.RootElement.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
                    return null;

                if (!document.RootElement.TryGetProperty("response", out var responseProp))
                    return null;

                string html = responseProp.GetString();
                if (string.IsNullOrEmpty(html))
                    return null;

                var items = ParsePlaylistHtml(html);
                if (items.Count > 0)
                    _hybridCache.Set(memKey, items, cacheTime(10, init: _init));

                return items;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UAKino playlist error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetPlayerUrl(string href)
        {
            if (string.IsNullOrEmpty(href))
                return null;

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host)
            };

            try
            {
                _onLog?.Invoke($"UAKino movie page: {href}");
                string html = await GetString(href, headers);
                if (string.IsNullOrEmpty(html))
                    return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var playlistNode = doc.DocumentNode.SelectSingleNode($"//div[contains(@class,'playlists-ajax') and @data-xfname='{PlaylistField}']");
                if (playlistNode != null)
                    return null;

                var iframe = doc.DocumentNode.SelectSingleNode("//iframe[@id='pre' and not(ancestor::*[@id='overroll'])]") ??
                             doc.DocumentNode.SelectSingleNode("//iframe[@id='pre']");
                if (iframe == null)
                    return null;

                string src = iframe.GetAttributeValue("src", "");
                if (string.IsNullOrEmpty(src))
                    src = iframe.GetAttributeValue("data-src", "");

                if (src.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    src.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                    return null;

                return NormalizeUrl(src);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UAKino player url error: {ex.Message}");
                return null;
            }
        }

        public async Task<PlayerResult> ParsePlayer(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (LooksLikeDirectStream(url))
            {
                return new PlayerResult { File = url };
            }

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host)
            };

            try
            {
                _onLog?.Invoke($"UAKino parse player: {url}");
                string html = await GetString(url, headers);
                if (string.IsNullOrEmpty(html))
                    return null;

                string file = ExtractPlayerFile(html);
                if (string.IsNullOrEmpty(file))
                    return null;

                return new PlayerResult
                {
                    File = NormalizeUrl(file),
                    Subtitles = ExtractSubtitles(html)
                };
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UAKino parse player error: {ex.Message}");
                return null;
            }
        }

        private async Task<string> GetString(string url, List<HeadersModel> headers, int timeoutSeconds = 15)
        {
            string requestUrl = ApnHelper.IsAshdiUrl(url) && ApnHelper.IsEnabled(_init)
                ? ApnHelper.WrapUrl(_init, url)
                : url;

            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }
            };

            var proxy = _proxyManager.Get();
            if (proxy != null)
            {
                handler.UseProxy = true;
                handler.Proxy = proxy;
            }
            else
            {
                handler.UseProxy = false;
            }

            using var client = new HttpClient(handler);
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            if (headers != null)
            {
                foreach (var h in headers)
                    req.Headers.TryAddWithoutValidation(h.name, h.val);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
            using var response = await client.SendAsync(req, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }

        private List<PlaylistItem> ParsePlaylistHtml(string html)
        {
            var items = new List<PlaylistItem>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes("//li[@data-file]");
            if (nodes == null)
                return items;

            foreach (var node in nodes)
            {
                string dataFile = node.GetAttributeValue("data-file", "");
                if (string.IsNullOrEmpty(dataFile))
                    continue;

                string title = CleanText(node.InnerText);
                string voice = node.GetAttributeValue("data-voice", "");

                items.Add(new PlaylistItem
                {
                    Title = string.IsNullOrEmpty(title) ? "Episode" : title,
                    Url = NormalizeUrl(dataFile),
                    Voice = voice
                });
            }

            return items;
        }

        private string BuildPlaylistUrl(string newsId)
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return $"{_init.host}{PlaylistPath}?news_id={newsId}&xfield={PlaylistField}&time={ts}";
        }

        private static string ExtractNewsId(string href)
        {
            if (string.IsNullOrEmpty(href))
                return null;

            string tail = href.TrimEnd('/').Split('/').LastOrDefault();
            if (string.IsNullOrEmpty(tail))
                return null;

            string newsId = tail.Split('-')[0];
            return string.IsNullOrEmpty(newsId) ? null : newsId;
        }

        private static string ExtractPlayerFile(string html)
        {
            var match = Regex.Match(html, "file\\s*:\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string value = match.Groups[1].Value.Trim();
                if (!value.StartsWith("[", StringComparison.Ordinal))
                    return value;
            }

            var sourceMatch = Regex.Match(html, "<source[^>]+src=['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
            if (sourceMatch.Success)
                return sourceMatch.Groups[1].Value;

            var m3u8Match = Regex.Match(html, "(https?://[^\"'\\s>]+\\.m3u8[^\"'\\s>]*)", RegexOptions.IgnoreCase);
            if (m3u8Match.Success)
                return m3u8Match.Groups[1].Value;

            return null;
        }

        private List<SubtitleInfo> ExtractSubtitles(string html)
        {
            var subtitles = new List<SubtitleInfo>();
            var match = Regex.Match(html, "subtitle\\s*:\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
            if (!match.Success)
                return subtitles;

            string value = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(value))
                return subtitles;

            if (value.StartsWith("[", StringComparison.Ordinal) && value.Contains(']'))
            {
                int endIdx = value.LastIndexOf(']');
                string label = value.Substring(1, endIdx - 1).Trim();
                string url = value[(endIdx + 1)..].Trim();
                url = NormalizeUrl(url);
                if (!string.IsNullOrEmpty(url))
                    subtitles.Add(new SubtitleInfo { Lang = string.IsNullOrEmpty(label) ? "unknown" : label, Url = url });
            }
            else if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                subtitles.Add(new SubtitleInfo { Lang = "unknown", Url = value });
            }

            return subtitles;
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

        private static bool LooksLikeDirectStream(string url)
        {
            return url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBlacklisted(string url)
        {
            return Regex.IsMatch(url ?? string.Empty, BlacklistRegex, RegexOptions.IgnoreCase);
        }

        private static bool IsSeriesUrl(string url)
        {
            return url.Contains("/seriesss/") || url.Contains("/anime-series/") || url.Contains("/cartoonseries/");
        }

        private static bool IsMovieUrl(string url)
        {
            return url.Contains("/filmy/") || url.Contains("/anime-solo/") || url.Contains("/features/");
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

        private static string CleanText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return HtmlEntity.DeEntitize(value).Trim();
        }

        private static int? ExtractEpisodeNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return null;

            var match = Regex.Match(title, @"(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
                return number;

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

        public static int? TryParseEpisodeNumber(string title)
        {
            return ExtractEpisodeNumber(title);
        }
    }
}
