using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using UafilmME.Models;

namespace UafilmME
{
    public class UafilmMEInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;



        public UafilmMEInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        public async Task<List<UafilmSearchItem>> Search(string title, string originalTitle, int year)
        {
            var queries = BuildSearchQueries(title, originalTitle, year).ToList();
            if (queries.Count == 0)
                return new List<UafilmSearchItem>();

            var all = new Dictionary<long, UafilmSearchItem>();
            foreach (var query in queries)
            {
                var items = await SearchByQuery(query);
                foreach (var item in items)
                    all[item.Id] = item;
            }

            return all.Values.ToList();
        }

        public UafilmSearchItem SelectBestSearchResult(List<UafilmSearchItem> results, long tmdbId, string imdbId, string title, string originalTitle, int year, int serial)
        {
            if (results == null || results.Count == 0)
                return null;

            foreach (var item in results)
                item.MatchScore = CalcMatchScore(item, tmdbId, imdbId, title, originalTitle, year, serial);

            return results
                .OrderByDescending(r => r.MatchScore)
                .ThenByDescending(r => r.Year)
                .FirstOrDefault();
        }

        public async Task<UafilmTitleDetails> GetTitleDetails(long titleId)
        {
            string memKey = $"UafilmME:title:{titleId}";
            if (_hybridCache.TryGetValue(memKey, out UafilmTitleDetails cached))
                return cached;

            try
            {
                string json = await ApiGet($"titles/{titleId}?loader=titlePage", $"{_init.host}/titles/{titleId}");
                var title = ParseTitleDetails(json);
                if (title != null)
                    _hybridCache.Set(memKey, title, cacheTime(30, init: _init));

                return title;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UafilmME: помилка отримання title {titleId}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<UafilmSeasonItem>> GetAllSeasons(long titleId)
        {
            string memKey = $"UafilmME:seasons:{titleId}";
            if (_hybridCache.TryGetValue(memKey, out List<UafilmSeasonItem> cached))
                return cached;

            var all = new List<UafilmSeasonItem>();
            int currentPage = 1;
            int guard = 0;

            while (currentPage > 0 && guard < 100)
            {
                guard++;
                var page = await GetSeasonsPage(titleId, currentPage);
                if (page.Items.Count == 0)
                    break;

                all.AddRange(page.Items);

                if (page.NextPage.HasValue && page.NextPage.Value != currentPage)
                    currentPage = page.NextPage.Value;
                else
                    break;
            }

            var result = all
                .GroupBy(s => s.Number)
                .Select(g => g.OrderByDescending(x => x.EpisodesCount).First())
                .OrderBy(s => s.Number)
                .ToList();

            if (result.Count == 0)
            {
                var title = await GetTitleDetails(titleId);
                if (title?.SeasonsCount > 0)
                {
                    for (int i = 1; i <= title.SeasonsCount; i++)
                    {
                        result.Add(new UafilmSeasonItem()
                        {
                            Number = i,
                            EpisodesCount = 0
                        });
                    }
                }
            }

            if (result.Count > 0)
                _hybridCache.Set(memKey, result, cacheTime(60, init: _init));

            return result;
        }

        public async Task<List<UafilmEpisodeItem>> GetSeasonEpisodes(long titleId, int season)
        {
            string memKey = $"UafilmME:episodes:{titleId}:{season}";
            if (_hybridCache.TryGetValue(memKey, out List<UafilmEpisodeItem> cached))
                return cached;

            var all = new List<UafilmEpisodeItem>();
            int currentPage = 1;
            int guard = 0;

            while (currentPage > 0 && guard < 200)
            {
                guard++;
                var page = await GetEpisodesPage(titleId, season, currentPage);
                if (page.Items.Count == 0)
                    break;

                all.AddRange(page.Items);

                if (page.NextPage.HasValue && page.NextPage.Value != currentPage)
                    currentPage = page.NextPage.Value;
                else
                    break;
            }

            var result = all
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .OrderBy(e => e.EpisodeNumber)
                .ToList();

            if (result.Count > 0)
                _hybridCache.Set(memKey, result, cacheTime(30, init: _init));

            return result;
        }

        public async Task<List<UafilmVideoItem>> GetMovieVideos(long titleId)
        {
            var title = await GetTitleDetails(titleId);
            if (title == null || title.PrimaryVideoId <= 0)
                return new List<UafilmVideoItem>();

            var watch = await GetWatch(title.PrimaryVideoId);
            return CollectPlayableVideos(watch);
        }

        public async Task<UafilmWatchInfo> GetWatch(long videoId)
        {
            if (videoId <= 0)
                return null;

            string memKey = $"UafilmME:watch:{videoId}";
            if (_hybridCache.TryGetValue(memKey, out UafilmWatchInfo cached))
                return cached;

            try
            {
                string json = await ApiGet($"watch/{videoId}", _init.host);
                var watch = ParseWatchInfo(json);
                if (watch?.Video != null)
                    _hybridCache.Set(memKey, watch, cacheTime(7, init: _init));

                return watch;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"UafilmME: помилка отримання watch/{videoId}: {ex.Message}");
                return null;
            }
        }

        public List<UafilmVideoItem> CollectPlayableVideos(UafilmWatchInfo watch)
        {
            var list = new List<UafilmVideoItem>();
            if (watch == null)
                return list;

            if (watch.Video != null)
                list.Add(watch.Video);

            if (watch.AlternativeVideos != null && watch.AlternativeVideos.Count > 0)
                list.AddRange(watch.AlternativeVideos);

            return list
                .Where(v => v != null && v.Id > 0)
                .Select(v =>
                {
                    v.Src = NormalizeVideoSource(v.Src);
                    return v;
                })
                .Where(v => !string.IsNullOrWhiteSpace(v.Src))
                .Where(v => !string.Equals(v.Type, "embed", StringComparison.OrdinalIgnoreCase))
                .Where(v => v.Src.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) < 0)
                .GroupBy(v => v.Id)
                .Select(g => g.First())
                .ToList();
        }

