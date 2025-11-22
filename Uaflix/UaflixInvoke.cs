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

namespace Uaflix
{
    public class UaflixInvoke
    {
        private OnlinesSettings _init;
        private HybridCache _hybridCache;
        private Action<string> _onLog;
        private ProxyManager _proxyManager;

        public UaflixInvoke(OnlinesSettings init, HybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }
        
        #region Методи для визначення та парсингу різних типів плеєрів
        
        /// <summary>
        /// Визначити тип плеєра з URL iframe
        /// </summary>
        private string DeterminePlayerType(string iframeUrl)
        {
            if (string.IsNullOrEmpty(iframeUrl))
                return null;

            // Перевіряємо на підтримувані типи плеєрів
            if (iframeUrl.Contains("ashdi.vip/serial/"))
                return "ashdi-serial";
            else if (iframeUrl.Contains("ashdi.vip/vod/"))
                return "ashdi-vod";
            else if (iframeUrl.Contains("zetvideo.net/serial/"))
                return "zetvideo-serial";
            else if (iframeUrl.Contains("zetvideo.net/vod/"))
                return "zetvideo-vod";

            // Перевіряємо на небажані типи плеєрів (трейлери, реклама тощо)
            if (iframeUrl.Contains("youtube.com/embed/") ||
                iframeUrl.Contains("youtu.be/") ||
                iframeUrl.Contains("vimeo.com/") ||
                iframeUrl.Contains("dailymotion.com/"))
                return "trailer"; // Ігноруємо відеохостинги з трейлерами

            return null;
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

                string html = await Http.Get(requestUrl, headers: headers, proxy: _proxyManager.Get());
                
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
                string html = await Http.Get(iframeUrl, headers: headers, proxy: _proxyManager.Get());
                
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
        private async Task<(string file, string voiceName)> ParseAshdiVodEpisode(string iframeUrl)
        {
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://uafix.net/")
            };
            
            try
            {
                string html = await Http.Get(iframeUrl, headers: headers, proxy: _proxyManager.Get());
                
                // Шукаємо Playerjs конфігурацію з file параметром
                var match = Regex.Match(html, @"file:\s*'?([^'""\s,}]+\.m3u8)'?");
                if (!match.Success)
                {
                    // Якщо не знайдено, шукаємо в іншому форматі
                    match = Regex.Match(html, @"file['""]?\s*:\s*['""]([^'""}]+\.m3u8)['""]");
                }
                
                if (!match.Success)
                    return (null, null);
                
                string fileUrl = match.Groups[1].Value;
                
                // Визначити озвучку з URL
                string voiceName = ExtractVoiceFromUrl(fileUrl);
                
                return (fileUrl, voiceName);
            }
            catch (Exception ex)
            {
                _onLog($"ParseAshdiVodEpisode error: {ex.Message}");
                return (null, null);
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
                // Edge Case 1: Перевірка валідності URL
                if (string.IsNullOrEmpty(serialUrl) || !Uri.IsWellFormedUriString(serialUrl, UriKind.Absolute))
                {
                    _onLog($"AggregateSerialStructure: Invalid URL: {serialUrl}");
                    return null;
                }
                
                // Отримати список всіх епізодів
                var paginationInfo = await GetPaginationInfo(serialUrl);
                if (paginationInfo?.Episodes == null || !paginationInfo.Episodes.Any())
                {
                    _onLog($"AggregateSerialStructure: No episodes found for {serialUrl}");
                    return null;
                }
                
                var structure = new SerialAggregatedStructure
                {
                    SerialUrl = serialUrl,
                    Voices = new Dictionary<string, VoiceInfo>(),
                    AllEpisodes = paginationInfo.Episodes
                };
                
                // Групуємо епізоди по сезонах
                var episodesBySeason = paginationInfo.Episodes
                    .GroupBy(e => e.season)
                    .ToDictionary(g => g.Key, g => g.ToList());
                
                _onLog($"AggregateSerialStructure: Processing {episodesBySeason.Count} seasons");
                
                // Для кожного сезону беремо перший епізод та визначаємо тип плеєра
                foreach (var seasonGroup in episodesBySeason)
                {
                    int season = seasonGroup.Key;
                    var firstEpisode = seasonGroup.Value.First();
                    
                    _onLog($"AggregateSerialStructure: Processing season {season}, first episode: {firstEpisode.url}");
                    
                    // Отримати HTML епізоду та знайти iframe
                    var headers = new List<HeadersModel>() { 
                        new HeadersModel("User-Agent", "Mozilla/5.0"), 
                        new HeadersModel("Referer", _init.host) 
                    };
                    
                    string html = await Http.Get(firstEpisode.url, headers: headers, proxy: _proxyManager.Get());
                    
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    var iframe = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'video-box')]//iframe");
                    
                    if (iframe == null)
                    {
                        _onLog($"AggregateSerialStructure: No iframe found for season {season}");
                        continue;
                    }
                    
                    string iframeUrl = iframe.GetAttributeValue("src", "").Replace("&amp;", "&");
                    if (iframeUrl.StartsWith("//"))
                        iframeUrl = "https:" + iframeUrl;
                    
                    // Edge Case 2: Перевірка валідності iframe URL
                    if (string.IsNullOrEmpty(iframeUrl))
                    {
                        _onLog($"AggregateSerialStructure: Empty iframe URL for season {season}");
                        continue;
                    }
                    
                    string playerType = DeterminePlayerType(iframeUrl);
                    _onLog($"AggregateSerialStructure: Season {season} has playerType: {playerType}");

                    // Edge Case 3: Невідомий тип плеєра або YouTube трейлер
                    if (string.IsNullOrEmpty(playerType))
                    {
                        _onLog($"AggregateSerialStructure: Unknown player type for iframe {iframeUrl} in season {season}");
                        continue;
                    }

                    // Ігноруємо трейлери та небажані відеохостинги
                    if (playerType == "trailer")
                    {
                        _onLog($"AggregateSerialStructure: Ignoring trailer/video host for iframe {iframeUrl} in season {season}");
                        continue;
                    }
                    
                    if (playerType == "ashdi-serial" || playerType == "zetvideo-serial")
                    {
                        // Парсимо багатосерійний плеєр
                        var voices = await ParseMultiEpisodePlayer(iframeUrl, playerType);
                        
                        // Edge Case 4: Порожній результат парсингу
                        if (voices == null || !voices.Any())
                        {
                            _onLog($"AggregateSerialStructure: No voices found in {playerType} for season {season}");
                            continue;
                        }
                        
                        foreach (var voice in voices)
                        {
                            // Edge Case 5: Перевірка валідності озвучки
                            if (voice == null || string.IsNullOrEmpty(voice.DisplayName))
                            {
                                _onLog($"AggregateSerialStructure: Invalid voice data in season {season}");
                                continue;
                            }
                            
                            // Додаємо або об'єднуємо з існуючою озвучкою
                            if (!structure.Voices.ContainsKey(voice.DisplayName))
                            {
                                structure.Voices[voice.DisplayName] = voice;
                            }
                            else
                            {
                                // Об'єднуємо сезони
                                foreach (var seasonEpisodes in voice.Seasons)
                                {
                                    structure.Voices[voice.DisplayName].Seasons[seasonEpisodes.Key] = seasonEpisodes.Value;
                                }
                            }
                        }
                    }
                    else if (playerType == "zetvideo-vod")
                    {
                        _onLog($"AggregateSerialStructure: Processing zetvideo-vod for season {season} with {seasonGroup.Value.Count} episodes");
                        
                        // Для zetvideo-vod створюємо озвучку з реальними епізодами
                        string displayName = "Uaflix #2";
                        
                        if (!structure.Voices.ContainsKey(displayName))
                        {
                            structure.Voices[displayName] = new VoiceInfo
                            {
                                Name = "Uaflix",
                                PlayerType = "zetvideo-vod",
                                DisplayName = displayName,
                                Seasons = new Dictionary<int, List<EpisodeInfo>>()
                            };
                        }
                        
                        // Створюємо епізоди для цього сезону з посиланнями на сторінки епізодів
                        var episodes = new List<EpisodeInfo>();
                        foreach (var episodeInfo in seasonGroup.Value)
                        {
                            episodes.Add(new EpisodeInfo
                            {
                                Number = episodeInfo.episode,
                                Title = episodeInfo.title,
                                File = episodeInfo.url, // URL сторінки епізоду для використання в call
                                Id = episodeInfo.url,
                                Poster = null,
                                Subtitle = null
                            });
                        }
                        
                        structure.Voices[displayName].Seasons[season] = episodes;
                        
                        _onLog($"AggregateSerialStructure: Created voice with {episodes.Count} episodes for season {season} in zetvideo-vod");
                    }
                    else if (playerType == "ashdi-vod")
                    {
                        _onLog($"AggregateSerialStructure: Processing ashdi-vod for season {season} with {seasonGroup.Value.Count} episodes");
                        
                        // Для ashdi-vod створюємо озвучку з реальними епізодами
                        string displayName = "Uaflix #3";
                        
                        if (!structure.Voices.ContainsKey(displayName))
                        {
                            structure.Voices[displayName] = new VoiceInfo
                            {
                                Name = "Uaflix",
                                PlayerType = "ashdi-vod",
                                DisplayName = displayName,
                                Seasons = new Dictionary<int, List<EpisodeInfo>>()
                            };
                        }
                        
                        // Створюємо епізоди для цього сезону з посиланнями на сторінки епізодів
                        var episodes = new List<EpisodeInfo>();
                        foreach (var episodeInfo in seasonGroup.Value)
                        {
                            episodes.Add(new EpisodeInfo
                            {
                                Number = episodeInfo.episode,
                                Title = episodeInfo.title,
                                File = episodeInfo.url, // URL сторінки епізоду для використання в call
                                Id = episodeInfo.url,
                                Poster = null,
                                Subtitle = null
                            });
                        }
                        
                        structure.Voices[displayName].Seasons[season] = episodes;
                        
                        _onLog($"AggregateSerialStructure: Created voice with {episodes.Count} episodes for season {season} in ashdi-vod");
                    }
                }
                
