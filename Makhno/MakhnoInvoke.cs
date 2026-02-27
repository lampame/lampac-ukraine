using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Makhno.Models;

namespace Makhno
{
    public class MakhnoInvoke
    {
        private const string WormholeHost = "https://wh.lme.isroot.in/";
        private static readonly Regex Quality4kRegex = new Regex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase);
        private static readonly Regex QualityFhdRegex = new Regex(@"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b", RegexOptions.IgnoreCase);

        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public MakhnoInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
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

                string response = await Http.Get(_init.cors(url), timeoutSeconds: 4, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrWhiteSpace(response))
                    return null;

                var payload = JsonConvert.DeserializeObject<WormholeResponse>(response);
                return string.IsNullOrWhiteSpace(payload?.play) ? null : payload.play;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno wormhole error: {ex.Message}");
                return null;
            }
        }

        public async Task<PlayerData> GetPlayerData(string playerUrl)
        {
            if (string.IsNullOrEmpty(playerUrl))
                return null;

            try
            {
                string sourceUrl = WithAshdiMultivoice(playerUrl);
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

                _onLog($"Makhno getting player data from: {requestUrl}");

                var response = await Http.Get(_init.cors(requestUrl), headers: headers, proxy: _proxyManager.Get());
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
                    return new PlayerData
                    {
                        File = file,
                        Poster = posterMatch.Success ? posterMatch.Groups[1].Value : null,
                        Voices = new List<Voice>(),
                        Movies = new List<MovieVariant>()
                        {
                            new MovieVariant
                            {
                                File = file,
                                Quality = DetectQualityTag(file) ?? "auto",
                                Title = BuildMovieTitle("Основне джерело", file, 1)
                            }
                        }
                    };
                }

                string jsonData = ExtractPlayerJson(html);
                if (jsonData == null)
                    _onLog("Makhno ParsePlayerData: file array not found");
                else
                    _onLog($"Makhno ParsePlayerData: file array length={jsonData.Length}");
                if (!string.IsNullOrEmpty(jsonData))
                {
                    var voices = ParseVoicesJson(jsonData);
                    var movies = ParseMovieVariantsJson(jsonData);
                    _onLog($"Makhno ParsePlayerData: voices={voices?.Count ?? 0}");
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
                    _onLog("Makhno ParsePlayerData: fallback m3u8 match");
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
                                Quality = DetectQualityTag(m3u8Match.Groups[1].Value) ?? "auto",
                                Title = BuildMovieTitle("Основне джерело", m3u8Match.Groups[1].Value, 1)
                            }
                        }
                    };
                }

                var sourceMatch = Regex.Match(html, @"<source[^>]*src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (sourceMatch.Success)
                {
                    _onLog("Makhno ParsePlayerData: fallback source match");
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
                                Quality = DetectQualityTag(sourceMatch.Groups[1].Value) ?? "auto",
                                Title = BuildMovieTitle("Основне джерело", sourceMatch.Groups[1].Value, 1)
                            }
                        }
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno ParsePlayerData error: {ex.Message}");
                return null;
            }
        }

        private List<Voice> ParseVoicesJson(string jsonData)
        {
            try
            {
                var voicesArray = JsonConvert.DeserializeObject<List<JObject>>(jsonData);
                var voices = new List<Voice>();

                if (voicesArray == null)
                    return voices;

                foreach (var voiceGroup in voicesArray)
                {
                    var voice = new Voice
                    {
                        Name = voiceGroup["title"]?.ToString(),
                        Seasons = new List<Season>()
                    };

                    var seasons = voiceGroup["folder"] as JArray;
                    if (seasons != null)
                    {
                        foreach (var seasonGroup in seasons)
                        {
                            string seasonTitle = seasonGroup["title"]?.ToString() ?? string.Empty;
                            var episodes = new List<Episode>();

                            var episodesArray = seasonGroup["folder"] as JArray;
                            if (episodesArray != null)
                            {
                                foreach (var episode in episodesArray)
                                {
                                    episodes.Add(new Episode
                                    {
                                        Id = episode["id"]?.ToString(),
                                        Title = episode["title"]?.ToString(),
                                        File = episode["file"]?.ToString(),
                                        Poster = episode["poster"]?.ToString(),
                                        Subtitle = episode["subtitle"]?.ToString()
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

                    voices.Add(voice);
                }

                return voices;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno ParseVoicesJson error: {ex.Message}");
                return new List<Voice>();
            }
        }

        private List<MovieVariant> ParseMovieVariantsJson(string jsonData)
        {
            try
            {
                var voicesArray = JsonConvert.DeserializeObject<List<JObject>>(jsonData);
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
                        Quality = DetectQualityTag($"{rawTitle} {file}") ?? "auto",
                        Title = BuildMovieTitle(rawTitle, file, index)
                    });
                    index++;
                }

                return movies;
            }
            catch (Exception ex)
            {
                _onLog($"Makhno ParseMovieVariantsJson error: {ex.Message}");
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

            string jsonArray = ExtractBracketArray(html, startIndex);
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

        private string ExtractBracketArray(string text, int startIndex)
        {
            if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '[')
                return null;

            int depth = 0;
            bool inString = false;
            bool escape = false;
            char quoteChar = '\0';

            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escape = true;
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

        private static string BuildMovieTitle(string rawTitle, string file, int index)
        {
            string title = string.IsNullOrWhiteSpace(rawTitle) ? $"Варіант {index}" : StripMoviePrefix(WebUtility.HtmlDecode(rawTitle).Trim());
            string qualityTag = DetectQualityTag($"{title} {file}");

            if (string.IsNullOrWhiteSpace(qualityTag))
                return title;

            if (title.StartsWith("[4K]", StringComparison.OrdinalIgnoreCase) || title.StartsWith("[FHD]", StringComparison.OrdinalIgnoreCase))
                return title;

            return $"{qualityTag} {title}";
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

        private class WormholeResponse
        {
            public string play { get; set; }
        }
    }
}
