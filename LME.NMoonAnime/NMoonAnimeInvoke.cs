using LME.NMoonAnime.Models;
using LME.Common.Playerjs;
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
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex ReSeason = new Regex(@"(?:season|сезон)\s*(\d+)|(\d+)\s*(?:season|сезон)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ReEpisode = new Regex(@"(?:episode|серія|серия|епізод|ep)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public NMoonAnimeInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        public async Task<List<NMoonAnimeSeasonRef>> Search(string imdbId, string malId, string title, int year)
        {
            string memKey = $"NMoonAnime:search:{imdbId}:{malId}:{title}:{year}";
            if (_hybridCache.TryGetValue(memKey, out List<NMoonAnimeSeasonRef> cached))
                return cached;

            try
            {
                var endpoints = new[]
                {
                    "/moonanime/search",
                    "/moonanime"
                };

                foreach (var endpoint in endpoints)
                {
                    string searchUrl = BuildSearchUrl(endpoint, imdbId, malId, title, year);
                    if (string.IsNullOrWhiteSpace(searchUrl))
                        continue;

                    _onLog($"NMoonAnime: пошук через {searchUrl}");
                    string json = await HttpHelper.GetAsync(_httpHydra, _init, searchUrl, DefaultHeaders(), _proxyManager);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    var response = JsonSerializer.Deserialize<NMoonAnimeSearchResponse>(json, _jsonOptions);
                    var seasons = response?.Seasons?
                        .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Url))
                        .Select(s => new NMoonAnimeSeasonRef
                        {
                            SeasonNumber = s.SeasonNumber <= 0 ? 1 : s.SeasonNumber,
                            Url = s.Url.Trim()
                        })
                        .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(s => s.SeasonNumber)
                        .ToList();

                    if (seasons != null && seasons.Count > 0)
                    {
                        _hybridCache.Set(memKey, seasons, CacheHelper.CacheTime(10, init: _init));
                        return seasons;
                    }
                }
            }
            catch (Exception ex)
            {
                _onLog($"NMoonAnime: помилка пошуку - {ex.Message}");
            }

            return new List<NMoonAnimeSeasonRef>();
        }

        public async Task<NMoonAnimeSeasonContent> GetSeasonContent(NMoonAnimeSeasonRef season)
        {
            if (season == null || string.IsNullOrWhiteSpace(season.Url))
                return null;

            string memKey = $"NMoonAnime:season:{season.Url}";
            if (_hybridCache.TryGetValue(memKey, out NMoonAnimeSeasonContent cached))
                return cached;

            try
            {
                _onLog($"NMoonAnime: завантаження сезону {season.Url}");
                string html = await HttpHelper.GetAsync(_httpHydra, _init, season.Url, DefaultHeaders(), _proxyManager);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var content = ParseSeasonPage(html, season.SeasonNumber, season.Url);
                if (content != null)
                    _hybridCache.Set(memKey, content, CacheHelper.CacheTime(20, init: _init));

                return content;
            }
            catch (Exception ex)
            {
                _onLog($"NMoonAnime: помилка читання сезону - {ex.Message}");
                return null;
            }
        }

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

        private string BuildSearchUrl(string endpoint, string imdbId, string malId, string title, int year)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);

            if (!string.IsNullOrWhiteSpace(malId))
                query["mal_id"] = malId;
            else if (!string.IsNullOrWhiteSpace(imdbId))
                query["imdb_id"] = imdbId;
            else if (!string.IsNullOrWhiteSpace(title))
                query["title"] = title;
            else
                return null;

            if (year > 0)
                query["year"] = year.ToString();

            if (query.Count == 0)
                return null;

            return $"{_init.apihost.TrimEnd('/')}{endpoint}?{query}";
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

        private static JsonNode LoadJsonLoose(string value)
        {
            return PlayerJsDecoder.LoadJsonLoose(value);
        }

        private static string Nullish(string value)
        {
            return PlayerJsDecoder.Nullish(value);
        }

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
    }
}
