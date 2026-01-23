using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Mikai.Models;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;

namespace Mikai
{
    public class MikaiInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public MikaiInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<List<MikaiAnime>> Search(string title, string original_title, int year)
        {
            string memKey = $"Mikai:search:{title}:{original_title}:{year}";
            if (_hybridCache.TryGetValue(memKey, out List<MikaiAnime> cached))
                return cached;

            try
            {
                async Task<List<MikaiAnime>> FindAnime(string query)
                {
                    if (string.IsNullOrWhiteSpace(query))
                        return null;

                    string searchUrl = $"{_init.apihost}/anime/search?page=1&limit=24&sort=year&order=desc&name={HttpUtility.UrlEncode(query)}";
                    var headers = DefaultHeaders();

                    _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {searchUrl}");
                    string json = await Http.Get(searchUrl, headers: headers, proxy: _proxyManager.Get());
                    if (string.IsNullOrEmpty(json))
                        return null;

                    var response = JsonSerializer.Deserialize<SearchResponse>(json);
                    if (response?.Result == null || response.Result.Count == 0)
                        return null;

                    if (year > 0)
                    {
                        var byYear = response.Result.Where(r => r.Year == year).ToList();
                        if (byYear.Count > 0)
                            return byYear;
                    }

                    return response.Result;
                }

                var results = await FindAnime(title) ?? await FindAnime(original_title);
                if (results == null || results.Count == 0)
                    return null;

                _hybridCache.Set(memKey, results, cacheTime(10, init: _init));
                return results;
            }
            catch (Exception ex)
            {
                _onLog($"Mikai Search error: {ex.Message}");
                return null;
            }
        }

        public async Task<MikaiAnime> GetDetails(int id)
        {
            string memKey = $"Mikai:details:{id}";
            if (_hybridCache.TryGetValue(memKey, out MikaiAnime cached))
                return cached;

            try
            {
                string url = $"{_init.apihost}/anime/{id}";
                var headers = DefaultHeaders();

                _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {url}");
                string json = await Http.Get(url, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(json))
                    return null;

                var response = JsonSerializer.Deserialize<DetailResponse>(json);
                if (response?.Result == null)
                    return null;

                _hybridCache.Set(memKey, response.Result, cacheTime(20, init: _init));
                return response.Result;
            }
            catch (Exception ex)
            {
                _onLog($"Mikai Details error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> ResolveVideoUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (url.Contains("moonanime.art", StringComparison.OrdinalIgnoreCase))
                return await ParseMoonAnimePage(url);

            if (url.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase))
                return await ParseAshdiPage(url);

            return url;
        }

        public async Task<string> ParseMoonAnimePage(string url)
        {
            try
            {
                string requestUrl = url;
                if (!requestUrl.Contains("player=", StringComparison.OrdinalIgnoreCase))
                {
                    requestUrl = requestUrl.Contains("?")
                        ? $"{requestUrl}&player=mikai.me"
                        : $"{requestUrl}?player=mikai.me";
                }

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
                string html = await Http.Get(requestUrl, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return null;

                var match = System.Text.RegularExpressions.Regex.Match(html, @"file:\s*""([^""]+\.m3u8)""");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch (Exception ex)
            {
                _onLog($"Mikai ParseMoonAnimePage error: {ex.Message}");
            }

            return null;
        }

        string AshdiRequestUrl(string url)
        {
            if (!ApnHelper.IsAshdiUrl(url))
                return url;

            return ApnHelper.WrapUrl(_init, url);
        }

        public async Task<string> ParseAshdiPage(string url)
        {
            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", "https://ashdi.vip/")
                };

                string requestUrl = AshdiRequestUrl(url);
                _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
                string html = await Http.Get(requestUrl, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return null;

                var match = System.Text.RegularExpressions.Regex.Match(html, @"file\s*:\s*['""]([^'""]+)['""]");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch (Exception ex)
            {
                _onLog($"Mikai ParseAshdiPage error: {ex.Message}");
            }

            return null;
        }

        private List<HeadersModel> DefaultHeaders()
        {
            return new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host),
                new HeadersModel("Accept", "application/json")
            };
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