                // Edge Case 8: Перевірка наявності озвучок після агрегації
                if (!structure.Voices.Any())
                {
                    _onLog($"AggregateSerialStructure: No voices found after aggregation for {serialUrl}");
                    return null;
                }

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

        public async Task<List<SearchResult>> Search(string imdb_id, long kinopoisk_id, string title, string original_title, int year, string search_query)
        {
            string memKey = $"UaFlix:search:{kinopoisk_id}:{imdb_id}:{search_query}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchResult> res))
                return res;

            try
            {
                string filmTitle = !string.IsNullOrEmpty(original_title) ? original_title : (!string.IsNullOrEmpty(title) ? title : search_query);
                string searchUrl = $"{_init.host}/index.php?do=search&subaction=search&story={System.Web.HttpUtility.UrlEncode(filmTitle)}";
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) };

                var searchHtml = await Http.Get(searchUrl, headers: headers, proxy: _proxyManager.Get());
                var doc = new HtmlDocument();
                doc.LoadHtml(searchHtml);

                // Спробуємо різні селектори для пошуку результатів
                var filmNodes = doc.DocumentNode.SelectNodes("//a[contains(@class, 'sres-wrap')]") ??
                               doc.DocumentNode.SelectNodes("//div[contains(@class, 'sres-item')]//a") ??
                               doc.DocumentNode.SelectNodes("//div[contains(@class, 'search-result')]//a") ??
                               doc.DocumentNode.SelectNodes("//a[contains(@href, '/serials/') or contains(@href, '/films/')]");

