using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Uaflix.Controllers;
using Shared.Engine;
using Uaflix.Models;
using System.Linq;
using Shared.Models.Templates;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace Uaflix
{
    public class UaflixInvoke
    {
        private static readonly Regex Quality4kRegex = new Regex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase);
        private static readonly Regex QualityFhdRegex = new Regex(@"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b", RegexOptions.IgnoreCase);

        private readonly UaflixSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly UaflixAuth _auth;

        public UaflixInvoke(UaflixSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, UaflixAuth auth)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _auth = auth;
        }

        string AshdiRequestUrl(string url)
        {
            if (!ApnHelper.IsAshdiUrl(url))
                return url;

            if (!string.IsNullOrWhiteSpace(_init.webcorshost))
                return url;

            return ApnHelper.WrapUrl(_init, url);
        }

        public async Task<bool> CheckSearchAvailability(string title, string originalTitle)
        {
            string filmTitle = !string.IsNullOrWhiteSpace(title) ? title : originalTitle;
            if (string.IsNullOrWhiteSpace(filmTitle))
                return false;

            string searchUrl = $"{_init.host}/index.php?do=search&subaction=search&story={System.Web.HttpUtility.UrlEncode(filmTitle)}";
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host)
            };

            string searchHtml = await GetHtml(searchUrl, headers, timeoutSeconds: 10);
            if (string.IsNullOrWhiteSpace(searchHtml))
                return false;

            return searchHtml.Contains("sres-wrap")
                || searchHtml.Contains("sres-item")
                || searchHtml.Contains("search-results");
        }

        async Task<string> GetHtml(string url, List<HeadersModel> headers, int timeoutSeconds = 15, bool retryOnUnauthorized = true)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string requestUrl = _init.cors(url);
            bool withAuth = ShouldUseAuth(url);
            var requestHeaders = headers != null ? new List<HeadersModel>(headers) : new List<HeadersModel>();

            if (withAuth && _auth != null)
            {
                string cookie = await _auth.GetCookieHeaderAsync();
                _auth.ApplyCookieHeader(requestHeaders, cookie);
            }

            var response = await Http.BaseGet(requestUrl,
                headers: requestHeaders,
                timeoutSeconds: timeoutSeconds,
                proxy: _proxyManager.Get(),
                statusCodeOK: false);

            if (response.response?.StatusCode == HttpStatusCode.Forbidden
                && retryOnUnauthorized
                && withAuth
                && _auth != null
                && _auth.CanUseCredentials)
            {
                _onLog($"UaflixAuth: отримано 403 для {url}, виконую повторну авторизацію");
                string refreshedCookie = await _auth.GetCookieHeaderAsync(forceRefresh: true);
                _auth.ApplyCookieHeader(requestHeaders, refreshedCookie);

                response = await Http.BaseGet(requestUrl,
                    headers: requestHeaders,
                    timeoutSeconds: timeoutSeconds,
                    proxy: _proxyManager.Get(),
                    statusCodeOK: false);
            }

            if (response.response?.StatusCode != HttpStatusCode.OK)
            {
                if (response.response != null)
                    _onLog($"Uaflix HTTP {(int)response.response.StatusCode} для {url}");

                return null;
            }

            return response.content;
        }

        bool ShouldUseAuth(string url)
        {
            if (_auth == null || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(_init?.host))
                return false;

            try
            {
                Uri siteUri = new Uri(_init.host);
                Uri requestUri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out requestUri))
                    requestUri = new Uri(siteUri, url.TrimStart('/'));

                return string.Equals(requestUri.Host, siteUri.Host, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
        
        #region Методи для визначення та парсингу різних типів плеєрів
        
        /// <summary>
        /// Визначити тип плеєра з URL iframe
        /// </summary>
        private string DeterminePlayerType(string iframeUrl)
        {
            if (string.IsNullOrEmpty(iframeUrl))
                return null;

            string normalized = iframeUrl.Trim().ToLowerInvariant();

            // Перевіряємо на підтримувані типи плеєрів
            if (normalized.Contains("ashdi.vip/serial/"))
                return "ashdi-serial";
            else if (normalized.Contains("ashdi.vip/vod/"))
                return "ashdi-vod";
            else if (normalized.Contains("zetvideo.net/serial/"))
                return "zetvideo-serial";
            else if (normalized.Contains("zetvideo.net/vod/"))
                return "zetvideo-vod";

            // Перевіряємо на небажані типи плеєрів (трейлери, реклама тощо)
            if (normalized.Contains("youtube.com/embed/") ||
                normalized.Contains("youtu.be/") ||
                normalized.Contains("vimeo.com/") ||
                normalized.Contains("dailymotion.com/"))
                return "trailer"; // Ігноруємо відеохостинги з трейлерами

            return null;
        }

        private string NormalizeIframeUrl(string iframeUrl)
        {
            if (string.IsNullOrWhiteSpace(iframeUrl))
                return null;

            string url = WebUtility.HtmlDecode(iframeUrl.Trim()).Replace("&amp;", "&");
            if (url.StartsWith("//"))
                url = "https:" + url;

            return url;
        }

        private string ExtractIframeFromMeta(HtmlDocument doc)
        {
            if (doc?.DocumentNode == null)
                return null;

            var meta = doc.DocumentNode.SelectSingleNode("//meta[@property='og:video:iframe']");
            if (meta == null)
                return null;

            string content = meta.GetAttributeValue("content", null);
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var match = Regex.Match(content, "src=['\\\"]([^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            return NormalizeIframeUrl(match.Groups[1].Value);
        }

        private string ExtractIframeUrl(HtmlDocument doc)
        {
            if (doc?.DocumentNode == null)
                return null;

            var iframeNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'video-box')]//iframe")
                ?? doc.DocumentNode.SelectSingleNode("//iframe");

            string iframeUrl = iframeNode?.GetAttributeValue("src", null);
            iframeUrl = NormalizeIframeUrl(iframeUrl);
            if (!string.IsNullOrEmpty(iframeUrl))
                return iframeUrl;

            return ExtractIframeFromMeta(doc);
        }

        private async Task<(string iframeUrl, string playerType)> ProbeEpisodePlayer(string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
                return (null, null);

            string memKey = $"UaFlix:episode-player:{pageUrl}";
            if (_hybridCache.TryGetValue(memKey, out EpisodePlayerInfo cached))
                return (cached?.IframeUrl, cached?.PlayerType);

            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                string html = await GetHtml(pageUrl, headers);
                if (string.IsNullOrWhiteSpace(html))
                    return (null, null);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                string iframeUrl = ExtractIframeUrl(doc);
                string playerType = DeterminePlayerType(iframeUrl);

                _hybridCache.Set(memKey, new EpisodePlayerInfo
                {
                    IframeUrl = iframeUrl,
                    PlayerType = playerType
                }, cacheTime(20));

                return (iframeUrl, playerType);
            }
            catch (Exception ex)
            {
                _onLog($"ProbeEpisodePlayer error ({pageUrl}): {ex.Message}");
                return (null, null);
            }
        }

        private async Task<(string iframeUrl, string playerType)> ProbeSeasonPlayer(List<EpisodeLinkInfo> seasonEpisodes)
        {
            if (seasonEpisodes == null || seasonEpisodes.Count == 0)
                return (null, null);

            foreach (var episode in seasonEpisodes.OrderBy(e => e.episode))
            {
                if (episode == null || string.IsNullOrWhiteSpace(episode.url))
                    continue;

                var probed = await ProbeEpisodePlayer(episode.url);
                string playerType = probed.playerType;

                episode.iframeUrl = probed.iframeUrl;
                episode.playerType = playerType;

                if (string.IsNullOrWhiteSpace(playerType))
                    continue;

                if (playerType == "trailer")
                    continue;

                return probed;
            }

            return (null, null);
        }

        private static string NormalizeSerialPlayerKey(string playerType, string iframeUrl)
        {
            if (string.IsNullOrWhiteSpace(playerType) || string.IsNullOrWhiteSpace(iframeUrl))
                return iframeUrl;

            if (playerType == "ashdi-serial")
            {
                var match = Regex.Match(iframeUrl, @"(https://ashdi\.vip/serial/\d+)", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return iframeUrl;
        }

        private void MergeVoices(SerialAggregatedStructure structure, List<VoiceInfo> voices)
        {
            if (structure == null || voices == null || voices.Count == 0)
                return;

            foreach (var voice in voices)
            {
                if (voice == null || string.IsNullOrWhiteSpace(voice.DisplayName))
                    continue;

                if (!structure.Voices.TryGetValue(voice.DisplayName, out VoiceInfo existing))
                {
                    structure.Voices[voice.DisplayName] = voice;
                    continue;
                }

                foreach (var season in voice.Seasons)
                    existing.Seasons[season.Key] = season.Value;
            }
        }

        private void AddVodSeasonEpisodes(SerialAggregatedStructure structure, string playerType, int season, List<EpisodeLinkInfo> seasonEpisodes)
        {
            if (structure == null || string.IsNullOrWhiteSpace(playerType) || seasonEpisodes == null || seasonEpisodes.Count == 0)
                return;

            string displayName = playerType == "ashdi-vod" ? "Uaflix #3" : "Uaflix #2";
            if (!structure.Voices.ContainsKey(displayName))
            {
                structure.Voices[displayName] = new VoiceInfo
                {
                    Name = "Uaflix",
                    PlayerType = playerType,
                    DisplayName = displayName,
                    Seasons = new Dictionary<int, List<EpisodeInfo>>()
                };
            }

            var episodes = seasonEpisodes
                .OrderBy(ep => ep.episode)
                .Select(ep => new EpisodeInfo
                {
                    Number = ep.episode,
                    Title = ep.title,
                    File = ep.url,
                    Id = ep.url,
                    Poster = null,
                    Subtitle = null
                })
                .ToList();

            structure.Voices[displayName].Seasons[season] = episodes;
        }
        
        /// <summary>
        /// Парсинг багатосерійного плеєра (ashdi-serial або zetvideo-serial)
        /// </summary>
        private async Task<List<VoiceInfo>> ParseMultiEpisodePlayer(string iframeUrl, string playerType)
        {
            string referer = "https://uafix.net/";

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", referer)
            };

            try
            {
                // Для ashdi видаляємо параметри season та episode для отримання всіх озвучок
                string requestUrl = iframeUrl;
                if (playerType == "ashdi-serial" && iframeUrl.Contains("ashdi.vip/serial/"))
                {
                    // Витягуємо базовий URL без параметрів
                    var baseUrlMatch = Regex.Match(iframeUrl, @"(https://ashdi\.vip/serial/\d+)");
                    if (baseUrlMatch.Success)
                    {
                        requestUrl = baseUrlMatch.Groups[1].Value;
                        _onLog($"ParseMultiEpisodePlayer: Using base ashdi URL without parameters: {requestUrl}");
                    }
                }

                string html = await GetHtml(AshdiRequestUrl(requestUrl), headers);
                
                // Знайти JSON у new Playerjs({file:'...'})
                var match = Regex.Match(html, @"file:'(\[.+?\])'", RegexOptions.Singleline);
                if (!match.Success)
                {
                    _onLog($"ParseMultiEpisodePlayer: JSON not found in iframe {iframeUrl}");
                    return new List<VoiceInfo>();
                }
                
                string jsonStr = match.Groups[1].Value
                    .Replace("\\'", "'")
                    .Replace("\\\"", "\"");
                
                var voicesArray = JsonConvert.DeserializeObject<List<JObject>>(jsonStr);
                var voices = new List<VoiceInfo>();
                
                string playerPrefix = playerType == "ashdi-serial" ? "Ashdi" : "Zetvideo";
                
                // Для формування унікальних назв озвучок
                var voiceCounts = new Dictionary<string, int>();
                
                foreach (var voiceObj in voicesArray)
                {
                    string voiceName = voiceObj["title"]?.ToString().Trim();
                    if (string.IsNullOrEmpty(voiceName))
                        continue;
                    
                    // Перевіряємо, чи вже існує така назва озвучки
                    if (voiceCounts.ContainsKey(voiceName))
                    {
                        voiceCounts[voiceName]++;
                        // Якщо є дублікат, додаємо номер
                        voiceName = $"{voiceName} {voiceCounts[voiceName]}";
                    }
                    else
                    {
                        // Ініціалізуємо лічильник для нової озвучки
                        voiceCounts[voiceObj["title"]?.ToString().Trim()] = 1;
                    }
                    
                    var voiceInfo = new VoiceInfo
                    {
                        Name = voiceObj["title"]?.ToString().Trim(), // Зберігаємо оригінальну назву для внутрішнього використання
                        PlayerType = playerType,
                        DisplayName = voiceName, // Відображаємо унікальну назву
                        Seasons = new Dictionary<int, List<EpisodeInfo>>()
                    };
                    
                    var seasons = voiceObj["folder"] as JArray;
                    if (seasons != null)
                    {
                        foreach (var seasonObj in seasons)
                        {
                            string seasonTitle = seasonObj["title"]?.ToString();
                            var seasonMatch = Regex.Match(seasonTitle, @"Сезон\s+(\d+)", RegexOptions.IgnoreCase);
                            
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
                
                _onLog($"ParseMultiEpisodePlayer: Found {voices.Count} voices in {playerType}");
                return voices;
            }
            catch (Exception ex)
            {
                _onLog($"ParseMultiEpisodePlayer error: {ex.Message}");
                return new List<VoiceInfo>();
            }
        }
        
        /// <summary>
        /// Парсинг одного епізоду з zetvideo-vod
        /// </summary>
        private async Task<(string file, string voiceName)> ParseSingleEpisodePlayer(string iframeUrl)
        {
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://uafix.net/")
            };
            
            try
            {
                string html = await GetHtml(iframeUrl, headers);
                
                // Знайти file:"url"
                var match = Regex.Match(html, @"file:\s*""([^""]+\.m3u8)""");
                if (!match.Success)
                    return (null, null);
                
                string fileUrl = match.Groups[1].Value;
                
                // Визначити озвучку з URL
                string voiceName = ExtractVoiceFromUrl(fileUrl);
                
                return (fileUrl, voiceName);
            }
            catch (Exception ex)
            {
                _onLog($"ParseSingleEpisodePlayer error: {ex.Message}");
                return (null, null);
            }
        }
        
        /// <summary>
        /// Парсинг одного епізоду з ashdi-vod (новий метод для обробки окремих епізодів з ashdi.vip/vod/)
        /// </summary>
        private async Task<List<PlayStream>> ParseAshdiVodEpisode(string iframeUrl)
        {
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://uafix.net/")
            };

            var result = new List<PlayStream>();
            try
            {
                string requestUrl = WithAshdiMultivoice(iframeUrl);
                string html = await GetHtml(AshdiRequestUrl(requestUrl), headers);
                if (string.IsNullOrEmpty(html))
                    return result;

                string rawArray = ExtractPlayerFileArray(html);
                if (!string.IsNullOrWhiteSpace(rawArray))
                {
                    string json = WebUtility.HtmlDecode(rawArray)
                        .Replace("\\/", "/")
                        .Replace("\\'", "'")
                        .Replace("\\\"", "\"");

                    var items = JsonConvert.DeserializeObject<List<JObject>>(json);
                    if (items != null && items.Count > 0)
                    {
                        int index = 1;
                        foreach (var item in items)
                        {
                            string fileUrl = item?["file"]?.ToString();
                            if (string.IsNullOrWhiteSpace(fileUrl))
                                continue;

                            string rawTitle = item["title"]?.ToString();
                            result.Add(new PlayStream
                            {
                                link = fileUrl,
                                quality = DetectQualityTag($"{rawTitle} {fileUrl}") ?? "auto",
                                title = BuildDisplayTitle(rawTitle, fileUrl, index)
                            });
                            index++;
                        }

                        if (result.Count > 0)
                            return result;
                    }
                }

                // Fallback для старого формату, де є лише один file
                var match = Regex.Match(html, @"file:\s*'?([^'""\s,}]+\.m3u8)'?");
                if (!match.Success)
                    match = Regex.Match(html, @"file['""]?\s*:\s*['""]([^'""}]+\.m3u8)['""]");

                if (!match.Success)
                    return result;

                string fallbackFile = match.Groups[1].Value;
                result.Add(new PlayStream
                {
                    link = fallbackFile,
                    quality = DetectQualityTag(fallbackFile) ?? "auto",
                    title = BuildDisplayTitle(ExtractVoiceFromUrl(fallbackFile), fallbackFile, 1)
                });

                return result;
            }
            catch (Exception ex)
            {
                _onLog($"ParseAshdiVodEpisode error: {ex.Message}");
                return result;
            }
        }
        
        /// <summary>
        /// Витягнути назву озвучки з URL файлу
        /// </summary>
        private string ExtractVoiceFromUrl(string fileUrl)
        {
            if (string.IsNullOrEmpty(fileUrl))
                return "Невідомо";
            
            if (fileUrl.Contains("uaflix"))
                return "Uaflix";
            else if (fileUrl.Contains("dniprofilm"))
                return "DniproFilm";
            else if (fileUrl.Contains("newstudio"))
                return "NewStudio";
            
            return "Невідомо";
        }
        
        #endregion
        
        #region Агрегація структури серіалу з усіх джерел
        
        /// <summary>
        /// Агрегує озвучки з усіх епізодів серіалу (ashdi, zetvideo-serial, zetvideo-vod)
        /// </summary>
        public async Task<SerialAggregatedStructure> AggregateSerialStructure(string serialUrl)
        {
            string memKey = $"UaFlix:aggregated:{serialUrl}";
            if (_hybridCache.TryGetValue(memKey, out SerialAggregatedStructure cached))
            {
                _onLog($"AggregateSerialStructure: Using cached structure for {serialUrl}");
                return cached;
            }

            try
            {
                if (string.IsNullOrEmpty(serialUrl) || !Uri.IsWellFormedUriString(serialUrl, UriKind.Absolute))
                {
                    _onLog($"AggregateSerialStructure: Invalid URL: {serialUrl}");
                    return null;
                }

                var paginationInfo = await GetPaginationInfo(serialUrl);

                var structure = new SerialAggregatedStructure
                {
                    SerialUrl = serialUrl,
                    Voices = new Dictionary<string, VoiceInfo>(),
                    AllEpisodes = paginationInfo?.Episodes ?? new List<EpisodeLinkInfo>()
                };

                var serialPlayersProcessed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool hasPaginationEpisodes = paginationInfo?.Episodes != null && paginationInfo.Episodes.Any();

                if (hasPaginationEpisodes)
                {
                    var episodesBySeason = paginationInfo.Episodes
                        .GroupBy(e => e.season)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    _onLog($"AggregateSerialStructure: Processing {episodesBySeason.Count} seasons");

                    foreach (var seasonGroup in episodesBySeason)
                    {
                        int season = seasonGroup.Key;
                        _onLog($"AggregateSerialStructure: Processing season {season}");

                        var seasonProbe = await ProbeSeasonPlayer(seasonGroup.Value);
                        if (string.IsNullOrWhiteSpace(seasonProbe.playerType))
                        {
                            _onLog($"AggregateSerialStructure: Season {season} has no supported player");
                            continue;
                        }

                        if (seasonProbe.playerType == "ashdi-serial" || seasonProbe.playerType == "zetvideo-serial")
                        {
                            string serialKey = NormalizeSerialPlayerKey(seasonProbe.playerType, seasonProbe.iframeUrl);
                            if (!serialPlayersProcessed.Add(serialKey))
                            {
                                _onLog($"AggregateSerialStructure: Serial player already parsed for season {season}: {serialKey}");
                                continue;
                            }

                            var voices = await ParseMultiEpisodePlayer(seasonProbe.iframeUrl, seasonProbe.playerType);
                            if (voices == null || voices.Count == 0)
                            {
                                _onLog($"AggregateSerialStructure: No voices in serial player for season {season}");
                                continue;
                            }

                            MergeVoices(structure, voices);
                            _onLog($"AggregateSerialStructure: Parsed serial player {seasonProbe.playerType}, voices={voices.Count}");
                            continue;
                        }

                        if (seasonProbe.playerType == "ashdi-vod" || seasonProbe.playerType == "zetvideo-vod")
                        {
                            AddVodSeasonEpisodes(structure, seasonProbe.playerType, season, seasonGroup.Value);
                            _onLog($"AggregateSerialStructure: Added vod season {season}, episodes={seasonGroup.Value.Count}");
                            continue;
                        }

                        _onLog($"AggregateSerialStructure: Unsupported player {seasonProbe.playerType} for season {season}");
                    }
                }
                else
                {
                    _onLog($"AggregateSerialStructure: No episodes from pagination for {serialUrl}, fallback to page iframe");

                    var serialProbe = await ProbeEpisodePlayer(serialUrl);
                    if (string.IsNullOrWhiteSpace(serialProbe.playerType))
                    {
                        _onLog($"AggregateSerialStructure: Fallback probe failed for {serialUrl}");
                        return null;
                    }

                    if (serialProbe.playerType == "ashdi-serial" || serialProbe.playerType == "zetvideo-serial")
                    {
                        var voices = await ParseMultiEpisodePlayer(serialProbe.iframeUrl, serialProbe.playerType);
                        if (voices == null || voices.Count == 0)
                        {
                            _onLog($"AggregateSerialStructure: Fallback serial player has no voices for {serialUrl}");
                            return null;
                        }

                        MergeVoices(structure, voices);
                        _onLog($"AggregateSerialStructure: Fallback serial player parsed, voices={voices.Count}");
                    }
                    else if (serialProbe.playerType == "ashdi-vod" || serialProbe.playerType == "zetvideo-vod")
                    {
                        var syntheticEpisodes = new List<EpisodeLinkInfo>
                        {
                            new EpisodeLinkInfo
                            {
                                url = serialUrl,
                                title = "Епізод 1",
                                season = 1,
                                episode = 1,
                                iframeUrl = serialProbe.iframeUrl,
                                playerType = serialProbe.playerType
                            }
                        };

                        structure.AllEpisodes = syntheticEpisodes;
                        AddVodSeasonEpisodes(structure, serialProbe.playerType, 1, syntheticEpisodes);
                    }
                    else
                    {
                        _onLog($"AggregateSerialStructure: Fallback player is not supported for serial: {serialProbe.playerType}");
                        return null;
                    }
                }

                if (!structure.Voices.Any())
                {
                    _onLog($"AggregateSerialStructure: No voices found after aggregation for {serialUrl}");
                    return null;
                }

                NormalizeUaflixVoiceNames(structure);

                // Edge Case 9: Перевірка наявності епізодів у озвучках
                bool hasEpisodes = structure.Voices.Values.Any(v => v.Seasons.Values.Any(s => s.Any()));
                if (!hasEpisodes)
                {
                    _onLog($"AggregateSerialStructure: No episodes found in any voice for {serialUrl}");
                    _onLog($"AggregateSerialStructure: Voices count: {structure.Voices.Count}");
                    foreach (var voice in structure.Voices)
                    {
                        _onLog($"  Voice {voice.Key}: {voice.Value.Seasons.Sum(s => s.Value.Count)} total episodes");
                    }
                    return null;
                }

                _hybridCache.Set(memKey, structure, cacheTime(40));
                _onLog($"AggregateSerialStructure: Cached structure with {structure.Voices.Count} total voices");

                // Детальне логування структури для діагностики
                foreach (var voice in structure.Voices)
                {
                    _onLog($"  Voice: {voice.Key} ({voice.Value.PlayerType}) - Seasons: {voice.Value.Seasons.Count}");
                    foreach (var season in voice.Value.Seasons)
                    {
                        _onLog($"    Season {season.Key}: {season.Value.Count} episodes");
                        foreach (var episode in season.Value.Take(3)) // Показуємо тільки перші 3 епізоди
                        {
                            _onLog($"      Episode {episode.Number}: {episode.Title} - {episode.File}");
                        }
                        if (season.Value.Count > 3)
                            _onLog($"      ... and {season.Value.Count - 3} more episodes");
                    }
                }

                return structure;
            }
            catch (Exception ex)
            {
                _onLog($"AggregateSerialStructure error: {ex.Message}");
                return null;
            }
        }
        
        #endregion

        public async Task<List<SearchResult>> Search(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int serial, string original_language, string source, string search_query)
        {
            bool allowAnime = IsAnimeRequest(title, original_title, original_language, source);
            string memKey = $"UaFlix:search:{kinopoisk_id}:{imdb_id}:{serial}:{year}:{allowAnime}:{title}:{original_title}:{search_query}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchResult> cached))
                return cached;

            try
            {
                var queries = new List<string>()
                {
                    original_title,
                    title,
                    search_query
                }
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Select(q => q.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

                if (queries.Count == 0)
                    return null;

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                var uniqueByUrl = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
                foreach (string query in queries)
                {
                    string searchUrl = $"{_init.host}/index.php?do=search&subaction=search&story={System.Web.HttpUtility.UrlEncode(query)}";
                    string searchHtml = await GetHtml(searchUrl, headers);
                    if (string.IsNullOrWhiteSpace(searchHtml))
                        continue;

                    var doc = new HtmlDocument();
                    doc.LoadHtml(searchHtml);

                    var filmNodes = doc.DocumentNode.SelectNodes("//a[contains(@class, 'sres-wrap')]");
                    if (filmNodes == null || filmNodes.Count == 0)
                        continue;

                    foreach (var filmNode in filmNodes)
                    {
                        try
                        {
                            var h2Node = filmNode.SelectSingleNode(".//h2") ?? filmNode.SelectSingleNode(".//h3");
                            if (h2Node == null)
                                continue;

                            string filmUrl = filmNode.GetAttributeValue("href", "");
                            if (string.IsNullOrWhiteSpace(filmUrl))
                                continue;

                            if (!filmUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                filmUrl = _init.host + filmUrl;

                            if (uniqueByUrl.ContainsKey(filmUrl))
                                continue;

                            var descNode = filmNode.SelectSingleNode(".//div[contains(@class, 'sres-desc')]") ??
                                           filmNode.SelectSingleNode(".//span[contains(@class, 'year')]");
                            int filmYear = ExtractYear(descNode?.InnerText);

                            var posterNode = filmNode.SelectSingleNode(".//img[@src]") ??
                                            filmNode.SelectSingleNode(".//img[@data-src]");
                            string posterUrl = posterNode?.GetAttributeValue("src", "") ?? posterNode?.GetAttributeValue("data-src", "");
                            if (!string.IsNullOrEmpty(posterUrl) && !posterUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                posterUrl = _init.host + posterUrl;

                            string category = ExtractCategoryFromUrl(filmUrl);
                            bool isAnime = string.Equals(category, "anime", StringComparison.OrdinalIgnoreCase);

                            uniqueByUrl[filmUrl] = new SearchResult
                            {
                                Title = WebUtility.HtmlDecode(h2Node.InnerText?.Trim() ?? string.Empty),
                                Url = filmUrl,
                                Year = filmYear,
                                PosterUrl = posterUrl,
                                Category = category,
                                IsAnime = isAnime
                            };
                        }
                        catch (Exception ex)
                        {
                            _onLog($"Search: Error processing film node: {ex.Message}");
                        }
                    }
                }

                if (uniqueByUrl.Count == 0)
                    return null;

                var results = uniqueByUrl.Values.ToList();
                results = FilterByContentType(results, serial, allowAnime);
                if (results.Count == 0)
                    return null;

                await EnrichSearchResults(results, year);

                foreach (var result in results)
                {
                    result.TitleMatched = HasStrongTitleMatch(result, title, original_title);
                    result.YearMatched = year > 0 && result.Year == year;
                    result.MatchScore = BuildMatchScore(result, title, original_title, year, serial, allowAnime);
                }

                results = results
                    .OrderByDescending(r => r.MatchScore)
                    .ThenByDescending(r => r.TitleMatched)
                    .ThenByDescending(r => r.YearMatched)
                    .ThenBy(r => r.Title)
                    .ToList();

                _hybridCache.Set(memKey, results, cacheTime(20));
                return results;
            }
            catch (Exception ex)
            {
                _onLog($"UaFlix search error: {ex.Message}");
                return null;
            }
        }

        public bool IsAnimeRequest(string title, string originalTitle, string originalLanguage, string source)
        {
            string combined = $"{title} {originalTitle} {source}".ToLowerInvariant();
            if (combined.Contains("anime") || combined.Contains("аніме"))
                return true;

            return string.Equals(originalLanguage, "ja", StringComparison.OrdinalIgnoreCase);
        }

        public SearchResult SelectBestSearchResult(List<SearchResult> results, string title, string originalTitle, int year)
        {
            if (results == null || results.Count == 0)
                return null;

            var ordered = results
                .OrderByDescending(r => r.MatchScore)
                .ToList();

            if (ordered.Count == 1)
                return ordered[0];

            if (year > 0)
            {
                var strict = ordered
                    .Where(r => r.TitleMatched && r.YearMatched)
                    .ToList();

                if (strict.Count == 1)
                    return strict[0];

                if (strict.Count > 1)
                    return null;
            }
            else
            {
                var titleOnly = ordered.Where(r => r.TitleMatched).ToList();
                if (titleOnly.Count == 1)
                    return titleOnly[0];
            }

            return null;
        }

        private async Task EnrichSearchResults(List<SearchResult> results, int targetYear)
        {
            if (results == null || results.Count == 0)
                return;

            var tasks = results.Select(async result =>
            {
                if (result == null || string.IsNullOrWhiteSpace(result.Url))
                    return;

                if (targetYear <= 0 && result.Year > 0 && !string.IsNullOrWhiteSpace(result.Category))
                    return;

                var meta = await LoadSearchMeta(result.Url);
                if (meta == null)
                    return;

                if (result.Year <= 0 && meta.Year > 0)
                    result.Year = meta.Year;

                if (string.IsNullOrWhiteSpace(result.Category))
                    result.Category = meta.Category;

                if (!result.IsAnime && meta.IsAnime)
                    result.IsAnime = true;
            });

            await Task.WhenAll(tasks);
        }

        private async Task<SearchMeta> LoadSearchMeta(string url)
        {
            string memKey = $"UaFlix:searchmeta:{url}";
            if (_hybridCache.TryGetValue(memKey, out SearchMeta cached))
                return cached;

            var meta = new SearchMeta
            {
                Category = ExtractCategoryFromUrl(url)
            };
            meta.IsAnime = string.Equals(meta.Category, "anime", StringComparison.OrdinalIgnoreCase);

            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                string html = await GetHtml(url, headers);
                if (!string.IsNullOrWhiteSpace(html))
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var yearNode = doc.DocumentNode.SelectSingleNode("//li[contains(@class, 'vis')]//span[contains(@class, 'year')]");
                    int year = ExtractYear(yearNode?.InnerText);
                    if (year <= 0)
                    {
                        var createdNode = doc.DocumentNode.SelectSingleNode("//*[@itemprop='dateCreated']");
                        year = ExtractYear(createdNode?.GetAttributeValue("content", null) ?? createdNode?.InnerText);
                    }

                    meta.Year = year;
                }
            }
            catch (Exception ex)
            {
                _onLog($"LoadSearchMeta error: {ex.Message}");
            }

            _hybridCache.Set(memKey, meta, cacheTime(60));
            return meta;
        }

        private List<SearchResult> FilterByContentType(List<SearchResult> input, int serial, bool allowAnime)
        {
            if (input == null || input.Count == 0)
                return new List<SearchResult>();

            string expected = serial == 1 ? "serials" : "films";

            var filtered = input
                .Where(i => allowAnime || !i.IsAnime)
                .Where(i => string.IsNullOrWhiteSpace(i.Category) || i.Category == expected || (allowAnime && i.IsAnime))
                .ToList();

            if (filtered.Count > 0)
                return filtered;

            return input
                .Where(i => allowAnime || !i.IsAnime)
                .ToList();
        }

        private int BuildMatchScore(SearchResult result, string title, string originalTitle, int year, int serial, bool allowAnime)
        {
            if (result == null)
                return 0;

            int score = 0;
            if (result.TitleMatched)
                score += 100;
            else
                score += ComputePartialTitleScore(result?.Title, title, originalTitle);

            if (year > 0)
            {
                if (result.Year == year)
                    score += 60;
                else if (result.Year > 0 && Math.Abs(result.Year - year) == 1)
                    score += 10;
                else if (result.Year > 0)
                    score -= 15;
            }

            if (serial == 1)
            {
                if (string.Equals(result.Category, "serials", StringComparison.OrdinalIgnoreCase))
                    score += 25;
                else if (string.Equals(result.Category, "films", StringComparison.OrdinalIgnoreCase))
                    score -= 10;
            }
            else
            {
                if (string.Equals(result.Category, "films", StringComparison.OrdinalIgnoreCase))
                    score += 25;
                else if (string.Equals(result.Category, "serials", StringComparison.OrdinalIgnoreCase))
                    score -= 10;
            }

            if (result.IsAnime && !allowAnime)
                score -= 80;

            return score;
        }

        private int ComputePartialTitleScore(string candidateTitle, string title, string originalTitle)
        {
            var candidateTokens = ToTitleTokens(candidateTitle);
            if (candidateTokens.Count == 0)
                return 0;

            var targetTokens = ToTitleTokens(title);
            foreach (var token in ToTitleTokens(originalTitle))
                targetTokens.Add(token);

            if (targetTokens.Count == 0)
                return 0;

            int overlap = candidateTokens.Count(t => targetTokens.Contains(t));
            double ratio = overlap / (double)Math.Max(candidateTokens.Count, targetTokens.Count);

            if (ratio >= 0.85) return 70;
            if (ratio >= 0.65) return 50;
            if (ratio >= 0.45) return 30;
            if (ratio >= 0.30) return 15;
            return 0;
        }

        private bool HasStrongTitleMatch(SearchResult result, string title, string originalTitle)
        {
            if (result == null)
                return false;

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in new[] { title, originalTitle })
            {
                string normalized = NormalizeTitle(candidate);
                if (!string.IsNullOrWhiteSpace(normalized))
                    targets.Add(normalized);
            }

            if (targets.Count == 0)
                return false;

            foreach (string part in SplitTitleParts(result.Title))
            {
                string normalizedPart = NormalizeTitle(part);
                if (string.IsNullOrWhiteSpace(normalizedPart))
                    continue;

                if (targets.Contains(normalizedPart))
                    return true;

                foreach (string target in targets)
                {
                    if (normalizedPart.Length >= 6 && target.Length >= 6 &&
                        (normalizedPart.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                         target.Contains(normalizedPart, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> SplitTitleParts(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return Enumerable.Empty<string>();

            return title
                .Split(new[] { '/', '|', '•' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => WebUtility.HtmlDecode(part.Trim()));
        }

        private static HashSet<string> ToTitleTokens(string value)
        {
            string normalized = NormalizeTitle(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 1)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string text = WebUtility.HtmlDecode(value).ToLowerInvariant();
            text = Regex.Replace(text, @"[^\p{L}\p{Nd}\s]+", " ");
            text = Regex.Replace(text, @"\b(season|сезон|частина|part|ova|special|movie|film)\b", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        private static int ExtractYear(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            var match = Regex.Match(text, @"(?:19|20)\d{2}");
            if (match.Success && int.TryParse(match.Value, out int year))
                return year;

            return 0;
        }

        private static string ExtractCategoryFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                var uri = new Uri(url);
                string first = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault()?.ToLowerInvariant();

                if (first == "film")
                    return "films";
                if (first == "serial")
                    return "serials";

                return first;
            }
            catch
            {
                return null;
            }
        }
        
        public async Task<FilmInfo> GetFilmInfo(string filmUrl)
        {
            string memKey = $"UaFlix:filminfo:{filmUrl}";
            if (_hybridCache.TryGetValue(memKey, out FilmInfo res))
                return res;

            try
            {
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) };
                var filmHtml = await GetHtml(filmUrl, headers);
                var doc = new HtmlDocument();
                doc.LoadHtml(filmHtml);
                
                var result = new FilmInfo
                {
                    Url = filmUrl
                };
                
                var titleNode = doc.DocumentNode.SelectSingleNode("//h1[@class='h1-title']");
                if (titleNode != null)
                {
                    result.Title = titleNode.InnerText.Trim();
                }
                
                var metaDuration = doc.DocumentNode.SelectSingleNode("//meta[@property='og:video:duration']");
                if (metaDuration != null)
                {
                    string durationStr = metaDuration.GetAttributeValue("content", "");
                    if (int.TryParse(durationStr, out int duration))
                    {
                        result.Duration = duration;
                    }
                }
                
                var metaActors = doc.DocumentNode.SelectSingleNode("//meta[@property='og:video:actor']");
                if (metaActors != null)
                {
                    string actorsStr = metaActors.GetAttributeValue("content", "");
                    result.Actors = actorsStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(a => a.Trim())
                                          .ToList();
                }
                
                var metaDirector = doc.DocumentNode.SelectSingleNode("//meta[@property='og:video:director']");
                if (metaDirector != null)
                {
                    result.Director = metaDirector.GetAttributeValue("content", "");
                }
                
                var descNode = doc.DocumentNode.SelectSingleNode("//div[@id='main-descr']//div[@itemprop='description']");
                if (descNode != null)
                {
                    result.Description = descNode.InnerText.Trim();
                }
                
                var posterNode = doc.DocumentNode.SelectSingleNode("//img[@itemprop='image']");
                if (posterNode != null)
                {
                    result.PosterUrl = posterNode.GetAttributeValue("src", "");
                    if (!result.PosterUrl.StartsWith("http") && !string.IsNullOrEmpty(result.PosterUrl))
                    {
                        result.PosterUrl = _init.host + result.PosterUrl;
                    }
                }
                
                _hybridCache.Set(memKey, result, cacheTime(60));
                return result;
            }
            catch (Exception ex)
            {
                _onLog($"UaFlix GetFilmInfo error: {ex.Message}");
            }
            return null;
        }

        public async Task<PaginationInfo> GetPaginationInfo(string filmUrl)
        {
            string memKey = $"UaFlix:pagination:{filmUrl}";
            if (_hybridCache.TryGetValue(memKey, out PaginationInfo res))
                return res;

            try
            {
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) };
                var filmHtml = await GetHtml(filmUrl, headers);
                var filmDoc = new HtmlDocument();
                filmDoc.LoadHtml(filmHtml);
                
                var paginationInfo = new PaginationInfo
                {
                    SerialUrl = filmUrl
                };

                var allEpisodes = new List<EpisodeLinkInfo>();
                var seasonUrls = new HashSet<string>();

                var seasonNodes = filmDoc.DocumentNode.SelectNodes("//div[contains(@class, 'sez-wr')]//a");
                if (seasonNodes == null)
                    seasonNodes = filmDoc.DocumentNode.SelectNodes("//div[contains(@class, 'fss-box')]//a");
                if (seasonNodes != null && seasonNodes.Count > 0)
                {
                    foreach (var node in seasonNodes)
                    {
                        string pageUrl = node.GetAttributeValue("href", null);
                        if (!string.IsNullOrEmpty(pageUrl))
                        {
                            if (!pageUrl.StartsWith("http"))
                                pageUrl = _init.host + pageUrl;
                            
                            seasonUrls.Add(pageUrl);
                        }
                    }
                }
                else
                {
                    seasonUrls.Add(filmUrl);
                }

                var safeSeasonUrls = seasonUrls.ToList();
                if (safeSeasonUrls.Count == 0)
                    return null;

                var seasonTasks = safeSeasonUrls.Select(url => GetHtml(url, headers));
                var seasonPagesHtml = await Task.WhenAll(seasonTasks);

                foreach (var html in seasonPagesHtml)
                {
                    var pageDoc = new HtmlDocument();
                    pageDoc.LoadHtml(html);

                    var episodeNodes = pageDoc.DocumentNode.SelectNodes("//div[contains(@class, 'frels')]//a[contains(@class, 'vi-img')]");
                    if (episodeNodes != null)
                    {
                        foreach (var episodeNode in episodeNodes)
                        {
                            string episodeUrl = episodeNode.GetAttributeValue("href", "");
                            if (!episodeUrl.StartsWith("http"))
                                episodeUrl = _init.host + episodeUrl;

                            var match = Regex.Match(episodeUrl, @"season-(\d+).*?episode-(\d+)");
                            if (match.Success)
                            {
                                allEpisodes.Add(new EpisodeLinkInfo
                                {
                                    url = episodeUrl,
                                    title = episodeNode.SelectSingleNode(".//div[@class='vi-rate']")?.InnerText.Trim() ?? $"Епізод {match.Groups[2].Value}",
                                    season = int.Parse(match.Groups[1].Value),
                                    episode = int.Parse(match.Groups[2].Value)
                                });
                            }
                        }
                    }
                }

                paginationInfo.Episodes = allEpisodes.OrderBy(e => e.season).ThenBy(e => e.episode).ToList();

                if (paginationInfo.Episodes.Any())
                {
                    var uniqueSeasons = paginationInfo.Episodes.Select(e => e.season).Distinct().OrderBy(se => se);
                    foreach (var season in uniqueSeasons)
                    {
                        paginationInfo.Seasons[season] = 1;
                    }
                }

                if (paginationInfo.Episodes.Count > 0)
                {
                    _hybridCache.Set(memKey, paginationInfo, cacheTime(20));
                    return paginationInfo;
                }
            }
            catch (Exception ex)
            {
                _onLog($"UaFlix GetPaginationInfo error: {ex.Message}");
            }
            return null;
        }
        
        public async Task<Uaflix.Models.PlayResult> ParseEpisode(string url)
        {
            var result = new Uaflix.Models.PlayResult() { streams = new List<PlayStream>() };
            try
            {
                string html = await GetHtml(url, new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) });
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var videoNode = doc.DocumentNode.SelectSingleNode("//video");
                if (videoNode != null)
                {
                    string videoUrl = videoNode.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(videoUrl))
                    {
                        result.streams.Add(new PlayStream
                        {
                            link = videoUrl,
                            quality = "1080p",
                            title = BuildDisplayTitle("Основне джерело", videoUrl, 1)
                        });
                        return result;
                    }
                }

                string iframeUrl = ExtractIframeUrl(doc);
                if (!string.IsNullOrEmpty(iframeUrl))
                {
                    if (iframeUrl.Contains("ashdi.vip/serial/"))
                    {
                        result.ashdi_url = iframeUrl;
                        return result;
                    }

                    // Ігноруємо YouTube трейлери
                    if (iframeUrl.Contains("youtube.com/embed/"))
                    {
                        _onLog($"ParseEpisode: Ignoring YouTube trailer iframe: {iframeUrl}");
                        return result;
                    }

                    if (iframeUrl.Contains("zetvideo.net"))
                        result.streams = await ParseAllZetvideoSources(iframeUrl);
                    else if (iframeUrl.Contains("ashdi.vip"))
                    {
                        // Перевіряємо, чи це ashdi-vod (окремий епізод) або ashdi-serial (багатосерійний плеєр)
                        if (iframeUrl.Contains("/vod/"))
                        {
                            // Це окремий епізод на ashdi.vip/vod/, обробляємо як ashdi-vod
                            result.streams = await ParseAshdiVodEpisode(iframeUrl);
                        }
                        else
                        {
                            // Це багатосерійний плеєр, обробляємо як і раніше
                            result.streams = await ParseAllAshdiSources(iframeUrl);
                            var idMatch = Regex.Match(iframeUrl, @"_(\d+)|vod/(\d+)");
                            if (idMatch.Success)
                            {
                                string ashdiId = idMatch.Groups[1].Success ? idMatch.Groups[1].Value : idMatch.Groups[2].Value;
                                result.subtitles = await GetAshdiSubtitles(ashdiId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _onLog($"ParseEpisode error: {ex.Message}");
            }
            _onLog($"ParseEpisode result: streams.count={result.streams.Count}, ashdi_url={result.ashdi_url}");
            return result;
        }

        private void NormalizeUaflixVoiceNames(SerialAggregatedStructure structure)
        {
            const string baseName = "Uaflix";
            const string zetName = "Uaflix #2";
            const string ashdiName = "Uaflix #3";

            if (structure == null || structure.Voices == null || structure.Voices.Count == 0)
                return;

            bool hasBase = structure.Voices.ContainsKey(baseName);
            bool hasZet = structure.Voices.ContainsKey(zetName);
            bool hasAshdi = structure.Voices.ContainsKey(ashdiName);

            if (hasBase)
                return;

            if (hasZet && !hasAshdi)
            {
                var voice = structure.Voices[zetName];
                voice.DisplayName = baseName;
                structure.Voices.Remove(zetName);
                structure.Voices[baseName] = voice;
            }
            else if (hasAshdi && !hasZet)
            {
                var voice = structure.Voices[ashdiName];
                voice.DisplayName = baseName;
                structure.Voices.Remove(ashdiName);
                structure.Voices[baseName] = voice;
            }
        }

        async Task<List<PlayStream>> ParseAllZetvideoSources(string iframeUrl)
        {
            var result = new List<PlayStream>();
            var html = await GetHtml(iframeUrl, new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", "https://zetvideo.net/") });
            if (string.IsNullOrEmpty(html)) return result;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            var script = doc.DocumentNode.SelectSingleNode("//script[contains(text(), 'file:')]");
            if (script != null)
            {
                var match = Regex.Match(script.InnerText, @"file:\s*""([^""]+\.m3u8)");
                if (match.Success)
                {
                    string link = match.Groups[1].Value;
                    result.Add(new PlayStream
                    {
                        link = link,
                        quality = "1080p",
                        title = BuildDisplayTitle("Основне джерело", link, 1)
                    });
                    return result;
                }
            }

            var sourceNodes = doc.DocumentNode.SelectNodes("//source[contains(@src, '.m3u8')]");
            if (sourceNodes != null)
            {
                foreach (var node in sourceNodes)
                {
                    string link = node.GetAttributeValue("src", null);
                    string quality = node.GetAttributeValue("label", null) ?? node.GetAttributeValue("res", null) ?? "1080p";
                    result.Add(new PlayStream
                    {
                        link = link,
                        quality = quality,
                        title = BuildDisplayTitle(quality, link, result.Count + 1)
                    });
                }
            }
            return result;
        }

        async Task<List<PlayStream>> ParseAllAshdiSources(string iframeUrl)
        {
            var result = new List<PlayStream>();
            var html = await GetHtml(AshdiRequestUrl(iframeUrl), new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", "https://ashdi.vip/") });
             if (string.IsNullOrEmpty(html)) return result;
             
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sourceNodes = doc.DocumentNode.SelectNodes("//source[contains(@src, '.m3u8')]");
            if (sourceNodes != null)
            {
                foreach (var node in sourceNodes)
                {
                    string link = node.GetAttributeValue("src", null);
                    string quality = node.GetAttributeValue("label", null) ?? node.GetAttributeValue("res", null) ?? "1080p";
                    result.Add(new PlayStream
                    {
                        link = link,
                        quality = quality,
                        title = BuildDisplayTitle(quality, link, result.Count + 1)
                    });
                }
            }
            return result;
        }
        
        async Task<SubtitleTpl?> GetAshdiSubtitles(string id)
        {
            string url = $"https://ashdi.vip/vod/{id}";
            var html = await GetHtml(AshdiRequestUrl(url), new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", "https://ashdi.vip/") });
            string subtitle = new Regex("subtitle(\")?:\"([^\"]+)\"").Match(html).Groups[2].Value;
            if (!string.IsNullOrEmpty(subtitle))
            {
                var match = new Regex("\\[([^\\]]+)\\](https?://[^\\,]+)").Match(subtitle);
                var st = new Shared.Models.Templates.SubtitleTpl();
                while (match.Success)
                {
                    st.Append(match.Groups[1].Value, match.Groups[2].Value);
                    match = match.NextMatch();
                }
                if (st.data != null && st.data.Count > 0)
                    return st;
            }
            return null;
        }

        private static string WithAshdiMultivoice(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            if (url.IndexOf("ashdi.vip/vod/", StringComparison.OrdinalIgnoreCase) < 0)
                return url;

            if (url.IndexOf("multivoice", StringComparison.OrdinalIgnoreCase) >= 0)
                return url;

            return url.Contains("?") ? $"{url}&multivoice" : $"{url}?multivoice";
        }

        private static string BuildDisplayTitle(string rawTitle, string link, int index)
        {
            string normalized = string.IsNullOrWhiteSpace(rawTitle) ? $"Варіант {index}" : StripMoviePrefix(WebUtility.HtmlDecode(rawTitle).Trim());
            string qualityTag = DetectQualityTag($"{normalized} {link}");
            if (string.IsNullOrWhiteSpace(qualityTag))
                return normalized;

            if (normalized.StartsWith("[4K]", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("[FHD]", StringComparison.OrdinalIgnoreCase))
                return normalized;

            return $"{qualityTag} {normalized}";
        }

        private static string DetectQualityTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (Quality4kRegex.IsMatch(value))
                return "[4K]";

            if (QualityFhdRegex.IsMatch(value))
                return "[FHD]";

            return null;
        }

        private static string StripMoviePrefix(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title;

            string normalized = Regex.Replace(title, @"\s+", " ").Trim();
            int sepIndex = normalized.LastIndexOf(" - ", StringComparison.Ordinal);
            if (sepIndex <= 0 || sepIndex >= normalized.Length - 3)
                return normalized;

            string prefix = normalized.Substring(0, sepIndex).Trim();
            string suffix = normalized.Substring(sepIndex + 3).Trim();
            if (string.IsNullOrWhiteSpace(suffix))
                return normalized;

            if (Regex.IsMatch(prefix, @"(19|20)\d{2}"))
                return suffix;

            return normalized;
        }

        private static string ExtractPlayerFileArray(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            int searchIndex = 0;
            while (searchIndex >= 0 && searchIndex < html.Length)
            {
                int fileIndex = html.IndexOf("file", searchIndex, StringComparison.OrdinalIgnoreCase);
                if (fileIndex < 0)
                    return null;

                int colonIndex = html.IndexOf(':', fileIndex);
                if (colonIndex < 0)
                    return null;

                int startIndex = colonIndex + 1;
                while (startIndex < html.Length && char.IsWhiteSpace(html[startIndex]))
                    startIndex++;

                if (startIndex < html.Length && (html[startIndex] == '\'' || html[startIndex] == '"'))
                {
                    startIndex++;
                    while (startIndex < html.Length && char.IsWhiteSpace(html[startIndex]))
                        startIndex++;
                }

                if (startIndex >= html.Length || html[startIndex] != '[')
                {
                    searchIndex = fileIndex + 4;
                    continue;
                }

                return ExtractBracketArray(html, startIndex);
            }

            return null;
        }

        private static string ExtractBracketArray(string text, int startIndex)
        {
            if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '[')
                return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            char quoteChar = '\0';

            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (ch == quoteChar)
                    {
                        inString = false;
                        quoteChar = '\0';
                    }

                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    inString = true;
                    quoteChar = ch;
                    continue;
                }

                if (ch == '[')
                {
                    depth++;
                    continue;
                }

                if (ch == ']')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(startIndex, i - startIndex + 1);
                }
            }

            return null;
        }

        sealed class EpisodePlayerInfo
        {
            public string IframeUrl { get; set; }
            public string PlayerType { get; set; }
        }

        sealed class SearchMeta
        {
            public int Year { get; set; }
            public string Category { get; set; }
            public bool IsAnime { get; set; }
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

        /// <summary>
        /// Оновлений метод кешування згідно стандарту Lampac
        /// </summary>
        public static TimeSpan GetCacheTime(OnlinesSettings init, int multiaccess = 20, int home = 5, int mikrotik = 2, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);
            
            int ctime = AppInit.conf.mikrotik ? mikrotik : AppInit.conf.multiaccess ? init != null && init.cache_time > 0 ? init.cache_time : multiaccess : home;
            if (init != null && ctime > init.cache_time && init.cache_time > 0)
                ctime = init.cache_time;
            
            return TimeSpan.FromMinutes(ctime);
        }
    }
}
