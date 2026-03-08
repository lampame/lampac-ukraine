using NMoonAnime.Models;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace NMoonAnime
{
    public class NMoonAnimeInvoke
    {
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public NMoonAnimeInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
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
                    string json = await Http.Get(_init.cors(searchUrl), headers: DefaultHeaders(), proxy: _proxyManager.Get());
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
                        _hybridCache.Set(memKey, seasons, cacheTime(10, init: _init));
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
                string html = await Http.Get(_init.cors(season.Url), headers: DefaultHeaders(), proxy: _proxyManager.Get());
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var content = ParseSeasonPage(html, season.SeasonNumber, season.Url);
                if (content != null)
                    _hybridCache.Set(memKey, content, cacheTime(20, init: _init));

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

            string fileArrayJson = ExtractFileArrayJson(html);
            if (string.IsNullOrWhiteSpace(fileArrayJson))
                return content;

            using var doc = JsonDocument.Parse(fileArrayJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return content;

            int voiceIndex = 1;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                var voice = new NMoonAnimeVoiceContent
                {
                    Name = NormalizeVoiceName(GetStringProperty(entry, "title"), voiceIndex)
                };

                if (entry.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.Array)
                {
                    int episodeIndex = 1;
                    foreach (var episodeEntry in folder.EnumerateArray())
                    {
                        if (episodeEntry.ValueKind != JsonValueKind.Object)
                            continue;

                        string file = GetStringProperty(episodeEntry, "file");
                        if (string.IsNullOrWhiteSpace(file))
                            continue;

                        string episodeTitle = GetStringProperty(episodeEntry, "title");
                        int episodeNumber = ParseEpisodeNumber(episodeTitle, episodeIndex);

                        voice.Episodes.Add(new NMoonAnimeEpisodeContent
                        {
                            Name = string.IsNullOrWhiteSpace(episodeTitle) ? $"Епізод {episodeNumber}" : WebUtility.HtmlDecode(episodeTitle),
                            Number = episodeNumber,
                            File = file
                        });

                        episodeIndex++;
                    }

                    if (voice.Episodes.Count > 0)
                    {
                        content.IsSeries = true;
                        voice.Episodes = voice.Episodes
                            .OrderBy(e => e.Number <= 0 ? int.MaxValue : e.Number)
                            .ThenBy(e => e.Name)
                            .ToList();
                    }
                }
                else
                {
                    voice.MovieFile = GetStringProperty(entry, "file");
                }

                if (!string.IsNullOrWhiteSpace(voice.MovieFile) || voice.Episodes.Count > 0)
                    content.Voices.Add(voice);

                voiceIndex++;
            }

            return content;
        }

        private static string NormalizeVoiceName(string source, int fallbackIndex)
        {
            string voice = WebUtility.HtmlDecode(source ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(voice) ? $"Озвучка {fallbackIndex}" : voice;
        }

        private static int ParseEpisodeNumber(string title, int fallback)
        {
            if (string.IsNullOrWhiteSpace(title))
                return fallback;

            var match = Regex.Match(title, @"\d+");
            if (match.Success && int.TryParse(match.Value, out int number))
                return number;

            return fallback;
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

        private static string GetStringProperty(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string ExtractFileArrayJson(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var match = Regex.Match(html, @"file\s*:\s*(\[[\s\S]*?\])\s*,\s*skip\s*:", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;

            int fileIndex = html.IndexOf("file", StringComparison.OrdinalIgnoreCase);
            if (fileIndex < 0)
                return null;

            int colonIndex = html.IndexOf(':', fileIndex);
            if (colonIndex < 0)
                return null;

            int arrayIndex = html.IndexOf('[', colonIndex);
            if (arrayIndex < 0)
                return null;

            return ExtractBracketArray(html, arrayIndex);
        }

        private static string ExtractBracketArray(string source, int startIndex)
        {
            bool inString = false;
            bool escaped = false;
            char stringChar = '\0';
            int depth = 0;
            int begin = -1;

            for (int i = startIndex; i < source.Length; i++)
            {
                char c = source[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == stringChar)
                    {
                        inString = false;
                        stringChar = '\0';
                    }

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    stringChar = c;
                    continue;
                }

                if (c == '[')
                {
                    if (depth == 0)
                        begin = i;

                    depth++;
                    continue;
                }

                if (c == ']')
                {
                    depth--;
                    if (depth == 0 && begin >= 0)
                        return source.Substring(begin, i - begin + 1);
                }
            }

            return null;
        }

        private List<HeadersModel> DefaultHeaders()
        {
            return new List<HeadersModel>
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host)
            };
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
