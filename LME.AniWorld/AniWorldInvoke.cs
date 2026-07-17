using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using LME.AniWorld.Models;
using HtmlAgilityPack;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using System.Text.Json;

namespace LME.AniWorld
{
    public class AniWorldInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        public AniWorldInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        /// <summary>
        /// Пошук аніме за original_title з фільтрацією по року та типу медіа
        /// </summary>
        public async Task<List<AniWorldSearchResult>> Search(string original_title, int year, int serial)
        {
            if (string.IsNullOrEmpty(original_title))
                return null;

            string memKey = $"AniWorld:search:{original_title}:{year}";
            if (_hybridCache.TryGetValue(memKey, out List<AniWorldSearchResult> cached))
                return cached;

            try
            {
                string searchUrl = $"{_init.host}/api/v1/catalog/list/?limit=12&offset=0&search={HttpUtility.UrlEncode(original_title)}";

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog?.Invoke($"AniWorld search: {searchUrl}");
                string json = await HttpHelper.GetAsync(_httpHydra, _init, searchUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(json))
                    return null;

                var response = JsonSerializer.Deserialize<CatalogListResponse>(json);
                if (response?.Results == null || response.Results.Count == 0)
                    return null;

                // Фільтрація за original_title та media_type
                // Рік НЕ фільтрується — багатосезонні тайтли мають різні роки для кожного сезону
                var results = new List<AniWorldSearchResult>();
                foreach (var item in response.Results)
                {
                    // Перевірка original_title (точне збігання)
                    bool titleMatch = !string.IsNullOrEmpty(item.OriginalTitle) &&
                                     item.OriginalTitle.Equals(original_title, StringComparison.OrdinalIgnoreCase);

                    // Для серіалів: ONA, TVA або SPECIAL
                    // Для фільмів: MOVIE або будь-який інший тип
                    bool typeMatch = serial == 1
                        ? (item.MediaType == "ONA" || item.MediaType == "TVA" || item.MediaType == "SPECIAL")
                        : true;

                    if (titleMatch && typeMatch)
                    {
                        results.Add(new AniWorldSearchResult
                        {
                            Id = item.Id,
                            Title = item.Title,
                            OriginalTitle = item.OriginalTitle,
                            ReleaseYear = item.ReleaseYear,
                            MediaType = item.MediaType
                        });
                    }
                }

                if (results.Count > 0)
                    _hybridCache.Set(memKey, results, CacheHelper.CacheTime(20, init: _init));

                return results;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AniWorld search error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримання деталей аніме (список епізодів)
        /// </summary>
        public async Task<CatalogDetail> GetDetail(int catalogId)
        {
            if (catalogId <= 0)
                return null;

            string memKey = $"AniWorld:detail:{catalogId}";
            if (_hybridCache.TryGetValue(memKey, out CatalogDetail cached))
                return cached;

            try
            {
                string detailUrl = $"{_init.host}/api/v1/catalog/detail/{catalogId}/?source_type=DEFAULT";

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog?.Invoke($"AniWorld detail: {detailUrl}");
                string json = await HttpHelper.GetAsync(_httpHydra, _init, detailUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(json))
                    return null;

                var detail = JsonSerializer.Deserialize<CatalogDetail>(json);
                if (detail == null)
                    return null;

                _hybridCache.Set(memKey, detail, CacheHelper.CacheTime(30, init: _init));
                return detail;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AniWorld detail error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримання source_url для епізоду (per-episode резолв)
        /// </summary>
        public async Task<EpisodeSource> GetEpisodeSource(int episodeId)
        {
            if (episodeId <= 0)
                return null;

            string memKey = $"AniWorld:source:{episodeId}";
            if (_hybridCache.TryGetValue(memKey, out EpisodeSource cached))
                return cached;

            try
            {
                string sourceUrl = $"{_init.host}/api/v1/catalog/episode/{episodeId}/";

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog?.Invoke($"AniWorld episode source: {sourceUrl}");
                string json = await HttpHelper.GetAsync(_httpHydra, _init, sourceUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(json))
                    return null;

                var response = JsonSerializer.Deserialize<EpisodeSourceResponse>(json);
                if (response == null || string.IsNullOrEmpty(response.SourceUrl))
                    return null;

                // Визначення типу стріму
                var streamType = StreamType.Unknown;
                if (response.SourceUrl.Contains("dailymotion.com", StringComparison.OrdinalIgnoreCase))
                    streamType = StreamType.Dailymotion;
                else if (response.SourceUrl.Contains("mediadelivery.net", StringComparison.OrdinalIgnoreCase))
                    streamType = StreamType.Mediadelivery;

                var result = new EpisodeSource
                {
                    Url = response.SourceUrl,
                    Type = streamType
                };

                _hybridCache.Set(memKey, result, CacheHelper.CacheTime(10, init: _init));
                return result;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AniWorld episode source error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримання video ID з Dailymotion URL
        /// </summary>
        public static string ExtractDailymotionVideoId(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // Формати URL:
            // https://www.dailymotion.com/video/k2gLPRjxWV04mHHKtiC
            // https://geo.dailymotion.com/video/k2gLPRjxWV04mHHKtiC
            var match = Regex.Match(url, @"dailymotion\.com/video/([a-zA-Z0-9]+)");
            if (match.Success)
                return match.Groups[1].Value;

            return null;
        }

        /// <summary>
        /// Отримання якостей стріму з Dailymotion metadata API
        /// </summary>
        public async Task<List<(string quality, string url)>> GetDailymotionQualities(string videoId)
        {
            if (string.IsNullOrEmpty(videoId))
                return null;

            string memKey = $"AniWorld:dm:{videoId}";
            if (_hybridCache.TryGetValue(memKey, out List<(string, string)> cached))
                return cached;

            try
            {
                string metadataUrl = $"https://www.dailymotion.com/player/metadata/video/{videoId}";

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", "https://www.dailymotion.com/")
                };

                _onLog?.Invoke($"AniWorld Dailymotion metadata: {metadataUrl}");
                string json = await HttpHelper.GetAsync(_httpHydra, _init, metadataUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(json))
                    return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("qualities", out var qualities))
                    return null;

                if (!qualities.TryGetProperty("auto", out var autoArray) || autoArray.GetArrayLength() == 0)
                    return null;

                string autoUrl = autoArray[0].GetProperty("url").GetString();
                if (string.IsNullOrEmpty(autoUrl))
                    return null;

                // Парсинг M3U8 для отримання всіх якостей
                var qualitiesList = await ParseM3U8Qualities(autoUrl);
                if (qualitiesList == null || qualitiesList.Count == 0)
                {
                    // Fallback: повертаємо auto quality
                    qualitiesList = new List<(string, string)> { ("auto", autoUrl) };
                }

                _hybridCache.Set(memKey, qualitiesList, CacheHelper.CacheTime(5, init: _init));
                return qualitiesList;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AniWorld Dailymotion metadata error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Парсинг M3U8 маніфесту для отримання якостей
        /// </summary>
        private async Task<List<(string quality, string url)>> ParseM3U8Qualities(string m3u8Url)
        {
            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0")
                };

                _onLog?.Invoke($"AniWorld M3U8 fetch: {m3u8Url}");
                string content = await HttpHelper.GetAsync(_httpHydra, _init, m3u8Url, headers, _proxyManager);
                if (string.IsNullOrEmpty(content))
                    return null;

                var qualities = new List<(string quality, string url)>();
                var lines = content.Split('\n');

                for (int i = 0; i < lines.Length - 1; i++)
                {
                    if (lines[i].StartsWith("#EXT-X-STREAM-INF:"))
                    {
                        string quality = ExtractQualityFromStreamInf(lines[i]);
                        string streamUrl = lines[i + 1].Trim();

                        if (!string.IsNullOrEmpty(quality) && !string.IsNullOrEmpty(streamUrl))
                        {
                            // Абсолютний URL
                            if (!streamUrl.StartsWith("http"))
                            {
                                var baseUri = new Uri(m3u8Url);
                                streamUrl = new Uri(baseUri, streamUrl).ToString();
                            }

                            qualities.Add((quality, streamUrl));
                        }
                    }
                }

                return qualities.Count > 0 ? qualities : null;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AniWorld M3U8 parse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Витягування назви якості з #EXT-X-STREAM-INF
        /// </summary>
        private static string ExtractQualityFromStreamInf(string line)
        {
            // Шукаємо RESOLUTION або NAME
            var resolutionMatch = Regex.Match(line, @"RESOLUTION=(\d+x\d+)");
            if (resolutionMatch.Success)
            {
                string res = resolutionMatch.Groups[1].Value;
                // Конвертуємо в зрозумілу назву
                return res switch
                {
                    "512x288" => "380",
                    "848x480" => "480",
                    "1280x720" => "720",
                    "1920x1080" => "1080",
                    "2560x1440" => "1440",
                    _ => res
                };
            }

            var nameMatch = Regex.Match(line, @"NAME=""?([^""&,]+)""?");
            if (nameMatch.Success)
                return nameMatch.Groups[1].Value;

            return "auto";
        }

        /// <summary>
        /// Отримання content-src з Mediadelivery embed
        /// </summary>
        public async Task<string> GetMediadeliveryStreamUrl(string embedUrl)
        {
            if (string.IsNullOrEmpty(embedUrl))
                return null;

            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog?.Invoke($"AniWorld Mediadelivery embed: {embedUrl}");
                string html = await HttpHelper.GetAsync(_httpHydra, _init, embedUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(html))
                    return null;

                // Парсинг HTML для пошуку bunny-stream-video
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var bunnyVideo = doc.DocumentNode.SelectSingleNode("//bunny-stream-video[@content-src]");
                if (bunnyVideo == null)
                    return null;

                string contentSrc = bunnyVideo.GetAttributeValue("content-src", "");
                if (string.IsNullOrEmpty(contentSrc))
                    return null;

                _onLog?.Invoke($"AniWorld Mediadelivery stream: {contentSrc}");
                return contentSrc;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AniWorld Mediadelivery error: {ex.Message}");
                return null;
            }
        }

    }
}