        private async Task<List<UafilmSearchItem>> SearchByQuery(string query)
        {
            string memKey = $"UafilmME:search:{query}";
            if (_hybridCache.TryGetValue(memKey, out List<UafilmSearchItem> cached))
                return cached;

            string encoded = HttpUtility.UrlEncode(query);
            string json = await ApiGet($"search/{encoded}?loader=searchPage", $"{_init.host}/search/{encoded}");
            var items = ParseSearchResults(json);

            if (items.Count > 0)
                _hybridCache.Set(memKey, items, cacheTime(20, init: _init));

            return items;
        }

        private async Task<(List<UafilmSeasonItem> Items, int? NextPage)> GetSeasonsPage(long titleId, int page)
        {
            string memKey = $"UafilmME:seasons-page:{titleId}:{page}";
            if (_hybridCache.TryGetValue(memKey, out List<UafilmSeasonItem> cachedItems) &&
                _hybridCache.TryGetValue(memKey + ":next", out int? cachedNext))
            {
                return (cachedItems, cachedNext);
            }

            string suffix = page > 1 ? $"?page={page}" : string.Empty;
            string json = await ApiGet($"titles/{titleId}/seasons{suffix}", $"{_init.host}/titles/{titleId}");
            var parsed = ParseSeasonsPage(json);

            _hybridCache.Set(memKey, parsed.Items, cacheTime(30, init: _init));
            _hybridCache.Set(memKey + ":next", parsed.NextPage, cacheTime(30, init: _init));
            return parsed;
        }

