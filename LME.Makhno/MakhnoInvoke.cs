using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using LME.Makhno.Models;

namespace LME.Makhno
{
    public class MakhnoInvoke
    {
        private const string WormholeHost = "https://wh.lme.isroot.in/";

        private static readonly Regex Quality4kRegex = new Regex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        public MakhnoInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        public async Task<string> GetWormholePlay(string imdbId)
        {
            if (string.IsNullOrWhiteSpace(imdbId))
                return null;

            string url = $"{WormholeHost}?imdb_id={imdbId}";
            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent)
                };

                string response = await HttpHelper.GetAsync(_httpHydra, _init, url, headers, _proxyManager);
                if (string.IsNullOrWhiteSpace(response))
                    return null;

                var payload = JsonSerializer.Deserialize<WormholeResponse>(response);
                return string.IsNullOrWhiteSpace(payload?.play) ? null : payload.play;
            }
            catch (Exception ex)
            {
                _onLog($"lme_makhno wormhole error: {ex.Message}");
                return null;
            }
        }

        public async Task<PlayerData> GetPlayerData(string playerUrl)
        {
            if (string.IsNullOrEmpty(playerUrl))
                return null;

            try
            {
                string sourceUrl = ApnExtensions.WithAshdiMultivoice(playerUrl);
                string requestUrl = sourceUrl;
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", Http.UserAgent)
                };

                if (sourceUrl.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase))
                {
                    headers.Add(new HeadersModel("Referer", "https://ashdi.vip/"));
                }

                if (ApnHelper.IsAshdiUrl(sourceUrl) && ApnHelper.IsEnabled(_init) && string.IsNullOrWhiteSpace(_init.webcorshost))
                    requestUrl = ApnHelper.WrapUrl(_init, sourceUrl);

                _onLog($"lme_makhno getting player data from: {requestUrl}");

                var response = await HttpHelper.GetAsync(_httpHydra, _init, requestUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(response))
                    return null;

                return ParsePlayerData(response);
            }
            catch (Exception ex)
            {
                _onLog($"Makhno GetPlayerData error: {ex.Message}");
                return null;
            }
        }

        private PlayerData ParsePlayerData(string html)
        {
            try
            {
                if (string.IsNullOrEmpty(html))
                    return null;

                var fileMatch = Regex.Match(html, @"file:'([^']+)'", RegexOptions.IgnoreCase);
                if (!fileMatch.Success)
                    fileMatch = Regex.Match(html, @"file:\s*""([^""]+)""", RegexOptions.IgnoreCase);

                if (fileMatch.Success && !fileMatch.Groups[1].Value.StartsWith("["))
                {
                    string file = fileMatch.Groups[1].Value;
                    var posterMatch = Regex.Match(html, @"poster:[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    var subtitles = ApnHelper.ParseSubtitles(ApnHelper.ExtractPlayerSubtitle(html));

                    return new PlayerData
                    {
                        File = file,
                        Poster = posterMatch.Success ? posterMatch.Groups[1].Value : null,
                        Voices = new List<Voice>(),
                        Subtitles = subtitles,
                        Movies = new List<MovieVariant>()
                        {
                            new MovieVariant
                            {
                                File = file,
                                Quality = QualityHelper.DetectQualityTag(file) ?? "auto",
                                Title = QualityHelper.BuildDisplayTitle("Основне джерело", file, 1),
                                Subtitles = subtitles
                            }
                        }
                    };
                }

                string jsonData = ExtractPlayerJson(html);
                if (!string.IsNullOrEmpty(jsonData))
                {
                    var voices = ParseVoicesJson(jsonData);
                    var movies = ParseMovieVariantsJson(jsonData);
                    _onLog($"lme_makhno ParsePlayerData: voices={voices?.Count ?? 0}, movies={movies?.Count ?? 0}");
                    return new PlayerData
                    {
                        File = movies.FirstOrDefault()?.File,
                        Poster = null,
                        Voices = voices,
                        Movies = movies
                    };
                }

                var m3u8Match = Regex.Match(html, @"(https?://[^""'\s>]+\.m3u8[^""'\s>]*)", RegexOptions.IgnoreCase);
                if (m3u8Match.Success)
                {
                    _onLog("lme_makhno ParsePlayerData: fallback m3u8 match");
                    return new PlayerData
                    {
                        File = m3u8Match.Groups[1].Value,
                        Poster = null,
                        Voices = new List<Voice>(),
                        Movies = new List<MovieVariant>()
                        {
                            new MovieVariant
                            {
                                File = m3u8Match.Groups[1].Value,
                                Quality = QualityHelper.DetectQualityTag(m3u8Match.Groups[1].Value) ?? "auto",
                                Title = QualityHelper.BuildDisplayTitle("Основне джерело", m3u8Match.Groups[1].Value, 1)
                            }
                        }
                    };
                }

                var sourceMatch = Regex.Match(html, @"<source[^>]*src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (sourceMatch.Success)
                {
                    _onLog("lme_makhno ParsePlayerData: fallback source match");
                    return new PlayerData
                    {
                        File = sourceMatch.Groups[1].Value,
                        Poster = null,
                        Voices = new List<Voice>(),
                        Movies = new List<MovieVariant>()
                        {
                            new MovieVariant
                            {
                                File = sourceMatch.Groups[1].Value,
                                Quality = QualityHelper.DetectQualityTag(sourceMatch.Groups[1].Value) ?? "auto",
                                Title = QualityHelper.BuildDisplayTitle("Основне джерело", sourceMatch.Groups[1].Value, 1)
                            }
                        }
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _onLog($"lme_makhno ParsePlayerData error: {ex.Message}");
                return null;
            }
        }

        private List<Voice> ParseVoicesJson(string jsonData)
        {
            try
            {
                var voicesArray = JsonSerializer.Deserialize<List<JsonObject>>(jsonData);
                var voices = new List<Voice>();

                if (voicesArray == null)
                    return voices;

                foreach (var voiceGroup in voicesArray)
                {
                    string rawVoiceName = voiceGroup["title"]?.ToString() ?? string.Empty;
                    var voice = new Voice
                    {
                        Name = rawVoiceName,
                        Seasons = new List<Season>()
                    };

                    bool has4k = false;
                    var seasons = voiceGroup["folder"] as JsonArray;
                    if (seasons != null)
                    {
                        foreach (var seasonGroup in seasons)
                        {
                            string seasonTitle = seasonGroup["title"]?.ToString() ?? string.Empty;
                            var episodes = new List<Episode>();

                            var episodesArray = seasonGroup["folder"] as JsonArray;
                            if (episodesArray != null)
                            {
                                foreach (var episode in episodesArray)
                                {
                                    string rawTitle = episode["title"]?.ToString();
                                    string file = episode["file"]?.ToString();

                                    // Перевіряємо наявність 4K у файлі епізоду (на рівні озвучки)
                                    if (!has4k && file != null)
                                        has4k = Quality4kRegex.IsMatch(file);

                                    episodes.Add(new Episode
                                    {
                                        Id = episode["id"]?.ToString(),
                                        Title = string.IsNullOrWhiteSpace(rawTitle) ? $"Епізод {episodes.Count + 1}" : rawTitle,
                                        File = file,
                                        Poster = episode["poster"]?.ToString(),
                                        Subtitle = episode["subtitle"]?.ToString(),
                                        Subtitles = ApnHelper.ParseSubtitles(episode["subtitle"]?.ToString())
                                    });
                                }
                            }

                            episodes = episodes
                                .OrderBy(item => ExtractEpisodeNumber(item.Title) is null)
                                .ThenBy(item => ExtractEpisodeNumber(item.Title) ?? 0)
                                .ToList();

                            voice.Seasons.Add(new Season
                            {
                                Title = seasonTitle,
                                Episodes = episodes
                            });
                        }
                    }

                    // Додаємо маркер 4K до назви озвучки, якщо хоч один епізод має 4K
                    if (has4k)
                    {
                        string trimmedName = rawVoiceName.Trim();
                        if (!trimmedName.StartsWith("[4K]", StringComparison.OrdinalIgnoreCase))
                            voice.Name = $"[4K] {trimmedName}";
                    }

                    voices.Add(voice);
                }

                return voices;
            }
            catch (Exception ex)
            {
                _onLog($"lme_makhno ParseVoicesJson error: {ex.Message}");
                return new List<Voice>();
            }
        }

        private List<MovieVariant> ParseMovieVariantsJson(string jsonData)
        {
            try
            {
                var voicesArray = JsonSerializer.Deserialize<List<JsonObject>>(jsonData);
                var movies = new List<MovieVariant>();
                if (voicesArray == null || voicesArray.Count == 0)
                    return movies;

                int index = 1;
                foreach (var item in voicesArray)
                {
                    string file = item?["file"]?.ToString();
                    if (string.IsNullOrWhiteSpace(file))
                        continue;

                    string rawTitle = item["title"]?.ToString();
                    movies.Add(new MovieVariant
                    {
                        File = file,
                        Quality = QualityHelper.DetectQualityTag($"{rawTitle} {file}") ?? "auto",
                        Title = QualityHelper.BuildDisplayTitle(rawTitle, file, index),
                        Subtitles = ApnHelper.ParseSubtitles(item["subtitle"]?.ToString())
                    });
                    index++;
                }

                return movies;
            }
            catch (Exception ex)
            {
                _onLog($"lme_makhno ParseMovieVariantsJson error: {ex.Message}");
                return new List<MovieVariant>();
            }
        }

        private string ExtractPlayerJson(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var startIndex = FindFileArrayStart(html);
            if (startIndex < 0)
                return null;

            string jsonArray = AshdiParser.ExtractBracketArray(html, startIndex);
            if (string.IsNullOrEmpty(jsonArray))
                return null;

            return jsonArray
                .Replace("\\'", "'")
                .Replace("\\\"", "\"");
        }

        private int FindFileArrayStart(string html)
        {
            int playerStart = html.IndexOf("Playerjs", StringComparison.OrdinalIgnoreCase);
            if (playerStart >= 0)
            {
                int playerIndex = FindFileArrayStartInRange(html, playerStart);
                if (playerIndex >= 0)
                    return playerIndex;
            }

            int index = FindFileArrayIndex(html, "file:'[");
            if (index >= 0)
                return index;

            index = FindFileArrayIndex(html, "file:\"[");
            if (index >= 0)
                return index;

            var match = Regex.Match(html, @"file\s*:\s*'?\[", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Index + match.Value.LastIndexOf('[');

            return -1;
        }

        private int FindFileArrayStartInRange(string html, int startIndex)
        {
            int searchStart = startIndex;
            int searchEnd = Math.Min(html.Length, startIndex + 200000);

            int tokenIndex = IndexOfIgnoreCase(html, "file:'[", searchStart, searchEnd);
            if (tokenIndex >= 0)
                return html.IndexOf('[', tokenIndex);

            tokenIndex = IndexOfIgnoreCase(html, "file:\"[", searchStart, searchEnd);
            if (tokenIndex >= 0)
                return html.IndexOf('[', tokenIndex);

            tokenIndex = IndexOfIgnoreCase(html, "file", searchStart, searchEnd);
            if (tokenIndex >= 0)
            {
                int bracketIndex = html.IndexOf('[', tokenIndex);
                if (bracketIndex >= 0 && bracketIndex < searchEnd)
                    return bracketIndex;
            }

            return -1;
        }

        private int IndexOfIgnoreCase(string text, string value, int startIndex, int endIndex)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < endIndex)
                return index;

            return -1;
        }

        private int FindFileArrayIndex(string html, string token)
        {
            int tokenIndex = html.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0)
                return -1;

            int bracketIndex = html.IndexOf('[', tokenIndex);
            return bracketIndex;
        }

        private int? ExtractEpisodeNumber(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var match = Regex.Match(value, @"(\d+)");
            if (!match.Success)
                return null;

            if (int.TryParse(match.Groups[1].Value, out int num))
                return num;

            return null;
        }

        private class WormholeResponse
        {
            public string play { get; set; }
        }
    }
}