                if (filmNodes == null || filmNodes.Count == 0)
                {
                    _onLog($"Search: No search results found with any selector for query: {filmTitle}");
                    return null;
                }

                res = new List<SearchResult>();
                foreach (var filmNode in filmNodes)
                {
                    try
                    {
                        var h2Node = filmNode.SelectSingleNode(".//h2") ?? filmNode.SelectSingleNode(".//h3");
                        if (h2Node == null) continue;

                        string filmUrl = filmNode.GetAttributeValue("href", "");
                        if (string.IsNullOrEmpty(filmUrl)) continue;

                        if (!filmUrl.StartsWith("http"))
                            filmUrl = _init.host + filmUrl;

                        // Спробуємо різні способи отримати рік
                        int filmYear = 0;
                        var descNode = filmNode.SelectSingleNode(".//div[contains(@class, 'sres-desc')]") ??
                                      filmNode.SelectSingleNode(".//span[contains(@class, 'year')]") ??
                                      filmNode.SelectSingleNode(".//*[contains(text(), '20')]");

                        if (descNode != null)
                        {
                            string yearText = descNode.InnerText ?? "";
                            var yearMatch = Regex.Match(yearText, @"(?:19|20)\d{2}");
                            if (yearMatch.Success)
                                int.TryParse(yearMatch.Value, out filmYear);
                        }

                        // Спробуємо різні селектори для постера
                        var posterNode = filmNode.SelectSingleNode(".//img[@src]") ??
                                        filmNode.SelectSingleNode(".//img[@data-src]") ??
                                        filmNode.SelectSingleNode(".//div[contains(@class, 'poster')]//img");

                        string posterUrl = posterNode?.GetAttributeValue("src", "") ?? posterNode?.GetAttributeValue("data-src", "");
                        if (!string.IsNullOrEmpty(posterUrl) && !posterUrl.StartsWith("http"))
                            posterUrl = _init.host + posterUrl;

                        res.Add(new SearchResult
                        {
                            Title = h2Node.InnerText.Trim(),
                            Url = filmUrl,
                            Year = filmYear,
                            PosterUrl = posterUrl
                        });

                        _onLog($"Search: Found result - {h2Node.InnerText.Trim()}, URL: {filmUrl}");
                    }
                    catch (Exception ex)
                    {
                        _onLog($"Search: Error processing film node: {ex.Message}");
                        continue;
                    }
                }

