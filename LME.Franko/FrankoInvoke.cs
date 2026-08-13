using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using LME.Common.Engine;
using LME.Franko.Models;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Services;
using Shared.Services.Hybrid;

namespace LME.Franko
{
    public class FrankoInvoke
    {
        private const int TimeoutSeconds = 10;
        private const int MaxConsiliumWorkers = 8;

        private static readonly Regex ImdbRegex = new Regex(@"^tt\d{7,10}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PlayerPayloadRegex = new Regex(@"window\.__PLAYER_PAYLOAD__\s*=\s*(\{.*?\});", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex ContentHrefRegex = new Regex(@"href=""(https?://[^""]+\.html)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PicassoIdRegex = new Regex(@"picasso\.uacdn\.online/videos/(\d+)/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FrankoIframeRegex = new Regex(@"franko\.uacdn\.online(/show/[^""'<>\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EpisodeTokenRegex = new Regex(@"s(\d+)e(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FallbackRegex = new Regex(@"fallback=([^&""'\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly FrankoConfig _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        public FrankoInvoke(FrankoConfig init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        /// <summary>
        /// Consilium search: imdb_id → franko payload (кеш 20 хв).
        /// </summary>
        public async Task<FrankoSearchResult> Search(string imdbId)
        {
            if (string.IsNullOrWhiteSpace(imdbId))
                return null;

            imdbId = imdbId.Trim();
            if (!ImdbRegex.IsMatch(imdbId))
            {
                _onLog?.Invoke($"lme_franko search: imdb '{imdbId}' не валідний");
                return null;
            }

            string memKey = $"lme_franko:search:{imdbId}";
            if (_hybridCache.TryGetValue(memKey, out FrankoSearchResult cached))
                return cached;

            var mirrors = _init.mirrors != null && _init.mirrors.Length > 0
                ? _init.mirrors
                : new string[] { "https://uakino.watch", "https://uaserials.live" };

            _onLog?.Invoke($"lme_franko search: imdb={imdbId}, mirrors=[{string.Join(", ", mirrors)}], fhost={_init.fhost}");

            var payload = await ConsiliumSearch(imdbId, mirrors);
            if (payload == null)
            {
                _onLog?.Invoke($"lme_franko search: payload не знайдено для {imdbId}");
                return null;
            }

            var result = new FrankoSearchResult
            {
                Id = payload.id,
                IsSerial = payload.is_serial,
                Payload = payload,
                Title = imdbId
            };

            _hybridCache.Set(memKey, result, CacheHelper.CacheTime(20, init: _init));
            return result;
        }

        /// <summary>
        /// Resolve одного мірора: imdb_id → franko payload.
        /// Всі мірори — через сайти-донори: DLE search → picasso id (fast path) або content page → franko player.
        /// Прямий /show/imdb/{imdb} ендпоінт НЕ використовуємо (ненадійний, чужий контент).
        /// </summary>
        public async Task<FrankoPayload> ResolveMirror(string mirror, string imdbId)
        {
            if (string.IsNullOrWhiteSpace(mirror) || string.IsNullOrWhiteSpace(imdbId))
                return null;

            string family = MirrorFamily(mirror);

            // Резолвимо ТІЛЬКИ через сайти-донори (DLE search → content page → franko player).
            // Прямий /show/imdb/{imdb} ендпоінт ненадійний — повертає чужий контент
            // (приклад: Avatar: Fire and Ash → id=149 замість правильного 11662).
            string html = await MirrorSearch(mirror, imdbId, family == "uaserials" ? "uaserials" : "uakino");
            if (string.IsNullOrEmpty(html))
            {
                _onLog?.Invoke($"lme_franko mirror {mirror}: пошук порожній");
                return null;
            }

            _onLog?.Invoke($"lme_franko mirror {mirror}: search len={html.Length}");

            // Fast path: franko id зашитий у URL постера пошуку.
            int? picassoId = ExtractPicassoId(html);
            if (picassoId.HasValue)
            {
                _onLog?.Invoke($"lme_franko mirror {mirror}: picasso id={picassoId}");
                return await GetPlayerPayload($"{_init.fhost}/show/{picassoId}/");
            }

            string contentHref = ExtractContentHref(html, mirror);
            if (string.IsNullOrEmpty(contentHref))
            {
                _onLog?.Invoke($"lme_franko mirror {mirror}: content href не знайдено");
                return null;
            }

            string page = await FetchHtml(contentHref, $"{mirror.TrimEnd('/')}/");
            if (string.IsNullOrEmpty(page))
                return null;

            string showPath = ExtractFrankoShowPath(page);
            if (string.IsNullOrEmpty(showPath))
            {
                _onLog?.Invoke($"lme_franko mirror {mirror}: franko show path не знайдено (page len={page.Length})");
                return null;
            }

            return await GetPlayerPayload($"{_init.fhost}{showPath}/");
        }

        /// <summary>
        /// Consilium: паралельний резолв усіх мірорів (max 8 workers).
        /// Якщо ≥2 мірорів повернули однаковий franko id — majority wins; інакше перший успішний.
        /// </summary>
        public async Task<FrankoPayload> ConsiliumSearch(string imdbId, string[] mirrors)
        {
            if (mirrors == null || mirrors.Length == 0)
                return null;

            int maxWorkers = Math.Min(mirrors.Length, MaxConsiliumWorkers);
            var semaphore = new SemaphoreSlim(maxWorkers);

            var tasks = mirrors.Select(async mirror =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await ResolveMirror(mirror, imdbId);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            // ponytail: без таймауту polling (на відміну від Python 0.5s) — WhenAny повертається миттєво,
            // а самі HTTP-запити обмежені власним таймаутом 10s.
            var ordered = new List<FrankoPayload>();
            var votes = new Dictionary<int, List<FrankoPayload>>();
            FrankoPayload consensus = null;
            int completed = 0;
            var pending = new HashSet<Task<FrankoPayload>>(tasks);

            while (pending.Count > 0)
            {
                var doneTask = await Task.WhenAny(pending);
                pending.Remove(doneTask);
                completed++;

                FrankoPayload payload = null;
                try
                {
                    payload = await doneTask;
                }
                catch
                {
                    payload = null;
                }

                if (payload != null)
                {
                    _onLog?.Invoke($"lme_franko consilium: знайдено payload id={payload.id}, is_serial={payload.is_serial}, translations={payload.translations?.Count}");
                    ordered.Add(payload);
                    if (payload.id > 0)
                    {
                        if (!votes.TryGetValue(payload.id, out var list))
                        {
                            list = new List<FrankoPayload>();
                            votes[payload.id] = list;
                        }
                        list.Add(payload);

                        // Majority wins: щойно 2 мірори зійшлися на тому самому id.
                        if (list.Count >= 2)
                        {
                            consensus = list[0];
                            break;
                        }
                    }
                }

                // Першого успіху достатньо — не чекаємо повільніші мірори після другої спроби.
                if (ordered.Count > 0 && completed >= 2)
                    break;
            }

            return consensus ?? ordered.FirstOrDefault();
        }

        /// <summary>
        /// GET franko player page → window.__PLAYER_PAYLOAD__ → FrankoPayload.
        /// </summary>
        public async Task<FrankoPayload> GetPlayerPayload(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string html = await FetchHtml(url, null);
            if (string.IsNullOrEmpty(html))
            {
                _onLog?.Invoke($"lme_franko payload: порожня відповідь для {url}");
                return null;
            }

            var match = PlayerPayloadRegex.Match(html);
            if (!match.Success)
            {
                _onLog?.Invoke($"lme_franko payload: regex не знайдено у {url} (html len={html.Length})");
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<FrankoPayload>(match.Groups[1].Value);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"lme_franko GetPlayerPayload parse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// POST {api_host}/api/player/files з JSON body. translation_id опційний — якщо null, ключ опускається.
        /// </summary>
        public async Task<FrankoStreamResponse> GetStreamData(int mediaId, int? translationId, int? season, int? episode)
        {
            var payload = new System.Text.Json.Nodes.JsonObject { ["id"] = mediaId };
            if (translationId.HasValue)
                payload["translation"] = translationId.Value;
            if (season.HasValue)
                payload["season_number"] = season.Value;
            if (episode.HasValue)
                payload["episode_number"] = episode.Value;

            string json = payload.ToJsonString();
            string url = $"{_init.api_host}/api/player/files";

            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent),
                    new HeadersModel("Referer", $"{_init.fhost}/")
                };

                // JSON API: явний Content-Type через StringContent (Http.Post(string) завжди form-urlencoded).
                // cors не застосовуємо — це прямий API-ендпоінт (як у Python-джерелі).
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                string response = await Http.Post(url, content, timeoutSeconds: TimeoutSeconds, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(response))
                    return null;

                return JsonSerializer.Deserialize<FrankoStreamResponse>(response);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"lme_franko GetStreamData error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolve stream з episode token validation (кеш 10 хв).
        /// </summary>
        public async Task<FrankoStream> ResolveStream(int mediaId, int? translationId, int? season, int? episode)
        {
            string cacheKey = $"lme_franko:stream:{mediaId}:{translationId}:{season}:{episode}";
            if (_hybridCache.TryGetValue(cacheKey, out FrankoStream cached))
                return cached;

            var result = await ResolveStreamInner(mediaId, translationId, season, episode);

            // ponytail: негативні результати не кешуємо (Python кешує 30s) — повторний resolve безпечніший за стейл-стан.
            if (result != null)
                _hybridCache.Set(cacheKey, result, CacheHelper.CacheTime(10, init: _init));

            return result;
        }

        /// <summary>
        /// Внутрішній resolve: якщо stream серіалу не відповідає запитаному епізоду
        /// (бекенд тихо повертає епізод 1 для відсутніх) — повторний resolve БЕЗ translation_id.
        /// </summary>
        private async Task<FrankoStream> ResolveStreamInner(int mediaId, int? translationId, int? season, int? episode)
        {
            var streamData = await GetStreamData(mediaId, translationId, season, episode);
            string fileUrl = streamData?.file;
            if (string.IsNullOrEmpty(fileUrl))
                return null;

            var stream = new FrankoStream { Url = fileUrl, Quality = "auto" };

            // Перевірка що стрім справді запитаний епізод.
            if (season.HasValue && episode.HasValue)
            {
                var actual = GetEpisodeToken(fileUrl);
                if (actual.HasValue && actual.Value != (season.Value, episode.Value))
                {
                    var fallback = await GetStreamData(mediaId, null, season, episode);
                    string fallbackUrl = fallback?.file;
                    if (!string.IsNullOrEmpty(fallbackUrl))
                        stream = new FrankoStream { Url = fallbackUrl, Quality = "auto" };
                }
            }

            return stream;
        }

        /// <summary>
        /// Декодує base64 fallback-параметр з URL файлу — реальний hdrezka-стиль stream,
        /// єдиний надійний per-episode сигнал (media_id однаковий для всіх епізодів).
        /// </summary>
        public static string DecodeFallback(string fileUrl)
        {
            var match = FallbackRegex.Match(fileUrl ?? string.Empty);
            if (!match.Success)
                return string.Empty;

            try
            {
                string token = match.Groups[1].Value;
                // base64 може прийти без padding
                token += new string('=', (4 - token.Length % 4) % 4);
                return Encoding.UTF8.GetString(Convert.FromBase64String(token));
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Витягує (season, episode) токен s{N}e{M} з декодованого fallback URL, або null.
        /// </summary>
        public static (int, int)? GetEpisodeToken(string fileUrl)
        {
            string decoded = DecodeFallback(fileUrl);
            if (string.IsNullOrEmpty(decoded))
                return null;

            var match = EpisodeTokenRegex.Match(decoded);
            if (!match.Success)
                return null;

            if (int.TryParse(match.Groups[1].Value, out int s) && int.TryParse(match.Groups[2].Value, out int e))
                return (s, e);

            return null;
        }

        /// <summary>
        /// POST <mirror>/engine/ajax/controller.php?mod=search (form-urlencoded).
        /// Простий Http.Post (як у Python-джерелі) — без hydra, з statusCodeOK:false для діагностики.
        /// </summary>
        private async Task<string> MirrorSearch(string mirror, string imdb, string skin)
        {
            string baseUrl = mirror.TrimEnd('/');
            string url = $"{baseUrl}/engine/ajax/controller.php?mod=search";
            string body = $"query={HttpUtility.UrlEncode(imdb)}&skin={skin}";

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", Http.UserAgent),
                new HeadersModel("Referer", $"{baseUrl}/"),
                new HeadersModel("X-Requested-With", "XMLHttpRequest")
            };

            try
            {
                string content = await Http.Post(
                    _init.cors(url),
                    body,
                    headers: headers,
                    proxy: _proxyManager.Get(),
                    timeoutSeconds: TimeoutSeconds,
                    statusCodeOK: false
                );

                _onLog?.Invoke($"lme_franko mirror search {url} -> len={content?.Length ?? 0}");
                return content;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"lme_franko mirror search error ({mirror}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// GET html з referer (опційно). Простий Http.Get (як у Python) з statusCodeOK:false.
        /// </summary>
        private async Task<string> FetchHtml(string url, string referer)
        {
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", Http.UserAgent)
            };
            if (!string.IsNullOrEmpty(referer))
                headers.Add(new HeadersModel("Referer", referer));

            try
            {
                string content = await Http.Get(
                    _init.cors(url),
                    headers: headers,
                    proxy: _proxyManager.Get(),
                    timeoutSeconds: TimeoutSeconds,
                    statusCodeOK: false
                );

                _onLog?.Invoke($"lme_franko fetch {url} -> len={content?.Length ?? 0}");
                return content;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"lme_franko fetch error ({url}): {ex.Message}");
                return null;
            }
        }

        private static string MirrorFamily(string mirror)
        {
            string host = HostOf(mirror).ToLowerInvariant();
            if (host.Contains("kinokrad"))
                return "kinokrad";
            if (host.Contains("uaserials") || host.Contains("uaserialshd"))
                return "uaserials";
            return "uakino";
        }

        private static string HostOf(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;

            var match = Regex.Match(url, @"https?://([^/]+)");
            return match.Success ? match.Groups[1].Value : url;
        }

        private static string ExtractContentHref(string html, string mirror)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            string host = HostOf(mirror).ToLowerInvariant();
            foreach (Match match in ContentHrefRegex.Matches(html))
            {
                string href = match.Groups[1].Value;
                if (!string.IsNullOrEmpty(host) && href.IndexOf(host, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (href.Contains("?do=search") || href.Contains("mode=advanced"))
                    continue;
                if (href.Contains("/engine/"))
                    continue;
                return href;
            }

            return null;
        }

        private static int? ExtractPicassoId(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var match = PicassoIdRegex.Match(html);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                return id;

            return null;
        }

        private static string ExtractFrankoShowPath(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            foreach (Match match in FrankoIframeRegex.Matches(html))
            {
                string path = match.Groups[1].Value.TrimEnd('/');
                if (!string.IsNullOrEmpty(path))
                    return path;
            }

            return null;
        }
    }
}
