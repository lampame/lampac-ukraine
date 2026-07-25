using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using LME.Petlura.Models;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;

namespace LME.Petlura
{
    public class PetluraInvoke
    {
        private readonly PetluraSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        private static readonly Regex HdvbEmbedRegex = new Regex(
            @"https?://hdvbua\.pro/embed/(\d+)/([a-z0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex HrefRegex = new Regex(
            @"href\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex ContentPathRegex = new Regex(
            @"^/(?:serialy|series|films|movies|filmy|multfilmy)/\d+[^/]*\.html$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex M3u8UrlRegex = new Regex(
            @"(https?://[^""'\s>]+\.m3u8[^""'\s>]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly string[] NoResultMarkers = {
            "не дав жодних результатів",
            "нічого не знайдено",
            "нічого не знайшли",
            "на жаль",
            "вибачте",
            "по вашому запиту нічого",
            "по вашому запиту не знайдено",
            "пошук не дав",
            "0 результатів",
            "немає результатів",
            "nothing found",
            "no results",
            "not found"
        };

        public PetluraInvoke(PetluraSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        /// <summary>
        /// Знайти hdvbua embed ID через пошук на всіх джерелах.
        /// Повертає числовий ID (напр. "7519") або null.
        /// </summary>
        public async Task<string> ResolveEmbedTail(string imdbId)
        {
            if (string.IsNullOrWhiteSpace(imdbId))
                return null;

            string memKey = $"Petlura:embed:{imdbId}";
            if (_hybridCache.TryGetValue(memKey, out string cached))
                return cached;

            try
            {
                var sources = _init.source_list;
                if (sources == null || sources.Length == 0)
                    sources = new[] { "https://uaserials.fm", "https://uaserials.my" };

                // (numericId, fullTail, source)
                var found = new List<(int id, string tail, string source)>();

                foreach (var source in sources)
                {
                    string tail = await SearchSingleSource(source, imdbId);
                    if (string.IsNullOrEmpty(tail))
                        continue;

                    // tail = "embed/XXXX/XXXXX"
                    var match = Regex.Match(tail, @"embed/(\d+)/", RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int numId))
                        found.Add((numId, tail, source));
                }

                if (found.Count == 0)
                {
                    _onLog?.Invoke($"Petlura: жодне джерело не знайшло embed для {imdbId}");
                    _hybridCache.Set<string>(memKey, null, CacheHelper.CacheTime(5, init: _init));
                    return null;
                }

                // Всі однакові numericId — беремо перший
                var ids = found.Select(f => f.id).Distinct().ToList();
                if (ids.Count == 1)
                {
                    string resultId = found[0].id.ToString();
                    _onLog?.Invoke($"Petlura: знайдено embed /embed/{resultId} для {imdbId}");
                    _hybridCache.Set(memKey, resultId, CacheHelper.CacheTime(30, init: _init));
                    return resultId;
                }

                // Різні numericId — беремо найбільший
                int bestId = ids.Max();
                var best = found.First(f => f.id == bestId);
                _onLog?.Invoke($"Petlura: різні embed ID, вибрано найбільший /embed/{best.id} (джерело: {best.source}) для {imdbId}");
                _hybridCache.Set(memKey, best.id.ToString(), CacheHelper.CacheTime(30, init: _init));
                return best.id.ToString();
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Petlura: помилка резолву embed для {imdbId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Пошук на одному джерелі: DLE search → content page → HDVB embed tail.
        /// </summary>
        private async Task<string> SearchSingleSource(string baseUrl, string imdbId)
        {
            try
            {
                // 1. DLE search
                string searchUrl = $"{baseUrl}/index.php?do=search&subaction=search&story={HttpUtility.UrlEncode(imdbId)}";
                var headers = new List<HeadersModel>
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", baseUrl)
                };

                _onLog?.Invoke($"Petlura: пошук на {baseUrl} для {imdbId}");
                string searchHtml = await HttpHelper.GetAsync(_httpHydra, _init, searchUrl, headers, _proxyManager);
                if (string.IsNullOrWhiteSpace(searchHtml))
                    return null;

                // 2. Перевірка на відсутність результатів
                if (HasNoResults(searchHtml))
                {
                    _onLog?.Invoke($"Petlura: {baseUrl} — результатів не знайдено для {imdbId}");
                    return null;
                }

                // 3. Знайти посилання на контент
                Uri baseUri = new Uri(baseUrl);
                string contentUrl = FindContentLink(searchHtml, baseUri.Host);
                if (string.IsNullOrEmpty(contentUrl))
                {
                    _onLog?.Invoke($"Petlura: {baseUrl} — не знайдено посилання на контент для {imdbId}");
                    return null;
                }

                if (!contentUrl.StartsWith("http"))
                    contentUrl = baseUrl.TrimEnd('/') + "/" + contentUrl.TrimStart('/');

                // 4. Отримати сторінку контенту
                _onLog?.Invoke($"Petlura: сторінка контенту {contentUrl}");
                string pageHtml = await HttpHelper.GetAsync(_httpHydra, _init, contentUrl, headers, _proxyManager);
                if (string.IsNullOrWhiteSpace(pageHtml))
                    return null;

                // 5. Знайти HDVB embed
                string tail = ExtractHdvbTail(pageHtml);
                if (!string.IsNullOrEmpty(tail))
                    _onLog?.Invoke($"Petlura: {baseUrl} -> embed {tail}");

                return tail;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Petlura: помилка {baseUrl}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримати HTML плеєра hdvbua.pro.
        /// </summary>
        private async Task<string> FetchPlayerHtml(string embedId)
        {
            if (string.IsNullOrWhiteSpace(embedId))
                return null;

            string url = $"https://hdvbua.pro/embed/{embedId}";
            var headers = new List<HeadersModel>
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://uaserials.fm/")
            };

            _onLog?.Invoke($"Petlura: запит до плеєра {url}");

            if (_httpHydra != null)
                return await _httpHydra.Get(url, newheaders: headers);

            var proxy = _proxyManager?.Get();
            return await Http.Get(url, headers: headers, proxy: proxy);
        }

        /// <summary>
        /// Розпарсити HTML плеєра як структуру сезонів.
        /// Повертає список сезонів з озвучками та епізодами, або null для фільму.
        /// </summary>
        public async Task<List<HdvbSeason>> ParseSeasons(string embedId)
        {
            if (string.IsNullOrWhiteSpace(embedId))
                return null;

            string memKey = $"Petlura:seasons:{embedId}";
            if (_hybridCache.TryGetValue(memKey, out List<HdvbSeason> cached))
                return cached;

            try
            {
                string html = await FetchPlayerHtml(embedId);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                // Шукаємо file:'[{...}]' — серіал з сезонами й озвучками
                int idx = html.IndexOf("file:'[", StringComparison.Ordinal);
                if (idx < 0)
                    return null;

                int endIdx = html.IndexOf("']", idx + 6);
                if (endIdx < 0)
                    return null;

                string jsonStr = html.Substring(idx + 6, endIdx - idx - 6)
                    .Replace("\\'", "'")
                    .Replace("\\\"", "\"")
                    .Replace("\\/", "/");

                var seasons = JsonSerializer.Deserialize<List<HdvbSeason>>(jsonStr, JsonOptions);
                if (seasons == null || seasons.Count == 0)
                    return null;

                _hybridCache.Set(memKey, seasons, CacheHelper.CacheTime(30, init: _init));
                return seasons;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Petlura: помилка парсингу сезонів {embedId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримати m3u8 URL для фільму.
        /// Шукає file: "https://.../index.m3u8" (подвійні лапки без масиву).
        /// </summary>
        public async Task<string> GetMovieStream(string embedId)
        {
            if (string.IsNullOrWhiteSpace(embedId))
                return null;

            string memKey = $"Petlura:movie:{embedId}";
            if (_hybridCache.TryGetValue(memKey, out string cached))
                return cached;

            try
            {
                string html = await FetchPlayerHtml(embedId);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                // file: "https://.../index.m3u8"
                var match = Regex.Match(html, @"file:\s*""(https?://[^""]+/index\.m3u8)""", RegexOptions.IgnoreCase);
                if (!match.Success)
                    return null;

                string url = match.Groups[1].Value;
                _hybridCache.Set(memKey, url, CacheHelper.CacheTime(30, init: _init));
                return url;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Petlura: помилка отримання фільму {embedId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Перевірка HTML на відсутність результатів пошуку.
        /// </summary>
        private bool HasNoResults(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return true;

            var text = System.Net.WebUtility.HtmlDecode(html).ToLowerInvariant();
            text = Regex.Replace(text, @"\s+", " ");
            return NoResultMarkers.Any(m => text.Contains(m));
        }

        /// <summary>
        /// Знайти посилання на сторінку контенту в результатах пошуку DLE.
        /// </summary>
        private string FindContentLink(string html, string expectedHost)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var hrefMatches = HrefRegex.Matches(html);
            foreach (System.Text.RegularExpressions.Match hrefMatch in hrefMatches)
            {
                if (!hrefMatch.Success)
                    continue;

                string href = hrefMatch.Groups[1].Value.Trim();

                // Перевіряємо чи це посилання на контент
                try
                {
                    var uri = new Uri(new Uri($"https://{expectedHost}/"), href);
                    if (!string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ContentPathRegex.IsMatch(uri.AbsolutePath))
                        return uri.AbsoluteUri;

                    if (LooksLikeContentPath(uri.AbsolutePath))
                        return uri.AbsoluteUri;
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        private bool LooksLikeContentPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                return false;

            var lowered = path.ToLowerInvariant();
            var skipPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "/genre.html", "/collection.html", "/rules.html",
                "/privacy_policy.html", "/partner.html", "/onas.html",
                "/copyrights.html"
            };

            if (skipPages.Contains(lowered))
                return false;

            return Regex.IsMatch(lowered, @"/\d+");
        }

        /// <summary>
        /// Витягти hdvbua.pro embed tail з HTML сторінки контенту.
        /// </summary>
        private string ExtractHdvbTail(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            // Прямий пошук embed URL
            var match = HdvbEmbedRegex.Match(html);
            if (match.Success)
                return $"embed/{match.Groups[1].Value}/{match.Groups[2].Value}";

            // Пошук в iframe src/data-src
            var iframeMatch = Regex.Match(html,
                @"<iframe[^>]+src\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (iframeMatch.Success)
            {
                string src = iframeMatch.Groups[1].Value;
                var innerMatch = HdvbEmbedRegex.Match(System.Net.WebUtility.HtmlDecode(src));
                if (innerMatch.Success)
                    return $"embed/{innerMatch.Groups[1].Value}/{innerMatch.Groups[2].Value}";
            }

            return null;
        }

        /// <summary>
        /// Парсинг субтитрів з формату [lang]url.
        /// </summary>
        public SubtitleInfo ParseSubtitle(string subtitleValue)
        {
            if (string.IsNullOrWhiteSpace(subtitleValue))
                return null;

            var match = Regex.Match(subtitleValue, @"\[([^\]]+)\](https?://[^,]+)");
            if (!match.Success)
                return null;

            return new SubtitleInfo
            {
                Lang = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim()),
                Url = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value.Trim())
            };
        }
    }

    /// <summary>
    /// Модель для субтитрів
    /// </summary>
    public class SubtitleInfo
    {
        public string Lang { get; set; }
        public string Url { get; set; }
    }
}