                if (res.Count > 0)
                {
                    _hybridCache.Set(memKey, res, cacheTime(20));
                    return res;
                }
            }
            catch (Exception ex)
            {
                _onLog($"UaFlix search error: {ex.Message}");
            }
            return null;
        }
        
        public async Task<FilmInfo> GetFilmInfo(string filmUrl)
        {
            string memKey = $"UaFlix:filminfo:{filmUrl}";
            if (_hybridCache.TryGetValue(memKey, out FilmInfo res))
                return res;

            try
            {
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) };
                var filmHtml = await Http.Get(filmUrl, headers: headers, proxy: _proxyManager.Get());
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
                var filmHtml = await Http.Get(filmUrl, headers: headers, proxy: _proxyManager.Get());
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

                var seasonTasks = seasonUrls.Select(url => Http.Get(url, headers: headers, proxy: _proxyManager.Get()).AsTask());
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
            var result = new Uaflix.Models.PlayResult() { streams = new List<(string, string)>() };
            try
            {
                string html = await Http.Get(url, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) }, proxy: _proxyManager.Get());
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var videoNode = doc.DocumentNode.SelectSingleNode("//video");
                if (videoNode != null)
                {
                    string videoUrl = videoNode.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(videoUrl))
                    {
                        result.streams.Add((videoUrl, "1080p"));
                        return result;
                    }
                }

                var iframe = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'video-box')]//iframe");
                if (iframe != null)
                {
                    string iframeUrl = iframe.GetAttributeValue("src", "").Replace("&", "&");
                    if (iframeUrl.StartsWith("//"))
                        iframeUrl = "https:" + iframeUrl;

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
                            var (file, voiceName) = await ParseAshdiVodEpisode(iframeUrl);
                            if (!string.IsNullOrEmpty(file))
                            {
                                result.streams.Add((file, "1080p"));
                            }
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

        async Task<List<(string link, string quality)>> ParseAllZetvideoSources(string iframeUrl)
        {
            var result = new List<(string link, string quality)>();
            var html = await Http.Get(iframeUrl, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", "https://zetvideo.net/") }, proxy: _proxyManager.Get());
            if (string.IsNullOrEmpty(html)) return result;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            var script = doc.DocumentNode.SelectSingleNode("//script[contains(text(), 'file:')]");
            if (script != null)
            {
                var match = Regex.Match(script.InnerText, @"file:\s*""([^""]+\.m3u8)");
                if (match.Success)
                {
                    result.Add((match.Groups[1].Value, "1080p"));
                    return result;
                }
            }

            var sourceNodes = doc.DocumentNode.SelectNodes("//source[contains(@src, '.m3u8')]");
            if (sourceNodes != null)
            {
                foreach (var node in sourceNodes)
                {
                    result.Add((node.GetAttributeValue("src", null), node.GetAttributeValue("label", null) ?? node.GetAttributeValue("res", null) ?? "1080p"));
                }
            }
            return result;
        }

        async Task<List<(string link, string quality)>> ParseAllAshdiSources(string iframeUrl)
        {
            var result = new List<(string link, string quality)>();
            var html = await Http.Get(iframeUrl, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", "https://ashdi.vip/") }, proxy: _proxyManager.Get());
             if (string.IsNullOrEmpty(html)) return result;
             
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
            return result;
        }
        
        async Task<SubtitleTpl?> GetAshdiSubtitles(string id)
        {
            var html = await Http.Get($"https://ashdi.vip/vod/{id}", headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", "https://ashdi.vip/") }, proxy: _proxyManager.Get());
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
                if (!st.IsEmpty())
                    return st;
            }
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