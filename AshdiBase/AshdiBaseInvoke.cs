using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using AshdiBase.Models;

namespace AshdiBase
{
    public class AshdiBaseInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public AshdiBaseInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        string AshdiRequestUrl(string url)
        {
            if (!ApnHelper.IsAshdiUrl(url))
                return url;

            return ApnHelper.WrapUrl(_init, url);
        }

        public async Task<AshdiIframeInfo> GetIframeInfo(string imdb_id, long kinopoisk_id)
        {
            string memKey = $"AshdiBase:iframe:{imdb_id}:{kinopoisk_id}";
            if (_hybridCache.TryGetValue(memKey, out AshdiIframeInfo cached))
                return cached;

            try
            {
                string iframeUrl = null;
                if (!string.IsNullOrEmpty(imdb_id))
                {
                    iframeUrl = await RequestIframeUrl($"https://base.ashdi.vip/api/product/read_api.php?imdb={imdb_id}");
                }

                if (string.IsNullOrEmpty(iframeUrl) && kinopoisk_id > 0)
                {
                    iframeUrl = await RequestIframeUrl($"https://base.ashdi.vip/api/product/read_api.php?kinopoisk={kinopoisk_id}");
                }

                if (string.IsNullOrEmpty(iframeUrl))
                    return null;

                var info = new AshdiIframeInfo
                {
                    Url = iframeUrl,
                    IsSerial = iframeUrl.Contains("ashdi.vip/serial/", StringComparison.OrdinalIgnoreCase)
                };

                _hybridCache.Set(memKey, info, cacheTime(20));
                return info;
            }
            catch (Exception ex)
            {
                _onLog($"AshdiBase GetIframeInfo error: {ex.Message}");
                return null;
            }
        }

        async Task<string> RequestIframeUrl(string apiUrl)
        {
            string requestUrl = apiUrl;
            if (ApnHelper.IsEnabled(_init))
                requestUrl = ApnHelper.WrapUrl(_init, apiUrl);

            _onLog($"AshdiBase: requesting API {requestUrl}");
            string response = await Http.Get(requestUrl, headers: new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://base.ashdi.vip/")
            }, proxy: _proxyManager.Get());

            if (string.IsNullOrEmpty(response))
                return null;

            return ExtractIframeSrc(response);
        }

        string ExtractIframeSrc(string response)
        {
            if (string.IsNullOrEmpty(response))
                return null;

            string decoded = System.Web.HttpUtility.HtmlDecode(response);

            var match = Regex.Match(decoded, @"<iframe[^>]+src=['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(decoded, @"src=['""]([^'""]+)['""]", RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            string src = match.Groups[1].Value?.Trim();
            if (string.IsNullOrEmpty(src))
                return null;

            if (src.StartsWith("//"))
                src = "https:" + src;

            return src;
        }

        public async Task<List<VoiceInfo>> ParseAshdiSerial(string iframeUrl)
        {
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://ashdi.vip/")
            };

            try
            {
                string requestUrl = iframeUrl;
                if (iframeUrl.Contains("ashdi.vip/serial/", StringComparison.OrdinalIgnoreCase))
                {
                    var baseUrlMatch = Regex.Match(iframeUrl, @"(https://ashdi\.vip/serial/\d+)");
                    if (baseUrlMatch.Success)
                        requestUrl = baseUrlMatch.Groups[1].Value;
                }

                string html = await Http.Get(AshdiRequestUrl(requestUrl), headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return new List<VoiceInfo>();

                var match = Regex.Match(html, @"file:'(\[.+?\])'", RegexOptions.Singleline);
                if (!match.Success)
                    return new List<VoiceInfo>();

                string jsonStr = match.Groups[1].Value
                    .Replace("\\'", "'")
                    .Replace("\\\"", "\"");

                var voicesArray = JsonConvert.DeserializeObject<List<JObject>>(jsonStr);
                var voices = new List<VoiceInfo>();
                var voiceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var voiceObj in voicesArray)
                {
                    string voiceName = voiceObj["title"]?.ToString().Trim();
                    if (string.IsNullOrEmpty(voiceName))
                        continue;

                    if (voiceCounts.ContainsKey(voiceName))
                    {
                        voiceCounts[voiceName]++;
                        voiceName = $"{voiceName} {voiceCounts[voiceName]}";
                    }
                    else
                    {
                        voiceCounts[voiceObj["title"]?.ToString().Trim()] = 1;
                    }

                    var voiceInfo = new VoiceInfo
                    {
                        Name = voiceObj["title"]?.ToString().Trim(),
                        PlayerType = "ashdi-serial",
                        DisplayName = voiceName
                    };

                    var seasons = voiceObj["folder"] as JArray;
                    if (seasons != null)
                    {
                        foreach (var seasonObj in seasons)
                        {
                            string seasonTitle = seasonObj["title"]?.ToString();
                            var seasonMatch = Regex.Match(seasonTitle ?? string.Empty, @"Сезон\s+(\d+)", RegexOptions.IgnoreCase);
                            if (!seasonMatch.Success)
                                continue;

                            int seasonNumber = int.Parse(seasonMatch.Groups[1].Value);
                            var episodes = new List<EpisodeInfo>();
                            var episodesArray = seasonObj["folder"] as JArray;

                            if (episodesArray != null)
                            {
                                int episodeNum = 1;
                                foreach (var epObj in episodesArray)
                                {
                                    episodes.Add(new EpisodeInfo
                                    {
                                        Number = episodeNum++,
                                        Title = epObj["title"]?.ToString(),
                                        File = epObj["file"]?.ToString(),
                                        Id = epObj["id"]?.ToString(),
                                        Poster = epObj["poster"]?.ToString(),
                                        Subtitle = epObj["subtitle"]?.ToString()
                                    });
                                }
                            }

                            voiceInfo.Seasons[seasonNumber] = episodes;
                        }
                    }

                    voices.Add(voiceInfo);
                }

                return voices;
            }
            catch (Exception ex)
            {
                _onLog($"AshdiBase ParseAshdiSerial error: {ex.Message}");
                return new List<VoiceInfo>();
            }
        }

        public async Task<List<(string link, string quality)>> ParseAshdiSources(string iframeUrl)
        {
            var result = new List<(string link, string quality)>();
            string html = await Http.Get(AshdiRequestUrl(iframeUrl), headers: new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://ashdi.vip/")
            }, proxy: _proxyManager.Get());

            if (string.IsNullOrEmpty(html))
                return result;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sourceNodes = doc.DocumentNode.SelectNodes("//source[contains(@src, '.m3u8')]");
            if (sourceNodes != null)
            {
                foreach (var node in sourceNodes)
                {
                    result.Add((node.GetAttributeValue("src", null), node.GetAttributeValue("label", null) ?? node.GetAttributeValue("res", null) ?? "1080p"));
                }
            }

            if (result.Count > 0)
                return result;

            var match = Regex.Match(html, @"file\s*:\s*['""]([^'""]+\.m3u8[^'""]*)['""]", RegexOptions.IgnoreCase);
            if (match.Success)
                result.Add((match.Groups[1].Value, "1080p"));

            return result;
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
