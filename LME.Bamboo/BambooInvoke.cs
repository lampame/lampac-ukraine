using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using LME.Bamboo.Models;
using HtmlAgilityPack;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace LME.Bamboo
{
    /// <summary>
    /// Портук Python-джерела bambooua.py на актуальну верстку BambooUA (2026-09).
    ///
    /// Ключові відмінності від старої версії:
    ///  - картки пошуку: article.swiper-slide / a.link-title / h2.label-3 (було li.slide-item / a.hover-buttons / h6);
    ///  - контент береться з Playerjs-плейлиста (const playlist = [...] або ashdi file: '[…]'),
    ///    а не з span[data-file] / div.mt-4 — цих конструкцій на сайті більше немає;
    ///  - мультиозвучка: голоси беруться з плейлиста, а не з двох полів Sub/Dub;
    ///  - ashdi.transl НЕ переноситься: обрізаний transl дає "<center>not found</center>" —
    ///    запит іде без нього, один запит на ashdi-id повертає всі голоси;
    ///  - внутрішні запити йдуть БЕЗ APN-обгортки (APN накладається лише на стрім у контролері).
    /// </summary>
    public class BambooInvoke
    {
        private const int MaxSearchPages = 3;
        private const int MaxPlaylistDepth = 4;
        private const int DefaultPageSize = 20;

        private static readonly Regex NativePlaylistRegex = new Regex(@"(?:const|var|let)\s+playlist\s*=\s*(\[.*?\])\s*;", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex PlayerJsFileRegex = new Regex(@"file\s*:\s*'(\[\{.*\}\])'", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex AshdiIframeRegex = new Regex(@"<iframe\b[^>]*src=[""'](?<url>https?://ashdi\.vip/(?:serial|vod)/[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TranslRegex = new Regex(@"([?&])transl=[^&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PageRangeRegex = new Regex(@"Результати запиту\s+(\d+)\s*-\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex YearRegex = new Regex(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
        private static readonly Regex SeasonRegex = new Regex(@"(?:сезон|season)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EpisodeNumberRegex = new Regex(@"(\d+)", RegexOptions.Compiled);
        private static readonly Regex NoPosterRegex = new Regex(@"no-poster", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        public BambooInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        #region Search

        /// <summary>
        /// Пошук: original_title → title (fallback), до 3 сторінок через search_start (1-based).
        /// </summary>
        public async Task<List<SearchResult>> Search(string title, string original_title)
        {
            var queries = new[] { original_title, title }
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Select(q => q.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var query in queries)
            {
                var results = await SearchQuery(query);
                if (results.Count > 0)
                    return results;
            }

            return null;
        }

        private async Task<List<SearchResult>> SearchQuery(string query)
        {
            string memKey = $"lme_bamboo:search:{query}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchResult> cached))
                return cached;

            var results = new List<SearchResult>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int page = 1; page <= MaxSearchPages; page++)
            {
                string searchUrl = BuildSearchUrl(query, page);
                string html = await Fetch(searchUrl, referer: _init.host);
                if (string.IsNullOrEmpty(html))
                    break;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var nodes = doc.DocumentNode.SelectNodes("//article[contains(@class,'swiper-slide')]");
                if (nodes == null || nodes.Count == 0)
                    break;

                int added = 0;
                foreach (var node in nodes)
                {
                    var item = ParseSearchItem(node);
                    if (item == null || !seen.Add(item.Url))
                        continue;

                    results.Add(item);
                    added++;
                }

                _onLog?.Invoke($"lme_bamboo search: page={page}, cards={nodes.Count}, added={added}, total={results.Count}");

                // Сторінка неповна або нічого нового — далі йти нема сенсу.
                if (added == 0 || nodes.Count < GetPageSizeHint(html))
                    break;
            }

            if (results.Count > 0)
                _hybridCache.Set(memKey, results, CacheHelper.CacheTime(20, init: _init));

            return results;
        }

        private string BuildSearchUrl(string query, int page)
        {
            string url = $"{_init.host}/index.php?do=search&subaction=search&story={HttpUtility.UrlEncode(query)}";
            // search_start — 1-based індекс сторінки, не офсет елементів.
            return page > 1 ? $"{url}&search_start={page}" : url;
        }

        private SearchResult ParseSearchItem(HtmlNode node)
        {
            string title = CleanText(node.SelectSingleNode(".//h2[contains(@class,'label-3')]")?.InnerText)
                           ?? CleanText(node.SelectSingleNode(".//h2")?.InnerText);

            if (string.IsNullOrEmpty(title))
                return null;

            string href = NormalizeUrl(node.SelectSingleNode(".//a[contains(@class,'link-title')]")?.GetAttributeValue("href", ""));
            if (string.IsNullOrEmpty(href) || !IsMediaUrl(href))
                return null;

            var genres = ExtractGenres(node);
            int episodesHint = ExtractEpisodeNumber(CleanText(node.SelectSingleNode(".//div[contains(@class,'date-anounce')]")?.InnerText));

            return new SearchResult
            {
                Title = title,
                Url = href,
                Poster = ExtractPoster(node),
                Genres = genres,
                EpisodesHint = episodesHint,
                Type = DetectType(href, genres, episodesHint)
            };
        }

        private static List<string> ExtractGenres(HtmlNode node)
        {
            string raw = CleanText(node.SelectSingleNode(".//span[contains(@class,'year')]")?.InnerText);
            if (string.IsNullOrEmpty(raw))
                return new List<string>();

            return raw.Split('/')
                .Select(g => g.Trim())
                .Where(g => !string.IsNullOrEmpty(g))
                .ToList();
        }

        /// <summary>
        /// Тип контенту не виводиться з URL (/dorama/ містить і фільми, /zhanr/ відсутній),
        /// тому читаємо жанри: "Дорама" → серіал, "Кіно" → фільм. Лічильник "Суб. N" — вирішальний хінт на серіал.
        /// </summary>
        private static string DetectType(string href, List<string> genres, int episodesHint)
        {
            if (episodesHint > 0)
                return "series";

            if (genres != null)
            {
                if (genres.Any(g => g.IndexOf("дорама", StringComparison.OrdinalIgnoreCase) >= 0))
                    return "series";
                if (genres.Any(g => g.IndexOf("кіно", StringComparison.OrdinalIgnoreCase) >= 0))
                    return "movie";
            }

            if (href.IndexOf("ashdi.vip/serial/", StringComparison.OrdinalIgnoreCase) >= 0
                || href.IndexOf("/serial", StringComparison.OrdinalIgnoreCase) >= 0
                || href.IndexOf("/series", StringComparison.OrdinalIgnoreCase) >= 0)
                return "series";

            if (href.IndexOf("/cinema/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "movie";

            return string.Empty;
        }

        /// <summary>Новини/блог не мають плеєра і ніколи не резолвляться — відкидаємо одразу.</summary>
        private static bool IsMediaUrl(string url)
        {
            string lowered = url.ToLowerInvariant();
            return lowered.IndexOf("/blog/", StringComparison.Ordinal) < 0
                   && lowered.IndexOf("/news/", StringComparison.Ordinal) < 0;
        }

        private string ExtractPoster(HtmlNode node)
        {
            var scope = node.SelectSingleNode(".//div[contains(@class,'poster')]") ?? node;
            var images = scope.SelectNodes(".//img");
            if (images == null)
                return string.Empty;

            foreach (var img in images)
            {
                if (NoPosterRegex.IsMatch(img.GetAttributeValue("class", "")))
                    continue;

                string src = img.GetAttributeValue("src", "");
                if (string.IsNullOrEmpty(src))
                    src = img.GetAttributeValue("data-src", "");

                if (!string.IsNullOrEmpty(src))
                    return NormalizeUrl(src);
            }

            return string.Empty;
        }

        private static int GetPageSizeHint(string html)
        {
            var match = PageRangeRegex.Match(html ?? string.Empty);
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out int from)
                && int.TryParse(match.Groups[2].Value, out int to))
            {
                int size = to - from + 1;
                if (size > 0)
                    return size;
            }

            return DefaultPageSize;
        }

        #endregion

        #region Series

        /// <summary>
        /// Епізоди серіалу, згруповані за озвучками.
        /// ashdi-джерела читаємо БЕЗ transl (обрізаний transl → "not found"), один запит на id
        /// повертає всі голоси; інакше — нативний плейлист сторінки.
        /// </summary>
        public async Task<SeriesEpisodes> GetSeriesEpisodes(string href)
        {
            if (string.IsNullOrEmpty(href))
                return null;

            string memKey = $"lme_bamboo:series:{href}";
            if (_hybridCache.TryGetValue(memKey, out SeriesEpisodes cached))
                return cached;

            try
            {
                string html = await Fetch(href, referer: _init.host);
                if (string.IsNullOrEmpty(html))
                    return null;

                var voices = new List<PlaylistVoice>();

                // 1) ashdi: один запит на унікальний id повертає всі озвучки серіалу.
                foreach (var src in ExtractAshdiSources(html))
                {
                    string ashdiHtml = await Fetch(src, referer: _init.host);
                    var ashdiVoices = PlaylistVoices(NativePlaylist(ashdiHtml));
                    if (ashdiVoices.Count > 0)
                    {
                        voices.AddRange(ashdiVoices);
                        break;
                    }
                }

                // 2) Нативний плеєр сторінки.
                if (voices.Count == 0)
                    voices.AddRange(PlaylistVoices(NativePlaylist(html)));

                if (voices.Count == 0)
                {
                    _onLog?.Invoke($"lme_bamboo series: плейлист не знайдено для {href}");
                    return null;
                }

                var result = new SeriesEpisodes();
                foreach (var voice in voices)
                {
                    var episodes = Dedupe(voice.Episodes);
                    if (episodes.Count == 0)
                        continue;

                    result.Voices.Add(new PlaylistVoice
                    {
                        Key = MakeVoiceKey(voice.Name, result.Voices.Count),
                        Name = voice.Name,
                        Episodes = episodes
                    });
                }

                if (result.Voices.Count == 0)
                    return null;

                _onLog?.Invoke($"lme_bamboo series: {href} → voices={result.Voices.Count}, " +
                               $"episodes=[{string.Join(", ", result.Voices.Select(v => $"{v.Name}:{v.Episodes.Count}"))}]");

                _hybridCache.Set(memKey, result, CacheHelper.CacheTime(10, init: _init));
                return result;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"lme_bamboo series error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Унікальні ashdi-плеєри без параметра transl.
        /// Усі вкладки серіалу вказують на той самий ashdi id, тому дублікати прибираємо,
        /// а transl зрізаємо — ashdi відповідає на нього "not found" і віддає 26 байт заглушки.
        /// </summary>
        private List<string> ExtractAshdiSources(string html)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in AshdiIframeRegex.Matches(html ?? string.Empty))
            {
                string src = StripTransl(NormalizeUrl(WebUtility.HtmlDecode(match.Groups["url"].Value)));
                if (string.IsNullOrEmpty(src) || !seen.Add(src))
                    continue;

                result.Add(src);
            }

            return result;
        }

        private static string StripTransl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            string cleaned = TranslRegex.Replace(url, string.Empty);
            return cleaned.Replace("?&", "?").Replace("&&", "&").TrimEnd('?', '&');
        }

        #endregion

        #region Movie

        /// <summary>
        /// Потоки фільму. Для ashdi додаємо multivoice — без нього приховані зайві дубляжі.
        /// </summary>
        public async Task<List<StreamInfo>> GetMovieStreams(string href)
        {
            if (string.IsNullOrEmpty(href))
                return null;

            string memKey = $"lme_bamboo:movie:{href}";
            if (_hybridCache.TryGetValue(memKey, out List<StreamInfo> cached))
                return cached;

            try
            {
                string html = await Fetch(href, referer: _init.host);
                if (string.IsNullOrEmpty(html))
                    return null;

                var streams = new List<StreamInfo>();

                // Нативний плеєр: leaf верхнього рівня = окремий голос фільму.
                foreach (var ep in WalkPlaylist(NativePlaylist(html), includeMovies: true))
                {
                    streams.Add(new StreamInfo
                    {
                        Title = string.IsNullOrEmpty(ep.Title) ? "Основне джерело" : ep.Title,
                        Url = ep.Url,
                        Voice = ep.Voice,
                        Subtitle = ep.Subtitle
                    });
                }

                if (streams.Count == 0)
                {
                    foreach (var src in ExtractAshdiSources(html))
                    {
                        string ashdiHtml = await Fetch(ApnExtensions.WithAshdiMultivoice(src), referer: "https://ashdi.vip/");
                        foreach (var ep in WalkPlaylist(NativePlaylist(ashdiHtml), includeMovies: true))
                        {
                            streams.Add(new StreamInfo
                            {
                                Title = string.IsNullOrEmpty(ep.Title) ? "Основне джерело" : ep.Title,
                                Url = ep.Url,
                                Voice = ep.Voice,
                                Subtitle = ep.Subtitle
                            });
                        }

                        if (streams.Count > 0)
                            break;
                    }
                }

                if (streams.Count == 0)
                    streams = StreamsFromPlayerBlocks(html);

                // Нативний плеєр і ashdi часто пропонують той самий дубляж — дедупимо за голосом.
                var unique = new List<StreamInfo>();
                var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stream in streams)
                {
                    if (string.IsNullOrEmpty(stream.Url))
                        continue;

                    string label = !string.IsNullOrEmpty(stream.Voice) ? stream.Voice : stream.Title;
                    if (!seenTitles.Add(label))
                        continue;

                    stream.Voice = label;
                    unique.Add(stream);
                }

                if (unique.Count == 0)
                    return null;

                _onLog?.Invoke($"lme_bamboo movie: {href} → streams={unique.Count} " +
                               $"[{string.Join(", ", unique.Select(s => s.Voice))}]");

                _hybridCache.Set(memKey, unique, CacheHelper.CacheTime(10, init: _init));
                return unique;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"lme_bamboo movie error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Аварійний варіант: читаємо вкладки плеєра напряму, пропускаючи YouTube-трейлери.
        /// </summary>
        private async Task<List<StreamInfo>> StreamsFromPlayerBlocks(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html ?? string.Empty);

            var streams = new List<StreamInfo>();
            var blocks = doc.DocumentNode.SelectNodes("//div[contains(@class,'player-content')]");
            if (blocks == null)
                return streams;

            foreach (var block in blocks)
            {
                string inner = block.InnerHtml;
                if (inner.IndexOf("youtube", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var parsed = new List<EpisodeInfo>();

                foreach (Match match in AshdiIframeRegex.Matches(inner))
                {
                    string src = NormalizeUrl(WebUtility.HtmlDecode(match.Groups["url"].Value));
                    if (string.IsNullOrEmpty(src))
                        continue;

                    string ashdiHtml = await Fetch(src, referer: "https://ashdi.vip/");
                    var episodes = WalkPlaylist(NativePlaylist(ashdiHtml), includeMovies: true);
                    if (episodes.Count > 0)
                    {
                        parsed.AddRange(episodes);
                        continue;
                    }

                    parsed.Add(new EpisodeInfo { Title = block.GetAttributeValue("id", "Основне джерело"), Url = src });
                }

                foreach (var ep in parsed)
                {
                    streams.Add(new StreamInfo
                    {
                        Title = string.IsNullOrEmpty(ep.Title) ? "Основне джерело" : ep.Title,
                        Url = ep.Url,
                        Voice = VoiceLabel(ep.Title, null)
                    });
                }
            }

            return streams;
        }

        #endregion

        #region Playlist

        /// <summary>
        /// Playerjs-плейлист сторінки: спершу нативний const/var/let playlist, потім playerjs file: '[…]'.
        /// </summary>
        public static JsonArray NativePlaylist(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            string text = WebUtility.HtmlDecode(html);

            var match = NativePlaylistRegex.Match(text);
            if (!match.Success)
                match = PlayerJsFileRegex.Match(text);

            if (!match.Success)
                return null;

            return ParseJsonArray(match.Groups[1].Value);
        }

        private static JsonArray ParseJsonArray(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string text = WebUtility.HtmlDecode(raw).Trim()
                .Replace("\\/", "/")
                .Replace("\\'", "'")
                .Replace("\\\"", "\"");

            // JSON може бути обгорнутий у зайві дужки — беремо внутрішній масив.
            int start = text.IndexOf('[');
            int end = text.LastIndexOf(']');
            if (start >= 0 && end > start)
                text = text.Substring(start, end - start + 1);

            try
            {
                return JsonNode.Parse(text) as JsonArray;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Розкладає плейлист на голоси: title верхнього рівня = озвучка, folder = серії/епізоди.
        /// Leaf на depth 0 — це самостійний файл фільму, а не епізод (includeMovies вирішує).
        /// </summary>
        public static List<PlaylistVoice> PlaylistVoices(JsonArray payload)
        {
            var voices = new List<PlaylistVoice>();
            if (payload == null)
                return voices;

            foreach (var entry in payload)
            {
                if (entry is not JsonObject obj)
                    continue;

                var episodes = WalkPlaylist(new JsonArray(obj.DeepClone()), includeMovies: false);
                if (episodes.Count == 0)
                    continue;

                string name = CleanText(GetString(obj, "title"));
                voices.Add(new PlaylistVoice { Key = name, Name = name, Episodes = episodes });
            }

            return voices;
        }

        /// <summary>
        /// Рекурсивний обхід плейлиста: voice(depth 0) → folder → folder → leaf(file).
        /// </summary>
        public static List<EpisodeInfo> WalkPlaylist(JsonArray payload, string voice = null, bool includeMovies = true)
        {
            var episodes = new List<EpisodeInfo>();
            Walk(payload, voice, episodes, 0, includeMovies);
            return episodes;
        }

        private static void Walk(JsonArray payload, string voice, List<EpisodeInfo> episodes, int depth, bool includeMovies)
        {
            if (payload == null || depth > MaxPlaylistDepth)
                return;

            foreach (var entry in payload)
            {
                if (entry is not JsonObject obj)
                    continue;

                string title = CleanText(GetString(obj, "title"));

                if (obj["folder"] is JsonArray folder)
                {
                    // На depth 0 title — це озвучка, далі title — це сезон.
                    string nestedVoice = depth == 0 ? (string.IsNullOrEmpty(title) ? voice : title) : voice;
                    Walk(folder, nestedVoice, episodes, depth + 1, includeMovies);
                    continue;
                }

                string file = NormalizeUrl(GetString(obj, "file"));
                if (string.IsNullOrEmpty(file))
                    continue;

                string subtitle = GetString(obj, "subtitle");

                // Leaf на depth 0 — самостійний файл фільму, а не епізод.
                if (depth == 0)
                {
                    if (includeMovies)
                    {
                        episodes.Add(new EpisodeInfo
                        {
                            Title = string.IsNullOrEmpty(title) ? "Основне джерело" : title,
                            Url = file,
                            Season = 1,
                            Voice = VoiceLabel(title, obj),
                            Subtitle = subtitle
                        });
                    }

                    continue;
                }

                int number = ExtractEpisodeNumber(title);
                if (number <= 0)
                    number = episodes.Count + 1;

                episodes.Add(new EpisodeInfo
                {
                    Title = string.IsNullOrEmpty(title) ? $"Серія {number}" : title,
                    Url = file,
                    Season = ToSeason(title),
                    Episode = number,
                    Voice = voice,
                    Subtitle = subtitle
                });
            }
        }

        /// <summary>
        /// Читабельна назва голосу. Частина ashdi-leaf'ів має title = лише id ("87") — це не показати,
        /// тому йдемо далі: id → "Озвучка N".
        /// </summary>
        private static string VoiceLabel(string title, JsonObject entry)
        {
            string label = CleanText(title);
            if (!string.IsNullOrEmpty(label) && !label.All(char.IsDigit))
                return label;

            string entryId = CleanText(GetString(entry, "id"));
            if (!string.IsNullOrEmpty(entryId) && !entryId.All(char.IsDigit))
                return entryId;

            string suffix = !string.IsNullOrEmpty(label) ? label : entryId;
            return string.IsNullOrEmpty(suffix) ? string.Empty : $"Озвучка {suffix}";
        }

        private static int ToSeason(string title)
        {
            var match = SeasonRegex.Match(title ?? string.Empty);
            return match.Success && int.TryParse(match.Groups[1].Value, out int season) ? season : 1;
        }

        private static List<EpisodeInfo> Dedupe(List<EpisodeInfo> episodes)
        {
            var result = new List<EpisodeInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var ep in episodes)
            {
                if (string.IsNullOrEmpty(ep.Url) || !seen.Add(ep.Url))
                    continue;

                result.Add(ep);
            }

            return result;
        }

        #endregion

        #region Helpers

        private static string GetString(JsonObject obj, string key)
        {
            if (obj == null || obj[key] is not JsonValue value)
                return string.Empty;

            return value.TryGetValue<string>(out string text) ? text ?? string.Empty : value.ToString();
        }

        /// <summary>Слаг голосу для параметра t — стабільний між запитами (index — лише фолбек).</summary>
        public static string MakeVoiceKey(string name, int index)
        {
            if (string.IsNullOrWhiteSpace(name))
                return $"v{index + 1}";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim().ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '-');

            string key = WhitespaceRegex.Replace(sb.ToString(), "-").Trim('-');
            while (key.Contains("--"))
                key = key.Replace("--", "-");

            return key.Length > 0 ? key : $"v{index + 1}";
        }

        /// <summary>
        /// Абсолютний URL для власних запитів. APN тут НЕ застосовується —
        /// інакше внутрішні fetch-и йдуть через проксі і ламаються.
        /// APN накладається лише на стрім у контролері (StreamHelper.BuildStreamUrl).
        /// </summary>
        public string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;

            string value = HttpUtility.HtmlDecode(url.Trim());
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.StartsWith("//", StringComparison.Ordinal))
                return $"https:{value}";

            if (value.StartsWith("/", StringComparison.Ordinal))
                return $"{_init.host.TrimEnd('/')}{value}";

            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? value
                : string.Empty;
        }

        private async Task<string> Fetch(string url, string referer)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"),
                new HeadersModel("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                new HeadersModel("Accept-Language", "uk-UA,uk;q=0.9,en-US;q=0.8,en;q=0.7")
            };

            if (!string.IsNullOrEmpty(referer))
                headers.Add(new HeadersModel("Referer", referer));

            try
            {
                string content = await HttpHelper.GetAsync(_httpHydra, _init, url, headers, _proxyManager);
                _onLog?.Invoke($"lme_bamboo fetch {url} -> len={content?.Length ?? 0}");
                return content;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"lme_bamboo fetch error ({url}): {ex.Message}");
                return null;
            }
        }

        private static int ExtractEpisodeNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return 0;

            var match = EpisodeNumberRegex.Match(title);
            return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : 0;
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return WhitespaceRegex.Replace(WebUtility.HtmlDecode(value)).Trim();
        }

        #endregion
    }
}
