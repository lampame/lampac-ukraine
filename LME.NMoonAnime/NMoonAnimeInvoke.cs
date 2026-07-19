using LME.NMoonAnime.Models;
using LME.Common.Playerjs;
using LME.Common.Engine;
using LME.Shared.Engine;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace LME.NMoonAnime
{
    public class NMoonAnimeInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;
        private readonly bool _nocache;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex ReSeason = new Regex(@"(?:season|сезон)\s*(\d+)|(\d+)\s*(?:season|сезон)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ReEpisode = new Regex(@"(?:episode|серія|серия|епізод|ep)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string ApiToken => string.IsNullOrWhiteSpace(_init.token) ? ModInit.DefaultApiKey : _init.token;

        public NMoonAnimeInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null, bool nocache = false)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
            _nocache = nocache;
        }

        #region Пошук тайтлів (новий пайплайн)

        /// <summary>
        /// Основний пайплайн пошуку: haglund (IMDB→MAL) → moonanime API → fallback за назвою.
        /// </summary>
        public async Task<List<NMoonAnimeSeasonRef>> Search(string imdbId, string malId, string title, string originalTitle, int year, int serial)
        {
            string memKey = $"NMoonAnime:search:{imdbId}:{malId}:{title}:{originalTitle}:{year}:{serial}";
            if (!_nocache && _hybridCache.TryGetValue(memKey, out List<NMoonAnimeSeasonRef> cached))
                return cached;

            try
            {
                // Етап 1: Якщо є mal_id напряму — шукаємо за ним
                if (!string.IsNullOrWhiteSpace(malId))
                {
                    var fromMal = await SearchByMalId(malId);
                    if (fromMal != null && fromMal.Count > 0)
                    {
                        _hybridCache.Set(memKey, fromMal, CacheHelper.CacheTime(10, init: _init));
                        return fromMal;
                    }
                }

                // Етап 2: IMDB → haglund → MAL ID → moonanime
                if (!string.IsNullOrWhiteSpace(imdbId))
                {
                    var fromImdb = await ResolveFromImdb(imdbId, year, serial);
                    if (fromImdb != null && fromImdb.Count > 0)
                    {
                        _hybridCache.Set(memKey, fromImdb, CacheHelper.CacheTime(10, init: _init));
                        return fromImdb;
                    }
                }

                // Етап 3: Fallback — прямий пошук за назвою
                var fromTitle = await SearchByTitle(originalTitle, title, year, serial, limit: 5);
                if (fromTitle != null && fromTitle.Count > 0)
                {
                    _hybridCache.Set(memKey, fromTitle, CacheHelper.CacheTime(10, init: _init));
                    return fromTitle;
                }
            }
            catch (Exception ex)
            {
                _onLog($"NMoonAnime: помилка пошуку - {ex.Message}");
            }

            return new List<NMoonAnimeSeasonRef>();
        }

        /// <summary>
        /// Конвертація IMDB → MAL через haglund, потім отримання moonanime ID.
        /// </summary>
        private async Task<List<NMoonAnimeSeasonRef>> ResolveFromImdb(string imdbId, int year, int serial)
        {
            string memKey = $"NMoonAnime:haglund:{imdbId}";
            return await SingleFlightCache.GetOrCreateAsync<List<NMoonAnimeSeasonRef>>(memKey, _hybridCache, async token =>
            {
                if (_hybridCache.TryGetValue(memKey, out List<NMoonAnimeSeasonRef> hit))
                    return hit;

                try
                {
                    string haglundUrl = $"{ModInit.HaglundHost}/api/v2/imdb?id={imdbId}";
                    _onLog($"NMoonAnime: haglund запит {haglundUrl}");

                    string json = await HttpHelper.GetAsync(_httpHydra, _init, haglundUrl, DefaultHeaders(), _proxyManager);
                    if (string.IsNullOrWhiteSpace(json))
                        return null;

                    var mappings = JsonSerializer.Deserialize<List<HaglundIdMapping>>(json, _jsonOptions);
                    if (mappings == null || mappings.Count == 0)
                        return null;

                    // Групуємо MAL ID за themoviedb-season
                    // Пропускаємо themoviedb-season == 0 (окремі ONA/OVA)
                    var bySeason = mappings
                        .Where(m => m.MyAnimeList.HasValue && (m.TheMovieDbSeason ?? 0) > 0)
                        .GroupBy(m => m.TheMovieDbSeason.Value)
                        .OrderBy(g => g.Key)
                        .ToList();

                    if (bySeason.Count == 0)
                        return null;

                    _onLog($"NMoonAnime: haglund знайдено {bySeason.Count} сезонів для {imdbId}");
                    foreach (var g in bySeason)
                        _onLog($"NMoonAnime: сезон {g.Key} → {g.Count()} MAL ID: [{string.Join(", ", g.Select(m => m.MyAnimeList.ToString()))}]");

                    var result = new List<NMoonAnimeSeasonRef>();

                    foreach (var seasonGroup in bySeason)
                    {
                        int seasonNumber = seasonGroup.Key;
                        var moonanimeUrls = new List<string>();

                        foreach (var mapping in seasonGroup)
                        {
                            string malId = mapping.MyAnimeList.Value.ToString();
                            var titleResponse = await FetchMoonanimeTitleByMalId(malId);
                            if (titleResponse != null && titleResponse.Id > 0)
                            {
                                string url = $"{_init.host.TrimEnd('/')}/title/{titleResponse.Id}";
                                if (!moonanimeUrls.Contains(url))
                                    moonanimeUrls.Add(url);
                            }
                        }

                        if (moonanimeUrls.Count == 0)
                            continue;

                        result.Add(new NMoonAnimeSeasonRef
                        {
                            SeasonNumber = seasonNumber,
                            Url = moonanimeUrls[0],
                            AdditionalUrls = moonanimeUrls.Count > 1
                                ? moonanimeUrls.Skip(1).ToList()
                                : new List<string>()
                        });
                    }

                    if (result.Count > 0)
                    {
                        _hybridCache.Set(memKey, result, CacheHelper.CacheTime(30, init: _init));
                        return result;
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _onLog($"NMoonAnime: помилка haglund - {ex.Message}");
                    return null;
                }
            }, default, negativeCacheSeconds: 60);
        }

        /// <summary>
        /// Отримання moonanime ID за MAL ID.
        /// v6.0 API повертає flat дані з полем id (moonanime internal ID).
        /// </summary>
        private async Task<MoonAnimeTitleResponse> FetchMoonanimeTitleByMalId(string malId)
        {
            string memKey = $"NMoonAnime:mal-title:{malId}";
            if (!_nocache && _hybridCache.TryGetValue(memKey, out MoonAnimeTitleResponse cached))
                return cached;

            try
            {
                string url = $"{_init.host.TrimEnd('/')}/api/6.0/title/by/mal_id/{malId}?api_key={ApiToken}";
                string json = await HttpHelper.GetAsync(_httpHydra, _init, url, DefaultHeaders(), _proxyManager);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var response = JsonSerializer.Deserialize<MoonAnimeTitleResponse>(json, _jsonOptions);
                if (response != null && response.Id > 0)
                    _hybridCache.Set(memKey, response, CacheHelper.CacheTime(10, init: _init));

                return response;
            }
            catch (Exception ex)
            {
                _onLog($"NMoonAnime: помилка отримання title за MAL ID {malId} - {ex.Message}");
                return null;
            }
        }

        private async Task<List<NMoonAnimeSeasonRef>> SearchByMalId(string malId, int seasonHint = 0)
        {
            string memKey = $"NMoonAnime:mal:{malId}";
            if (!_nocache && _hybridCache.TryGetValue(memKey, out List<NMoonAnimeSeasonRef> cached))
            {
                if (seasonHint > 0)
                    foreach (var s in cached)
                        if (s.SeasonNumber <= 0) s.SeasonNumber = seasonHint;
                return cached;
            }

            try
            {
                var response = await FetchMoonanimeTitleByMalId(malId);
                if (response == null || response.Id <= 0)
                    return null;

                int seasonNumber = seasonHint > 0 ? seasonHint : 1;
                var seasons = new List<NMoonAnimeSeasonRef>
                {
                    new NMoonAnimeSeasonRef
                    {
                        SeasonNumber = seasonNumber,
                        Url = $"{_init.host.TrimEnd('/')}/title/{response.Id}"
                    }
                };

                _hybridCache.Set(memKey, seasons, CacheHelper.CacheTime(10, init: _init));
                return seasons;
            }
            catch (Exception ex)
            {
                _onLog($"NMoonAnime: помилка пошуку за MAL ID - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Прямий пошук за назвою до moonanime API v7.0 з фільтрацією.
        /// </summary>
        private async Task<List<NMoonAnimeSeasonRef>> SearchByTitle(string originalTitle, string title, int year, int serial, int limit = 5)
        {
            // Спробуємо спочатку оригінальну назву, потім локалізовану
            var queries = new List<string>();
            if (!string.IsNullOrWhiteSpace(originalTitle))
                queries.Add(originalTitle);
            if (!string.IsNullOrWhiteSpace(title) && !string.Equals(title, originalTitle, StringComparison.OrdinalIgnoreCase))
                queries.Add(title);

            foreach (var query in queries)
            {
                var results = await SearchByTitleSingle(query, limit);
                if (results == null || results.Count == 0)
                    continue;

                // Фільтрація за типом та роком
                var filtered = FilterSearchResults(results, year, serial);
                if (filtered == null || filtered.Count == 0)
                    continue;

                // Конвертуємо в SeasonRef — використовуємо TitlePageId (moonanime internal ID)
                var seasons = filtered
                    .Select(r => new NMoonAnimeSeasonRef
                    {
                        SeasonNumber = 1,
                        Url = $"{_init.host.TrimEnd('/')}/title/{r.TitlePageId}"
                    })
                    .ToList();

                return seasons;
            }

            return new List<NMoonAnimeSeasonRef>();
        }

        private async Task<List<MoonAnimeSearchResult>> SearchByTitleSingle(string query, int limit)
        {
            string memKey = $"NMoonAnime:search-title:{query}:{limit}";
            if (!_nocache && _hybridCache.TryGetValue(memKey, out List<MoonAnimeSearchResult> cached))
                return cached;

            try
            {
                string encoded = Uri.EscapeDataString(query);
                string url = $"{_init.host.TrimEnd('/')}/api/7.0/anime/search?api_key={ApiToken}&q={encoded}&limit={limit}";
                _onLog($"NMoonAnime: прямий пошук {url}");

                string json = await HttpHelper.GetAsync(_httpHydra, _init, url, DefaultHeaders(), _proxyManager);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                // API повертає {"data": [...], "count": N}
                var response = JsonSerializer.Deserialize<MoonAnimeSearchResponse>(json, _jsonOptions);
                var results = response?.Data;
                if (results != null && results.Count > 0)
                    _hybridCache.Set(memKey, results, CacheHelper.CacheTime(10, init: _init));

                return results;
            }
            catch (Exception ex)
            {
                _onLog($"NMoonAnime: помилка прямого пошуку - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Фільтрація результатів пошуку за типом контенту.
        /// Повертає всі результати що відповідають запиту (серіал/фільм).
        /// </summary>
        private List<MoonAnimeSearchResult> FilterSearchResults(List<MoonAnimeSearchResult> results, int year, int serial)
        {
            if (results == null || results.Count == 0)
                return results;

            bool wantSeries = serial == 1;

            return results
                .Where(r =>
                {
                    string type = r.Type?.ToLowerInvariant() ?? "";
                    bool isMovie = type == "movie";
                    bool isSeries = type == "tv" || type == "ova" || type == "ona" || type == "special";

                    if (wantSeries)
                        return isSeries;
                    else
                        return isMovie;
                })
                .OrderByDescending(r => r.Year ?? 0)
                .ToList();
        }

        #endregion

        #region Парсинг контенту сезону

        public async Task<NMoonAnimeSeasonContent> GetSeasonContent(NMoonAnimeSeasonRef season)
        {
            if (season == null || string.IsNullOrWhiteSpace(season.Url))
                return null;

            string memKey = $"NMoonAnime:season:{season.Url}:{string.Join(",", season.AdditionalUrls ?? new List<string>())}";
            return await SingleFlightCache.GetOrCreateAsync<NMoonAnimeSeasonContent>(memKey, _hybridCache, async token =>
            {
                if (!_nocache && _hybridCache.TryGetValue(memKey, out NMoonAnimeSeasonContent hit))
                    return hit;

                try
                {
                    // Завантажуємо основний URL
                    _onLog($"NMoonAnime: завантаження сезону {season.Url} (додаткових URL: {season.AdditionalUrls?.Count ?? 0})");
                    string html = await HttpHelper.GetAsync(_httpHydra, _init, season.Url, DefaultHeaders(), _proxyManager);
                    if (string.IsNullOrWhiteSpace(html))
                        return null;

                    var content = ParseSeasonPage(html, season.SeasonNumber, season.Url);

                    // Завантажуємо додаткові URL (частини сезону) та об'єднуємо епізоди
                    var allUrls = season.AdditionalUrls ?? new List<string>();
                    foreach (var additionalUrl in allUrls)
                    {
                        if (string.IsNullOrWhiteSpace(additionalUrl))
                            continue;

                        _onLog($"NMoonAnime: завантаження додаткової частини {additionalUrl}");
                        string additionalHtml = await HttpHelper.GetAsync(_httpHydra, _init, additionalUrl, DefaultHeaders(), _proxyManager);
                        if (string.IsNullOrWhiteSpace(additionalHtml))
                            continue;

                        var additionalContent = ParseSeasonPage(additionalHtml, season.SeasonNumber, additionalUrl);
                        if (additionalContent != null && additionalContent.Voices.Count > 0)
                        {
                            MergeSeasonContent(content, additionalContent);
                        }
                    }

                    if (content != null)
                        _hybridCache.Set(memKey, content, CacheHelper.CacheTime(20, init: _init));

                    return content;
                }
                catch (Exception ex)
                {
                    _onLog($"NMoonAnime: помилка читання сезону - {ex.Message}");
                    return null;
                }
            }, default, negativeCacheSeconds: 60);
        }

        /// <summary>
        /// Об'єднує контент двох частин сезону.
        /// Додає епізоди з additional в voices основного контенту.
        /// </summary>
        private void MergeSeasonContent(NMoonAnimeSeasonContent primary, NMoonAnimeSeasonContent additional)
        {
            if (primary == null || additional == null)
                return;

            _onLog($"NMoonAnime: об'єднання сезону — основний: {primary.Voices.Count} voices, додатковий: {additional.Voices.Count} voices");

            foreach (var additionalVoice in additional.Voices)
            {
                if (additionalVoice == null)
                    continue;

                // Шукаємо відповідну озвучку в основному контенті
                var existingVoice = primary.Voices
                    .FirstOrDefault(v => v.Name == additionalVoice.Name);

                if (existingVoice != null && existingVoice.Episodes != null && additionalVoice.Episodes != null)
                {
                    // Об'єднуємо епізоди, уникаючи дублікатів за номером
                    var existingNumbers = new HashSet<int>(existingVoice.Episodes.Select(e => e.Number));
                    int addedCount = 0;
                    foreach (var ep in additionalVoice.Episodes)
                    {
                        if (!existingNumbers.Contains(ep.Number))
                        {
                            existingVoice.Episodes.Add(ep);
                            existingNumbers.Add(ep.Number);
                            addedCount++;
                        }
                    }
                    _onLog($"NMoonAnime: voice '{additionalVoice.Name}' — додано {addedCount} епізодів (було {existingVoice.Episodes.Count - addedCount}, стало {existingVoice.Episodes.Count})");
                    existingVoice.Episodes = existingVoice.Episodes
                        .OrderBy(e => e.Number <= 0 ? int.MaxValue : e.Number)
                        .ToList();
                }
                else if (existingVoice == null)
                {
                    // Нова озвучка — додаємо
                    primary.Voices.Add(additionalVoice);
                }
            }
        }

        private NMoonAnimeSeasonContent ParseSeasonPage(string html, int seasonNumber, string seasonUrl)
        {
            var content = new NMoonAnimeSeasonContent
            {
                SeasonNumber = seasonNumber <= 0 ? 1 : seasonNumber,
                Url = seasonUrl,
                IsSeries = false
            };

            var payload = PlayerJsDecoder.ExtractPlayerPayload(html);
            if (payload == null)
                return content;

            var voices = ParseSeriesVoices(payload.FilePayload, content.SeasonNumber);
            if (voices.Count > 0)
            {
                content.IsSeries = true;
                content.Voices = voices;
                return content;
            }

            var movieEntries = ParseMovieEntries(payload.FilePayload);
            int movieIndex = 1;
            foreach (var entry in movieEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.File))
                    continue;

                content.Voices.Add(new NMoonAnimeVoiceContent
                {
                    Name = NormalizeVoiceName(entry.Title, movieIndex),
                    MovieFile = entry.File
                });
                movieIndex++;
            }

            return content;
        }

        #endregion

        #region Парсинг стрімів

        public List<NMoonAnimeStreamVariant> ParseStreams(string rawFile)
        {
            var streams = new List<NMoonAnimeStreamVariant>();
            if (string.IsNullOrWhiteSpace(rawFile))
                return streams;

            string value = WebUtility.HtmlDecode(rawFile).Trim();

            var bracketMatches = Regex.Matches(value, @"\[(?<quality>[^\]]+)\](?<url>https?://[^,\[]+)", RegexOptions.IgnoreCase);
            foreach (Match match in bracketMatches)
            {
                string quality = NormalizeQuality(match.Groups["quality"].Value);
                string url = match.Groups["url"].Value?.Trim();
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                streams.Add(new NMoonAnimeStreamVariant
                {
                    Url = url,
                    Quality = quality
                });
            }

            if (streams.Count == 0)
            {
                var taggedMatches = Regex.Matches(value, @"(?<quality>\d{3,4}p?)\s*[:|]\s*(?<url>https?://[^,\s]+)", RegexOptions.IgnoreCase);
                foreach (Match match in taggedMatches)
                {
                    string quality = NormalizeQuality(match.Groups["quality"].Value);
                    string url = match.Groups["url"].Value?.Trim();
                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    streams.Add(new NMoonAnimeStreamVariant
                    {
                        Url = url,
                        Quality = quality
                    });
                }
            }

            if (streams.Count == 0)
            {
                var plainLinks = value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(part => part.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (plainLinks.Count > 1)
                {
                    for (int i = 0; i < plainLinks.Count; i++)
                    {
                        streams.Add(new NMoonAnimeStreamVariant
                        {
                            Url = plainLinks[i],
                            Quality = $"auto-{i + 1}"
                        });
                    }
                }
            }

            if (streams.Count == 0 && value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                streams.Add(new NMoonAnimeStreamVariant
                {
                    Url = value,
                    Quality = "auto"
                });
            }

            return streams
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Url))
                .Select(s => new NMoonAnimeStreamVariant
                {
                    Url = s.Url.Trim(),
                    Quality = NormalizeQuality(s.Quality)
                })
                .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(s => QualityWeight(s.Quality))
                .ToList();
        }

        #endregion

        #region Внутрішні методи парсингу

        private List<NMoonAnimeVoiceContent> ParseSeriesVoices(object filePayload, int seasonHint)
        {
            var voices = new List<NMoonAnimeVoiceContent>();
            var voiceItems = NormalizeVoiceItems(filePayload);
            if (voiceItems.Count == 0)
                return voices;

            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int fallbackVoiceIndex = 1;

            foreach (var voiceItem in voiceItems)
            {
                if (!TryGetArray(voiceItem, "folder", out JsonArray folder) || folder.Count == 0)
                {
                    fallbackVoiceIndex++;
                    continue;
                }

                string baseVoiceName = Nullish(GetString(voiceItem, "title")) ?? $"Озвучка {fallbackVoiceIndex}";
                nameCounts[baseVoiceName] = nameCounts.TryGetValue(baseVoiceName, out int currentCount) ? currentCount + 1 : 1;
                string displayVoiceName = nameCounts[baseVoiceName] == 1 ? baseVoiceName : $"{baseVoiceName} {nameCounts[baseVoiceName]}";

                var seasons = new Dictionary<int, List<NMoonAnimeEpisodeContent>>();
                bool hasNestedSeasons = folder.Any(item => item is JsonObject nestedItem && nestedItem["folder"] is JsonArray);

                if (hasNestedSeasons)
                {
                    int seasonIndex = 1;
                    foreach (var seasonItem in folder)
                    {
                        if (seasonItem is not JsonObject seasonObject)
                        {
                            seasonIndex++;
                            continue;
                        }

                        if (!TryGetArray(seasonObject, "folder", out JsonArray seasonFolder) || seasonFolder.Count == 0)
                        {
                            seasonIndex++;
                            continue;
                        }

                        var episodes = NormalizeEpisodeList(seasonFolder);
                        if (episodes.Count == 0)
                        {
                            seasonIndex++;
                            continue;
                        }

                        int seasonNumber = ExtractSeasonNumber(GetString(seasonObject, "title"))
                            ?? (seasonHint > 0 ? seasonHint : seasonIndex);

                        seasons[seasonNumber] = episodes;
                        seasonIndex++;
                    }
                }
                else
                {
                    var episodes = NormalizeEpisodeList(folder);
                    if (episodes.Count > 0)
                    {
                        int seasonNumber = seasonHint > 0
                            ? seasonHint
                            : ExtractSeasonNumber(GetString(voiceItem, "title")) ?? 1;

                        seasons[seasonNumber] = episodes;
                    }
                }

                if (seasons.Count == 0)
                {
                    fallbackVoiceIndex++;
                    continue;
                }

                int targetSeason = ResolveSeason(seasons.Keys, seasonHint);
                voices.Add(new NMoonAnimeVoiceContent
                {
                    Name = displayVoiceName,
                    Episodes = seasons[targetSeason]
                });

                fallbackVoiceIndex++;
            }

            return voices;
        }

        private List<NMoonAnimeEpisodeContent> NormalizeEpisodeList(JsonArray items)
        {
            var episodes = new List<NMoonAnimeEpisodeContent>();
            int index = 1;

            foreach (var item in items)
            {
                if (item is JsonObject episodeObject)
                {
                    var episode = NormalizeEpisode(episodeObject, index);
                    if (episode != null)
                        episodes.Add(episode);
                }

                index++;
            }

            return episodes
                .OrderBy(e => e.Number <= 0 ? int.MaxValue : e.Number)
                .ThenBy(e => e.Name)
                .ToList();
        }

        private NMoonAnimeEpisodeContent NormalizeEpisode(JsonObject item, int index)
        {
            string fileValue = NormalizeFileValue(GetString(item, "file"));
            if (string.IsNullOrWhiteSpace(fileValue))
                return null;

            string title = Nullish(GetString(item, "title")) ?? $"Епізод {index}";
            int number = ExtractEpisodeNumber(title, index);

            return new NMoonAnimeEpisodeContent
            {
                Name = WebUtility.HtmlDecode(title),
                Number = number,
                File = fileValue
            };
        }

        private List<NMoonAnimeMovieEntry> ParseMovieEntries(object filePayload)
        {
            var entries = new List<NMoonAnimeMovieEntry>();
            if (filePayload == null)
                return entries;

            if (filePayload is string textPayload)
            {
                var parsedPayload = LoadJsonLoose(textPayload);
                if (parsedPayload != null)
                    return ParseMovieEntries(parsedPayload);

                string fileValue = NormalizeFileValue(textPayload);
                if (!string.IsNullOrWhiteSpace(fileValue))
                {
                    entries.Add(new NMoonAnimeMovieEntry
                    {
                        Title = "Основне джерело",
                        File = fileValue
                    });
                }

                return entries;
            }

            if (filePayload is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string textValue))
            {
                var parsedPayload = LoadJsonLoose(textValue);
                if (parsedPayload != null)
                    return ParseMovieEntries(parsedPayload);

                string fileValue = NormalizeFileValue(textValue);
                if (!string.IsNullOrWhiteSpace(fileValue))
                {
                    entries.Add(new NMoonAnimeMovieEntry
                    {
                        Title = "Основне джерело",
                        File = fileValue
                    });
                }

                return entries;
            }

            if (filePayload is JsonObject objPayload)
            {
                string fileValue = NormalizeFileValue(GetString(objPayload, "file"));
                if (!string.IsNullOrWhiteSpace(fileValue))
                {
                    entries.Add(new NMoonAnimeMovieEntry
                    {
                        Title = Nullish(GetString(objPayload, "title")) ?? "Основне джерело",
                        File = fileValue
                    });
                }

                return entries;
            }

            if (filePayload is not JsonArray arrayPayload)
                return entries;

            int index = 1;
            foreach (var item in arrayPayload)
            {
                if (item is JsonObject itemObject)
                {
                    string fileValue = NormalizeFileValue(GetString(itemObject, "file"));
                    if (!string.IsNullOrWhiteSpace(fileValue))
                    {
                        entries.Add(new NMoonAnimeMovieEntry
                        {
                            Title = Nullish(GetString(itemObject, "title")) ?? $"Варіант {index}",
                            File = fileValue
                        });
                    }
                }
                else if (item is JsonValue itemValue && itemValue.TryGetValue<string>(out string itemText))
                {
                    string fileValue = NormalizeFileValue(itemText);
                    if (!string.IsNullOrWhiteSpace(fileValue))
                    {
                        entries.Add(new NMoonAnimeMovieEntry
                        {
                            Title = $"Варіант {index}",
                            File = fileValue
                        });
                    }
                }

                index++;
            }

            return entries;
        }

        #endregion

        #region Допоміжні методи

        private static JsonNode LoadJsonLoose(string value) => PlayerJsDecoder.LoadJsonLoose(value);
        private static string Nullish(string value) => PlayerJsDecoder.Nullish(value);

        private static bool TryGetArray(JsonObject obj, string key, out JsonArray array)
        {
            array = null;
            if (obj == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (!obj.TryGetPropertyValue(key, out JsonNode node))
                return false;

            if (node is not JsonArray jsonArray)
                return false;

            array = jsonArray;
            return true;
        }

        private static List<JsonObject> NormalizeVoiceItems(object filePayload)
        {
            if (filePayload == null)
                return new List<JsonObject>();

            if (filePayload is JsonObject objPayload)
                return new List<JsonObject> { objPayload };

            if (filePayload is JsonArray arrayPayload)
                return arrayPayload.OfType<JsonObject>().ToList();

            if (filePayload is JsonNode nodePayload)
            {
                if (nodePayload is JsonObject nodeObject)
                    return new List<JsonObject> { nodeObject };

                if (nodePayload is JsonArray nodeArray)
                    return nodeArray.OfType<JsonObject>().ToList();

                if (nodePayload is JsonValue nodeValue && nodeValue.TryGetValue<string>(out string rawText))
                    return NormalizeVoiceItems(rawText);
            }

            if (filePayload is string textPayload)
            {
                var parsedNode = LoadJsonLoose(textPayload);
                if (parsedNode != null)
                    return NormalizeVoiceItems(parsedNode);
            }

            return new List<JsonObject>();
        }

        private static int ResolveSeason(IEnumerable<int> availableSeasons, int seasonHint)
        {
            var ordered = availableSeasons
                .Where(s => s > 0)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            if (ordered.Count == 0)
                return seasonHint > 0 ? seasonHint : 1;

            if (seasonHint > 0 && ordered.Contains(seasonHint))
                return seasonHint;

            return ordered[0];
        }

        private static int? ExtractSeasonNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var match = ReSeason.Match(value);
            if (!match.Success)
                return null;

            string rawNumber = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (int.TryParse(rawNumber, out int seasonNumber))
                return seasonNumber;

            return null;
        }

        private static int ExtractEpisodeNumber(string title, int fallback)
        {
            if (string.IsNullOrWhiteSpace(title))
                return fallback;

            var episodeMatch = ReEpisode.Match(title);
            if (episodeMatch.Success && int.TryParse(episodeMatch.Groups[1].Value, out int episodeNumber))
                return episodeNumber;

            var trailingMatch = Regex.Match(title, @"(\d+)(?!.*\d)");
            if (trailingMatch.Success && int.TryParse(trailingMatch.Groups[1].Value, out int trailingNumber))
                return trailingNumber;

            return fallback;
        }

        private static string GetString(JsonObject obj, string key)
        {
            if (obj == null || string.IsNullOrWhiteSpace(key) || !obj.TryGetPropertyValue(key, out JsonNode node) || node == null)
                return null;

            if (node is JsonValue valueNode)
            {
                try
                {
                    return valueNode.GetValue<string>();
                }
                catch
                {
                    return valueNode.ToString();
                }
            }

            return node.ToString();
        }

        private static string NormalizeFileValue(string value)
        {
            string text = Nullish(value);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return WebUtility.HtmlDecode(text)
                .Replace("\\/", "/")
                .Trim();
        }

        private static string NormalizeVoiceName(string source, int fallbackIndex)
        {
            string voice = WebUtility.HtmlDecode(source ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(voice) ? $"Озвучка {fallbackIndex}" : voice;
        }

        private static string NormalizeQuality(string quality)
        {
            if (string.IsNullOrWhiteSpace(quality))
                return "auto";

            string value = quality.Trim().Trim('[', ']');
            if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return "auto";

            var match = Regex.Match(value, @"(?<q>\d{3,4})");
            if (match.Success)
                return $"{match.Groups["q"].Value}p";

            return value;
        }

        private static int QualityWeight(string quality)
        {
            if (string.IsNullOrWhiteSpace(quality))
                return 0;

            var match = Regex.Match(quality, @"\d{3,4}");
            if (match.Success && int.TryParse(match.Value, out int q))
                return q;

            return quality.Equals("auto", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private List<HeadersModel> DefaultHeaders()
        {
            return new List<HeadersModel>
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host)
            };
        }

        private sealed class NMoonAnimeMovieEntry
        {
            public string Title { get; set; }
            public string File { get; set; }
        }

        #endregion
    }
}