        private async Task<(List<UafilmEpisodeItem> Items, int? NextPage)> GetEpisodesPage(long titleId, int season, int page)
        {
            string memKey = $"UafilmME:episodes-page:{titleId}:{season}:{page}";
            if (_hybridCache.TryGetValue(memKey, out List<UafilmEpisodeItem> cachedItems) &&
                _hybridCache.TryGetValue(memKey + ":next", out int? cachedNext))
            {
                return (cachedItems, cachedNext);
            }

            string suffix = page > 1 ? $"?page={page}" : string.Empty;
            string json = await ApiGet($"titles/{titleId}/seasons/{season}/episodes{suffix}", $"{_init.host}/titles/{titleId}");
            var parsed = ParseEpisodesPage(json);

            _hybridCache.Set(memKey, parsed.Items, cacheTime(20, init: _init));
            _hybridCache.Set(memKey + ":next", parsed.NextPage, cacheTime(20, init: _init));
            return parsed;
        }

        private async Task<string> ApiGet(string pathAndQuery, string referer)
        {
            string url = $"{_init.host.TrimEnd('/')}/api/v1/{pathAndQuery.TrimStart('/')}";
            string reqReferer = string.IsNullOrWhiteSpace(referer) ? $"{_init.host}/" : referer;

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "EchoapiRuntime/1.1.0"),
                new HeadersModel("Referer", reqReferer),
                new HeadersModel("Accept", "*/*")
            };

            if (_httpHydra != null)
                return await _httpHydra.Get(url, newheaders: headers);

            return await Http.Get(url, headers: headers, proxy: _proxyManager.Get());
        }

        private string NormalizeVideoSource(string src)
        {
            if (string.IsNullOrWhiteSpace(src))
                return null;

            src = src.Trim();
            if (src.StartsWith("//"))
                return "https:" + src;

            if (src.StartsWith("/"))
                return _init.host.TrimEnd('/') + src;

            return src;
        }

        private static IEnumerable<string> BuildSearchQueries(string title, string originalTitle, int year)
        {
            var queries = new List<string>();
            void Add(string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    queries.Add(value.Trim());
            }

            Add(title);
            Add(originalTitle);

            if (year > 1900)
            {
                if (!string.IsNullOrWhiteSpace(title))
                    Add($"{title} {year}");

                if (!string.IsNullOrWhiteSpace(originalTitle))
                    Add($"{originalTitle} {year}");
            }

            return queries
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private List<UafilmSearchItem> ParseSearchResults(string json)
        {
            var list = new List<UafilmSearchItem>();
            if (string.IsNullOrWhiteSpace(json))
                return list;

            using var doc = JsonDocument.Parse(json);
            if (!TryGetArray(doc.RootElement, "results", out var results))
                return list;

            foreach (var item in results.EnumerateArray())
            {
                if (!TryReadLong(item, "id", out long id) || id <= 0)
                    continue;

                list.Add(new UafilmSearchItem()
                {
                    Id = id,
                    Name = ReadString(item, "name"),
                    OriginalTitle = ReadString(item, "original_title"),
                    IsSeries = ReadBool(item, "is_series"),
                    Year = ReadInt(item, "year"),
                    ImdbId = ReadString(item, "imdb_id"),
                    TmdbId = ReadLong(item, "tmdb_id"),
                    Poster = ReadString(item, "poster")
                });
            }

            return list;
        }

        private UafilmTitleDetails ParseTitleDetails(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            using var doc = JsonDocument.Parse(json);
            if (!TryGetObject(doc.RootElement, "title", out var titleObj))
                return null;

            var info = new UafilmTitleDetails()
            {
                Id = ReadLong(titleObj, "id"),
                Name = ReadString(titleObj, "name"),
                OriginalTitle = ReadString(titleObj, "original_title"),
                IsSeries = ReadBool(titleObj, "is_series"),
                Year = ReadInt(titleObj, "year"),
                ImdbId = ReadString(titleObj, "imdb_id"),
                TmdbId = ReadLong(titleObj, "tmdb_id"),
                SeasonsCount = ReadInt(titleObj, "seasons_count")
            };

            if (TryGetObject(titleObj, "primary_video", out var primaryVideo))
                info.PrimaryVideoId = ReadLong(primaryVideo, "id");

            return info;
        }

        private (List<UafilmSeasonItem> Items, int? NextPage) ParseSeasonsPage(string json)
        {
            var items = new List<UafilmSeasonItem>();
            int? next = null;
            if (string.IsNullOrWhiteSpace(json))
                return (items, next);

            using var doc = JsonDocument.Parse(json);
            if (!TryGetObject(doc.RootElement, "pagination", out var pagination))
                return (items, next);

            next = ReadNullableInt(pagination, "next_page");

            if (!TryGetArray(pagination, "data", out var data))
                return (items, next);

            foreach (var item in data.EnumerateArray())
            {
                int number = ReadInt(item, "number");
                if (number <= 0)
                    continue;

                items.Add(new UafilmSeasonItem()
                {
                    Id = ReadLong(item, "id"),
                    Number = number,
                    EpisodesCount = ReadInt(item, "episodes_count")
                });
            }

            return (items, next);
        }

        private (List<UafilmEpisodeItem> Items, int? NextPage) ParseEpisodesPage(string json)
        {
            var items = new List<UafilmEpisodeItem>();
            int? next = null;
            if (string.IsNullOrWhiteSpace(json))
                return (items, next);

            using var doc = JsonDocument.Parse(json);
            if (!TryGetObject(doc.RootElement, "pagination", out var pagination))
                return (items, next);

            next = ReadNullableInt(pagination, "next_page");

            if (!TryGetArray(pagination, "data", out var data))
                return (items, next);

            foreach (var item in data.EnumerateArray())
            {
                long episodeId = ReadLong(item, "id");
                if (episodeId <= 0)
                    continue;

                long primaryVideoId = 0;
                string primaryVideoName = null;
                if (TryGetObject(item, "primary_video", out var primaryVideoObj))
                {
                    primaryVideoId = ReadLong(primaryVideoObj, "id");
                    primaryVideoName = ReadString(primaryVideoObj, "name");
                }

                items.Add(new UafilmEpisodeItem()
                {
                    Id = episodeId,
                    Name = ReadString(item, "name"),
                    SeasonNumber = ReadInt(item, "season_number"),
                    EpisodeNumber = ReadInt(item, "episode_number"),
                    PrimaryVideoId = primaryVideoId,
                    PrimaryVideoName = primaryVideoName
                });
            }

            return (items, next);
        }

        private UafilmWatchInfo ParseWatchInfo(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var watch = new UafilmWatchInfo();

            if (TryGetObject(doc.RootElement, "video", out var videoObj))
                watch.Video = ParseVideo(videoObj);

            if (TryGetArray(doc.RootElement, "alternative_videos", out var alternatives))
            {
                foreach (var alt in alternatives.EnumerateArray())
                {
                    var parsed = ParseVideo(alt);
                    if (parsed != null)
                        watch.AlternativeVideos.Add(parsed);
                }
            }

            return watch;
        }

        private static UafilmVideoItem ParseVideo(JsonElement obj)
        {
            long id = ReadLong(obj, "id");
            if (id <= 0)
                return null;

            return new UafilmVideoItem()
            {
                Id = id,
                Name = ReadString(obj, "name"),
                Src = ReadString(obj, "src"),
                Type = ReadString(obj, "type"),
                Quality = ReadString(obj, "quality"),
                Origin = ReadString(obj, "origin"),
                Language = ReadString(obj, "language"),
                SeasonNum = ReadNullableInt(obj, "season_num"),
                EpisodeNum = ReadNullableInt(obj, "episode_num"),
                EpisodeId = ReadLong(obj, "episode_id")
            };
        }

        private int CalcMatchScore(UafilmSearchItem item, long tmdbId, string imdbId, string title, string originalTitle, int year, int serial)
        {
            int score = 0;

            if (item == null)
                return score;

            if (tmdbId > 0 && item.TmdbId == tmdbId)
                score += 120;

            if (!string.IsNullOrWhiteSpace(imdbId) && !string.IsNullOrWhiteSpace(item.ImdbId) && string.Equals(item.ImdbId.Trim(), imdbId.Trim(), StringComparison.OrdinalIgnoreCase))
                score += 120;

            if (serial == 1)
                score += item.IsSeries ? 25 : -25;
            else
                score += item.IsSeries ? -15 : 15;

            if (year > 1900 && item.Year > 1900)
            {
                int diff = Math.Abs(item.Year - year);
                if (diff == 0)
                    score += 20;
                else if (diff == 1)
                    score += 10;
                else if (diff == 2)
                    score += 5;
                else
                    score -= 6;
            }

            score += ScoreTitle(item.Name, title);
            score += ScoreTitle(item.Name, originalTitle);
            score += ScoreTitle(item.OriginalTitle, title);
            score += ScoreTitle(item.OriginalTitle, originalTitle);

            return score;
        }

        private static int ScoreTitle(string candidate, string expected)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(expected))
                return 0;

            string left = NormalizeTitle(candidate);
            string right = NormalizeTitle(expected);
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                return 0;

            if (left == right)
                return 35;

            if (left.Contains(right) || right.Contains(left))
                return 20;

            var leftWords = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rightWords = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int overlap = leftWords.Intersect(rightWords).Count();
            if (overlap >= 2)
                return 12;
            if (overlap == 1)
                return 6;

            return 0;
        }

        private static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.ToLowerInvariant();
            normalized = Regex.Replace(normalized, "[^\\p{L}\\p{Nd}]+", " ", RegexOptions.CultureInvariant);
            normalized = Regex.Replace(normalized, "\\s+", " ", RegexOptions.CultureInvariant).Trim();
            return normalized;
        }

        private static bool TryGetObject(JsonElement source, string property, out JsonElement value)
        {
            value = default;
            if (!source.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.Object)
                return false;

            value = prop;
            return true;
        }

        private static bool TryGetArray(JsonElement source, string property, out JsonElement value)
        {
            value = default;
            if (!source.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.Array)
                return false;

            value = prop;
            return true;
        }

        private static string ReadString(JsonElement source, string property)
        {
            if (!source.TryGetProperty(property, out var value))
                return null;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetRawText();

            if (value.ValueKind == JsonValueKind.True)
                return bool.TrueString;

            if (value.ValueKind == JsonValueKind.False)
                return bool.FalseString;

            return null;
        }

        private static bool ReadBool(JsonElement source, string property)
        {
            if (!source.TryGetProperty(property, out var value))
                return false;

            if (value.ValueKind == JsonValueKind.True)
                return true;

            if (value.ValueKind == JsonValueKind.False)
                return false;

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetInt32() != 0;

            if (value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString();
                if (bool.TryParse(text, out bool parsedBool))
                    return parsedBool;

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                    return parsedInt != 0;
            }

            return false;
        }

        private static int ReadInt(JsonElement source, string property)
        {
            if (!source.TryGetProperty(property, out var value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;

            return 0;
        }

        private static int? ReadNullableInt(JsonElement source, string property)
        {
            if (!source.TryGetProperty(property, out var value))
                return null;

            if (value.ValueKind == JsonValueKind.Null)
                return null;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;

            return null;
        }

        private static long ReadLong(JsonElement source, string property)
        {
            return TryReadLong(source, property, out long value)
                ? value
                : 0;
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

        private static bool TryReadLong(JsonElement source, string property, out long value)
        {
            value = 0;
            if (!source.TryGetProperty(property, out var element))
                return false;

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long number))
            {
                value = number;
                return true;
            }

            if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }
    }
}
