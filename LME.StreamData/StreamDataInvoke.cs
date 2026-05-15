using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LME.StreamData.Models;
using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;

namespace LME.StreamData
{
    public class StreamDataInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        private const string API_BASE = "https://streamdata.vaplayer.ru/api.php";

        public StreamDataInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        /// <summary>
        /// Отримати дані для фільму за TMDB ID
        /// </summary>
        public async Task<StreamDataResponse> GetMovie(long tmdbId)
        {
            string memKey = $"StreamData:movie:{tmdbId}";
            if (_hybridCache.TryGetValue(memKey, out StreamDataResponse cached))
                return cached;

            try
            {
                string url = $"{API_BASE}?tmdb={tmdbId}&type=movie";
                _onLog?.Invoke($"StreamData movie: {url}");

                string json = await ApiGet(url);
                if (string.IsNullOrEmpty(json))
                    return null;

                var response = JsonConvert.DeserializeObject<StreamDataResponse>(json);
                if (response?.status_code != "200" || response?.data?.stream_urls == null || response.data.stream_urls.Count == 0)
                    return null;

                _hybridCache.Set(memKey, response, cacheTime(30, init: _init));
                return response;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"StreamData movie error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримати дані для серіалу (без season/episode - отримуємо eps структуру + S01E01)
        /// </summary>
        public async Task<StreamDataResponse> GetTvSeries(long tmdbId)
        {
            string memKey = $"StreamData:tv:{tmdbId}";
            if (_hybridCache.TryGetValue(memKey, out StreamDataResponse cached))
                return cached;

            try
            {
                string url = $"{API_BASE}?tmdb={tmdbId}&type=tv";
                _onLog?.Invoke($"StreamData tv: {url}");

                string json = await ApiGet(url);
                if (string.IsNullOrEmpty(json))
                    return null;

                var response = JsonConvert.DeserializeObject<StreamDataResponse>(json);
                if (response?.status_code != "200" || response?.data?.eps == null || response.data.eps.Count == 0)
                    return null;

                _hybridCache.Set(memKey, response, cacheTime(30, init: _init));
                return response;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"StreamData tv error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримати стріми для конкретного епізоду
        /// </summary>
        public async Task<StreamDataResponse> GetEpisode(long tmdbId, int season, int episode)
        {
            string memKey = $"StreamData:ep:{tmdbId}:s{season}e{episode}";
            if (_hybridCache.TryGetValue(memKey, out StreamDataResponse cached))
                return cached;

            try
            {
                string url = $"{API_BASE}?tmdb={tmdbId}&type=tv&season={season}&episode={episode}";
                _onLog?.Invoke($"StreamData episode: {url}");

                string json = await ApiGet(url);
                if (string.IsNullOrEmpty(json))
                    return null;

                var response = JsonConvert.DeserializeObject<StreamDataResponse>(json);
                if (response?.status_code != "200" || response?.data?.stream_urls == null || response.data.stream_urls.Count == 0)
                    return null;

                _hybridCache.Set(memKey, response, cacheTime(30, init: _init));
                return response;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"StreamData episode error: {ex.Message}");
                return null;
            }
        }

        private Task<string> ApiGet(string url)
        {
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://brightpathsignals.com/"),
                new HeadersModel("X-Requested-With", "XMLHttpRequest")
            };

            if (_httpHydra != null)
                return _httpHydra.Get(url, newheaders: headers);

            return Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());
        }

        public static TimeSpan cacheTime(int multiaccess, int home = 5, int mikrotik = 2, OnlinesSettings init = null, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);

            int ctime = init != null && init.cache_time > 0 ? init.cache_time : multiaccess;
            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }
    }
}
