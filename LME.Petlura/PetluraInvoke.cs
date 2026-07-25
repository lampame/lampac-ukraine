using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
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

        private static readonly Regex Quality4kRegex = new Regex(
            @"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex QualityFhdRegex = new Regex(
            @"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex YearPrefixRegex = new Regex(
            @"(19|20)\d{2}",
            RegexOptions.Compiled
        );

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
        /// Знайти hdvbua embed tail через пошук на всіх джерелах.
        /// Повертає embed tail (embed/XXXX/XXXXX) або null.
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

                var tails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var errors = new List<string>();

                foreach (var source in sources)
                {
                    string tail = await SearchSingleSource(source, imdbId);
                    if (!string.IsNullOrEmpty(tail) && !tails.ContainsKey(tail))
                        tails[tail] = source;
                    else if (tail == null)
                        errors.Add(source);
                }

                if (tails.Count == 0)
                {
                    _onLog?.Invoke($"Petlura: жодне джерело не знайшло embed для {imdbId}");
                    _hybridCache.Set(memKey, null, CacheHelper.CacheTime(5, init: _init));
                    return null;
                }

                if (tails.Count > 1)
                {
                    // Різні джерела повернули різні embed — щось не так
                    var tailList = string.Join(", ", tails.Select(t => $"{t.Value}:{t.Key}"));
                    _onLog?.Invoke($"Petlura: різні embed від джерел для {imdbId}: {tailList}");
                    _hybridCache.Set(memKey, null, CacheHelper.CacheTime(5, init: _init));
                    return null;
                }

                string result = tails.Keys.First();
                _onLog?.Invoke($"Petlura: знайдено embed {result} для {imdbId}");
                _hybridCache.Set(memKey, result, CacheHelper.CacheTime(30, init: _init));
                return result;
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
        private async Task<string> FetchPlayerHtml(string embedTail)
        {
            if (string.IsNullOrWhiteSpace(embedTail))
                return null;

            string url = $"https://hdvbua.pro/{embedTail}";
            var headers = new List<HeadersModel>
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", "https://uaserials.fm/")
            };

            _onLog?.Invoke($"Petlura: запит до плеєра {url}");

            if (_httpHydra != null)
                return await _httpHydra.Get(url, newheaders: headers);

            return await Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());
        }

        /// <summary>
        /// Розпарсити HTML плеєра і отримати потоки для фільму.
        /// </summary>
        public async Task<List<StreamInfo>> GetMovieStreams(string embedTail)
        {
            if (string.IsNullOrWhiteSpace(embedTail))
                return null;

            string memKey = $"Petlura:movie:{embedTail}";
            if (_hybridCache.TryGetValue(memKey, out List<StreamInfo> cached))
                return cached;

            try
            {
                string html = await FetchPlayerHtml(embedTail);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var streams = new List<StreamInfo>();

                // 1. Спроба розпарсити PlayerJS JSON масив
                var jsonItems = ParsePlayerFileArray(html);
                if (jsonItems != null && jsonItems.Count > 0)
                {
                    int index = 1;
                    foreach (var item in jsonItems)
                    {
                        string fileUrl = item?.file;
                        if (string.IsNullOrWhiteSpace(fileUrl))
                            continue;

                        string rawTitle = item.title;
                        string title = BuildStreamTitle(rawTitle, fileUrl, index);
                        string quality = DetectQuality(rawTitle, fileUrl);

                        var stream = new StreamInfo
                        {
                            Title = title,
                            Url = fileUrl,
                            Quality = quality,
                            Subtitles = ParseSubtitles(item.subtitle)
                        };
                        streams.Add(stream);
                        index++;
                    }
                }

                // 2. Спроба знайти <source> теги
                if (streams.Count == 0)
                {
                    streams = ParseSourceTags(html);
                }

                // 3. Фолбек: m3u8 в тексті
                if (streams.Count == 0)
                {
                    var match = M3u8UrlRegex.Match(html ?? "");
                    if (match.Success)
                    {
                        string url = match.Groups[1].Value;
                        streams.Add(new StreamInfo
                        {
                            Title = "Video",
                            Url = url,
                            Quality = DetectQuality(null, url)
                        });
                    }
                }

                // 4. Дедуплікація
                streams = DedupeStreams(streams);

                if (streams.Count > 0)
                    _hybridCache.Set(memKey, streams, CacheHelper.CacheTime(30, init: _init));
                else
                    _hybridCache.Set(memKey, streams, CacheHelper.CacheTime(5, init: _init));

                return streams;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Petlura: помилка парсингу фільму {embedTail}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Розпарсити HTML плеєра і отримати серіали (озвучки → сезони → епізоди).
        /// </summary>
        public async Task<SerialInfo> GetSerialEpisodes(string embedTail)
        {
            if (string.IsNullOrWhiteSpace(embedTail))
                return null;

            string memKey = $"Petlura:serial:{embedTail}";
            if (_hybridCache.TryGetValue(memKey, out SerialInfo cached))
                return cached;

            try
            {
                string html = await FetchPlayerHtml(embedTail);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var jsonItems = ParsePlayerFileArray(html);
                if (jsonItems == null || jsonItems.Count == 0)
                    return null;

                var serialInfo = new SerialInfo();

                foreach (var item in jsonItems)
                {
                    string voiceName = item?.title?.Trim();
                    if (string.IsNullOrWhiteSpace(voiceName))
                        continue;

                    // Спроба розпарсити folder як JSON масив сезонів
                    if (item?.folder != null && item.folder.Value.ValueKind == JsonValueKind.Array)
                    {
                        var seasons = ParseFolderSeasons(item.folder.Value);
                        if (seasons != null && seasons.Count > 0)
                        {
                            var allEpisodes = new List<EpisodeInfo>();
                            foreach (var season in seasons)
                            {
                                foreach (var ep in season.Episodes)
                                {
                                    if (!string.IsNullOrWhiteSpace(ep.Url))
                                    {
                                        allEpisodes.Add(new EpisodeInfo
                                        {
                                            Episode = ep.Episode,
                                            Title = ep.Title ?? $"Епізод {ep.Episode}",
                                            Url = ep.Url
                                        });
                                    }
                                }
                            }

                            if (allEpisodes.Count > 0)
                            {
                                serialInfo.Voices.Add(new VoiceEpisodes
                                {
                                    Name = voiceName,
                                    Episodes = allEpisodes
                                });
                            }
                            continue;
                        }
                    }

                    // Якщо немає folder, пробуємо використати file як пряме посилання
                    string fileUrl = item?.file?.Trim();
                    if (!string.IsNullOrWhiteSpace(fileUrl))
                    {
                        serialInfo.Voices.Add(new VoiceEpisodes
                        {
                            Name = voiceName,
                            Episodes = new List<EpisodeInfo>
                            {
                                new EpisodeInfo
                                {
                                    Episode = 1,
                                    Title = voiceName,
                                    Url = fileUrl
                                }
                            }
                        });
                    }

                    // Беремо всі епізоди з усіх сезонів для цієї озвучки
                    var allEpisodes = new List<EpisodeInfo>();
                    foreach (var season in seasons)
                    {
                        if (season.Episodes == null)
                            continue;

                        foreach (var ep in season.Episodes)
                        {
                            if (!string.IsNullOrWhiteSpace(ep.Url))
                            {
                                allEpisodes.Add(new EpisodeInfo
                                {
                                    Episode = ep.Episode,
                                    Title = ep.Title ?? $"Епізод {ep.Episode}",
                                    Url = ep.Url
                                });
                            }
                        }
                    }

                    if (allEpisodes.Count > 0)
                    {
                        serialInfo.Voices.Add(new VoiceEpisodes
                        {
                            Name = voiceName,
                            Episodes = allEpisodes
                        });
                    }
                }

                if (serialInfo.Voices.Count > 0)
                    _hybridCache.Set(memKey, serialInfo, CacheHelper.CacheTime(30, init: _init));

                return serialInfo;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Petlura: помилка парсингу серіалу {embedTail}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримати епізоди для конкретного сезону та озвучки.
        /// </summary>
        public async Task<List<EpisodeInfo>> GetSeasonEpisodes(string embedTail, int seasonNumber, string voiceName)
        {
            var serialInfo = await GetSerialEpisodes(embedTail);
            if (serialInfo == null)
                return null;

            // Шукаємо голос
            var voice = serialInfo.Voices.FirstOrDefault(v =>
                string.Equals(v.Name, voiceName, StringComparison.OrdinalIgnoreCase));

            if (voice == null && serialInfo.Voices.Count > 0)
                voice = serialInfo.Voices[0];

            if (voice == null)
                return null;

            return voice.Episodes;
        }

        /// <summary>
        /// Розпарсити JSON масив з PlayerJS: file:'[...]'.
        /// </summary>
        private List<PlayerFileItem> ParsePlayerFileArray(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            string jsonArrayStr = AshdiParser.ExtractPlayerFileArray(html);
            if (string.IsNullOrWhiteSpace(jsonArrayStr))
            {
                // Фолбек: пошук file: через regex
                var match = Regex.Match(html, @"file\s*:\s*['""](\[.+?\])['""]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                    jsonArrayStr = match.Groups[1].Value;
            }

            if (string.IsNullOrWhiteSpace(jsonArrayStr))
                return null;

            try
            {
                jsonArrayStr = jsonArrayStr
                    .Replace("\\'", "'")
                    .Replace("\\\"", "\"")
                    .Replace("\\/", "/");

                jsonArrayStr = System.Net.WebUtility.HtmlDecode(jsonArrayStr);

                var items = JsonSerializer.Deserialize<List<PlayerFileItem>>(jsonArrayStr, JsonOptions);
                return items?.Where(i => i != null).ToList();
            }
            catch (JsonException ex)
            {
                _onLog?.Invoke($"Petlura: помилка парсингу JSON плеєра: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Розпарсити JsonElement (масив) як сезони з епізодами.
        /// Структура: [{"title":"Сезон 1","folder":[{"title":"1","file":"url1","subtitle":"..."}]}]
        /// </summary>
        private List<SeasonInfo> ParseFolderSeasons(JsonElement folderElement)
        {
            if (folderElement.ValueKind != JsonValueKind.Array)
                return null;

            var seasons = new List<SeasonInfo>();

            foreach (var seasonElem in folderElement.EnumerateArray())
            {
                string seasonTitle = seasonElem.TryGetProperty("title", out var st) ? st.GetString() : null;
                if (string.IsNullOrWhiteSpace(seasonTitle))
                    continue;

                // Витягуємо номер сезону з назви "Сезон N"
                int seasonNumber = 0;
                var seasonMatch = Regex.Match(seasonTitle, @"Сезон\s+(\d+)", RegexOptions.IgnoreCase);
                if (seasonMatch.Success)
                    int.TryParse(seasonMatch.Groups[1].Value, out seasonNumber);

                // Парсимо епізоди
                var episodes = new List<EpisodeInfo>();
                if (seasonElem.TryGetProperty("folder", out var episodesElem) && episodesElem.ValueKind == JsonValueKind.Array)
                {
                    int epIndex = 1;
                    foreach (var epElem in episodesElem.EnumerateArray())
                    {
                        string epTitle = epElem.TryGetProperty("title", out var et) ? et.GetString() : null;
                        string epFile = epElem.TryGetProperty("file", out var ef) ? ef.GetString() : null;

                        if (string.IsNullOrWhiteSpace(epFile))
                            continue;

                        episodes.Add(new EpisodeInfo
                        {
                            Episode = epIndex,
                            Title = epTitle ?? $"Епізод {epIndex}",
                            Url = epFile
                        });
                        epIndex++;
                    }
                }

                if (episodes.Count > 0)
                {
                    seasons.Add(new SeasonInfo
                    {
                        SeasonNumber = seasonNumber,
                        Voices = new List<VoiceEpisodes>
                        {
                            new VoiceEpisodes
                            {
                                Name = seasonTitle,
                                Episodes = episodes
                            }
                        }
                    });
                }
            }

            return seasons.Count > 0 ? seasons : null;
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
        /// Парсинг <source> тегів з HTML.
        /// </summary>
        private List<StreamInfo> ParseSourceTags(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return new List<StreamInfo>();

            var streams = new List<StreamInfo>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sourceNodes = doc.DocumentNode.SelectNodes("//source");
            if (sourceNodes != null)
            {
                foreach (var node in sourceNodes)
                {
                    string src = node.GetAttributeValue("src", "");
                    if (string.IsNullOrWhiteSpace(src) || !src.Contains(".m3u8"))
                        continue;

                    string quality = node.GetAttributeValue("label", "") ??
                                     node.GetAttributeValue("res", "") ?? "";
                    if (string.IsNullOrWhiteSpace(quality))
                        quality = DetectQuality(null, src);

                    streams.Add(new StreamInfo
                    {
                        Title = string.IsNullOrWhiteSpace(quality) ? "Video" : quality,
                        Url = src,
                        Quality = string.IsNullOrWhiteSpace(quality) ? "auto" : quality
                    });
                }
            }

            return streams;
        }

        /// <summary>
        /// Визначення якості з назви або URL.
        /// </summary>
        private string DetectQuality(string title, string url)
        {
            string text = $"{title ?? ""} {url ?? ""}";
            if (Quality4kRegex.IsMatch(text))
                return "2160p";
            if (QualityFhdRegex.IsMatch(text))
                return "1080p";
            return "auto";
        }

        /// <summary>
        /// Побудова назви стріму для фільму.
        /// </summary>
        private string BuildStreamTitle(string rawTitle, string streamUrl, int index)
        {
            string title = rawTitle?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(title))
                return $"Варіант {index}";

            // Прибрати рік на початку
            title = Regex.Replace(title, @"\s+", " ").Trim();
            int sepIndex = title.LastIndexOf(" - ", StringComparison.Ordinal);
            if (sepIndex > 0 && sepIndex < title.Length - 3)
            {
                string prefix = title.Substring(0, sepIndex).Trim();
                string suffix = title.Substring(sepIndex + 3).Trim();
                if (!string.IsNullOrEmpty(suffix) && YearPrefixRegex.IsMatch(prefix))
                    title = suffix;
            }

            // Додати тег якості
            string tag = QualityHelper.DetectQuality($"{title} {streamUrl}");
            if (!string.IsNullOrEmpty(tag) && !title.StartsWith("[4K]") && !title.StartsWith("[FHD]"))
                title = $"{tag} {title}";

            return title;
        }

        /// <summary>
        /// Дедуплікація стрімів за URL.
        /// </summary>
        private List<StreamInfo> DedupeStreams(List<StreamInfo> streams)
        {
            if (streams == null || streams.Count == 0)
                return streams ?? new List<StreamInfo>();

            var deduped = new List<StreamInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var stream in streams)
            {
                string url = stream?.Url?.Trim() ?? "";
                if (string.IsNullOrEmpty(url) || seen.Contains(url))
                    continue;

                seen.Add(url);
                deduped.Add(stream);
            }

            return deduped;
        }

        /// <summary>
        /// Парсинг субтитрів з формату [lang]url.
        /// </summary>
        private List<SubtitleInfo> ParseSubtitles(string subtitleValue)
        {
            var subtitles = new List<SubtitleInfo>();
            if (string.IsNullOrWhiteSpace(subtitleValue))
                return subtitles;

            var matches = Regex.Matches(subtitleValue, @"\[([^\]]+)\]([^,]+)");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (!match.Success)
                    continue;

                string lang = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
                string url = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
                if (string.IsNullOrEmpty(lang) || string.IsNullOrEmpty(url))
                    continue;

                subtitles.Add(new SubtitleInfo { Lang = lang, Url = url });
            }

            return subtitles;
        }
    }

}
