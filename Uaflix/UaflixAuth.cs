using Shared.Engine;
using Shared.Models;
using Uaflix.Models;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Uaflix
{
    public sealed class UaflixAuth
    {
        private static readonly ConcurrentDictionary<string, CookieContainer> CookieContainers = new();
        private static readonly ConcurrentDictionary<string, string> CookieHeaders = new();

        private readonly UaflixSettings _init;
        private readonly IMemoryCache _memoryCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public UaflixAuth(UaflixSettings init, IMemoryCache memoryCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _memoryCache = memoryCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public bool CanUseCredentials => !string.IsNullOrWhiteSpace(_init?.login) && !string.IsNullOrWhiteSpace(_init?.passwd);

        public async ValueTask<string> GetCookieHeaderAsync(bool forceRefresh = false)
        {
            if (_init == null || string.IsNullOrWhiteSpace(_init.host))
                return null;

            Uri hostUri;
            try
            {
                hostUri = new Uri(EnsureTrailingSlash(_init.host));
            }
            catch
            {
                _onLog("UaflixAuth: некоректний host у конфігурації");
                return null;
            }

            string key = BuildAuthKey();

            if (forceRefresh)
            {
                CookieHeaders.TryRemove(key, out _);
                CookieContainers.TryRemove(key, out _);
            }

            if (CookieHeaders.TryGetValue(key, out string cachedCookie) && !string.IsNullOrWhiteSpace(cachedCookie))
                return cachedCookie;

            if (CookieContainers.TryGetValue(key, out CookieContainer cachedContainer))
            {
                string cookieFromContainer = BuildCookieHeader(cachedContainer, hostUri);
                if (!string.IsNullOrWhiteSpace(cookieFromContainer))
                {
                    CookieHeaders[key] = cookieFromContainer;
                    return cookieFromContainer;
                }
            }

            if (!string.IsNullOrWhiteSpace(_init.cookie))
            {
                string normalized = NormalizeCookie(_init.cookie);
                if (string.IsNullOrWhiteSpace(normalized))
                    return null;

                var manualContainer = CreateContainerFromCookie(normalized);
                CacheAuthState(key, normalized, manualContainer);
                return normalized;
            }

            if (!CanUseCredentials)
                return null;

            string loginThrottleKey = $"uaflix:login:{_init.host}:{_init.login}";
            if (!forceRefresh && _memoryCache.TryGetValue(loginThrottleKey, out _))
                return null;

            _memoryCache.Set(loginThrottleKey, 0, TimeSpan.FromSeconds(20));

            var authResult = await LoginByCredentials();
            if (!authResult.success || string.IsNullOrWhiteSpace(authResult.cookie))
                return null;

            CacheAuthState(key, authResult.cookie, authResult.container);
            return authResult.cookie;
        }

        public void ApplyCookieHeader(List<HeadersModel> headers, string cookie)
        {
            if (headers == null || string.IsNullOrWhiteSpace(cookie))
                return;

            headers.RemoveAll(h => h.name.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
            headers.Add(new HeadersModel("Cookie", cookie));
        }

        private async Task<(bool success, string cookie, CookieContainer container)> LoginByCredentials()
        {
            try
            {
                string host = EnsureTrailingSlash(_init.host);
                var hostUri = new Uri(host);
                var container = new CookieContainer();

                var headers = new List<HeadersModel>
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                    new HeadersModel("Referer", host),
                    new HeadersModel("Origin", _init.host),
                    new HeadersModel("Accept-Language", "uk-UA,uk;q=0.9")
                };

                var postParams = new Dictionary<string, string>
                {
                    ["login_name"] = _init.login,
                    ["login_password"] = _init.passwd,
                    ["login"] = "submit",
                    ["login_not_save"] = "1"
                };

                using var postData = new FormUrlEncodedContent(postParams);
                var response = await Http.BasePost(host, postData,
                    timeoutSeconds: 20,
                    headers: headers,
                    proxy: _proxyManager?.Get(),
                    cookieContainer: container,
                    statusCodeOK: false);

                if (response.response == null)
                {
                    _onLog("UaflixAuth: логін не вдався, немає HTTP-відповіді");
                    return default;
                }

                string body = response.content ?? string.Empty;
                bool hasAuthError = body.Contains("Помилка авторизації", StringComparison.OrdinalIgnoreCase)
                    || body.Contains("Вхід на сайт не був проведений", StringComparison.OrdinalIgnoreCase);

                string cookie = BuildCookieHeader(container, hostUri) ?? string.Empty;
                bool hasSession = cookie.Contains("PHPSESSID=", StringComparison.OrdinalIgnoreCase);
                bool hasDleAuthCookie = cookie.Contains("dle_newpm=", StringComparison.OrdinalIgnoreCase)
                    || cookie.Contains("dle_user_id=", StringComparison.OrdinalIgnoreCase)
                    || cookie.Contains("dle_password=", StringComparison.OrdinalIgnoreCase)
                    || cookie.Contains("dle_hash=", StringComparison.OrdinalIgnoreCase);

                if (response.response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> setCookies))
                {
                    foreach (string line in setCookies)
                    {
                        if (string.IsNullOrWhiteSpace(line) || IsDeletedCookie(line))
                            continue;

                        TrySetCookie(container, hostUri, line);
                    }

                    cookie = BuildCookieHeader(container, hostUri) ?? string.Empty;
                    hasSession = cookie.Contains("PHPSESSID=", StringComparison.OrdinalIgnoreCase);
                    hasDleAuthCookie = cookie.Contains("dle_newpm=", StringComparison.OrdinalIgnoreCase)
                        || cookie.Contains("dle_user_id=", StringComparison.OrdinalIgnoreCase)
                        || cookie.Contains("dle_password=", StringComparison.OrdinalIgnoreCase)
                        || cookie.Contains("dle_hash=", StringComparison.OrdinalIgnoreCase);
                }

                if (hasAuthError || !hasSession || !hasDleAuthCookie)
                {
                    _onLog($"UaflixAuth: авторизація неуспішна, status={(int)response.response.StatusCode}");
                    return default;
                }

                _onLog("UaflixAuth: авторизація успішна");
                return (true, cookie, container);
            }
            catch (Exception ex)
            {
                _onLog($"UaflixAuth: помилка авторизації - {ex.Message}");
                return default;
            }
        }

        private string BuildAuthKey()
        {
            string login = _init.login ?? string.Empty;
            string manualCookie = _init.cookie ?? string.Empty;
            return $"{_init.host}|{login}|{manualCookie}";
        }

        private void CacheAuthState(string key, string cookie, CookieContainer container)
        {
            if (!string.IsNullOrWhiteSpace(cookie))
                CookieHeaders[key] = cookie;

            if (container != null)
                CookieContainers[key] = container;
        }

        private CookieContainer CreateContainerFromCookie(string cookie)
        {
            var container = new CookieContainer();

            if (string.IsNullOrWhiteSpace(cookie))
                return container;

            Uri hostUri = new Uri(EnsureTrailingSlash(_init.host));
            foreach (string part in cookie.Split(';'))
            {
                string row = part.Trim();
                if (string.IsNullOrWhiteSpace(row) || !row.Contains('='))
                    continue;

                string name = row[..row.IndexOf('=')].Trim();
                string value = row[(row.IndexOf('=') + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                TryAddCookie(container, hostUri.Host, name, value);
            }

            return container;
        }

        private static void TrySetCookie(CookieContainer container, Uri uri, string setCookieLine)
        {
            try
            {
                container.SetCookies(uri, setCookieLine);
            }
            catch
            {
                string raw = setCookieLine.Split(';')[0].Trim();
                int eq = raw.IndexOf('=');
                if (eq <= 0)
                    return;

                string name = raw[..eq].Trim();
                string value = raw[(eq + 1)..].Trim();
                TryAddCookie(container, uri.Host, name, value);
            }
        }

        private static void TryAddCookie(CookieContainer container, string host, string name, string value)
        {
            try
            {
                var cookie = new Cookie(name, value, "/", host)
                {
                    HttpOnly = true,
                    Expires = name.Equals("PHPSESSID", StringComparison.OrdinalIgnoreCase)
                        ? default
                        : DateTime.UtcNow.AddMonths(6)
                };

                container.Add(cookie);
            }
            catch
            {
            }
        }

        private static string BuildCookieHeader(CookieContainer container, Uri hostUri)
        {
            if (container == null)
                return null;

            var cookies = container.GetCookies(hostUri)
                .Cast<Cookie>()
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Value))
                .Select(c => $"{c.Name}={c.Value}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return cookies.Count == 0 ? null : string.Join("; ", cookies);
        }

        private static bool IsDeletedCookie(string line)
        {
            return line.Contains("=deleted;", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase)
                || line.Contains("expires=Thu, 01-Jan-1970", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCookie(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie))
                return null;

            var pairs = new List<string>();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string part in cookie.Split(';'))
            {
                string row = part.Trim();
                if (string.IsNullOrWhiteSpace(row) || !row.Contains('='))
                    continue;

                int eq = row.IndexOf('=');
                if (eq <= 0)
                    continue;

                string name = row[..eq].Trim();
                string value = row[(eq + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                map[name] = value;
            }

            foreach (var kv in map)
                pairs.Add($"{kv.Key}={kv.Value}");

            return pairs.Count == 0 ? null : string.Join("; ", pairs);
        }

        private static string EnsureTrailingSlash(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            return url.EndsWith('/') ? url : $"{url}/";
        }
    }
}
