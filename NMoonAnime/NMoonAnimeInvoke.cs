using NMoonAnime.Models;
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
        private static readonly Regex _reSeason = new Regex(@"(?:season|сезон)\s*(\d+)|(\d+)\s*(?:season|сезон)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _reEpisode = new Regex(@"(?:episode|серія|серия|епізод|ep)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _reTrailingComma = new Regex(@",\s*([}\]])", RegexOptions.Compiled);
        private static readonly Regex _reAtobLiteral = new Regex(@"atob\(\s*(['""])(?<payload>[A-Za-z0-9+/=]+)\1\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _reJsonParseHelper = new Regex(@"JSON\.parse\(\s*(?<fn>[A-Za-z_$][\w$]*)\(\s*(?<quote>['""])(?<payload>.*?)(\k<quote>)\s*\)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _reHelperCall = new Regex(@"^\s*(?<fn>[A-Za-z_$][\w$]*)\(\s*(?<quote>['""])(?<payload>.*?)(\k<quote>)\s*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly UTF8Encoding _utf8Strict = new UTF8Encoding(false, true);
        private static readonly Encoding _latin1 = Encoding.GetEncoding("ISO-8859-1");

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

            var payload = ExtractPlayerPayload(html);
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

        private PlayerPayload ExtractPlayerPayload(string htmlText)
        {
            string cleanHtml = WebUtility.HtmlDecode(htmlText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(cleanHtml))
                return null;

            var candidates = new List<string> { cleanHtml };
            string decodedScript = DecodeOuterPlayerScript(cleanHtml);
            if (!string.IsNullOrWhiteSpace(decodedScript))
                candidates.Insert(0, decodedScript);

            foreach (string sourceText in candidates)
            {
                string objectText = ExtractObjectByBraces(sourceText, "new Playerjs");
                if (string.IsNullOrWhiteSpace(objectText))
                    objectText = ExtractObjectByBraces(sourceText, "Playerjs({");

                string searchText = string.IsNullOrWhiteSpace(objectText) ? sourceText : objectText;

                string fileValue = ExtractJsValue(searchText, "file");
                if (fileValue == null && !string.IsNullOrWhiteSpace(objectText))
                    fileValue = ExtractJsValue(sourceText, "file");
                if (fileValue == null)
                    continue;

                string titleValue = ExtractJsValue(searchText, "title");
                object parsedFile = ParsePlayerFileValue(fileValue, sourceText);

                return new PlayerPayload
                {
                    Title = Nullish(titleValue),
                    FilePayload = parsedFile
                };
            }

            return null;
        }

        private object ParsePlayerFileValue(string rawValue, string contextText)
        {
            string text = rawValue?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return rawValue;

            if (text.StartsWith("[") || text.StartsWith("{"))
            {
                JsonNode loaded = LoadJsonLoose(text);
                if (loaded != null)
                    return loaded;
            }

            var parseMatch = _reJsonParseHelper.Match(text);
            if (parseMatch.Success)
            {
                string decoded = DecodeHelperPayload(parseMatch.Groups["fn"].Value, parseMatch.Groups["payload"].Value, contextText);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    JsonNode loaded = LoadJsonLoose(decoded);
                    if (loaded != null)
                        return loaded;
                }
            }

            var helperMatch = _reHelperCall.Match(text);
            if (helperMatch.Success)
            {
                string decoded = DecodeHelperPayload(helperMatch.Groups["fn"].Value, helperMatch.Groups["payload"].Value, contextText);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    JsonNode loaded = LoadJsonLoose(decoded);
                    if (loaded != null)
                        return loaded;

                    return decoded;
                }
            }

            return rawValue;
        }

        private string DecodeHelperPayload(string helperName, string payload, string contextText)
        {
            if (string.IsNullOrWhiteSpace(helperName))
                return null;

            if (helperName.Equals("atob", StringComparison.OrdinalIgnoreCase))
            {
                byte[] rawBytes = SafeBase64Decode(payload);
                return rawBytes == null ? null : DecodeBytes(rawBytes);
            }

            string helperKey = ExtractHelperKey(contextText, helperName);
            if (string.IsNullOrWhiteSpace(helperKey))
                return null;

            byte[] keyBytes = Encoding.UTF8.GetBytes(helperKey);
            if (keyBytes.Length == 0)
                return null;

            byte[] payloadBytes = SafeBase64Decode(payload);
            if (payloadBytes == null)
                return null;

            var decoded = new byte[payloadBytes.Length];
            for (int index = 0; index < payloadBytes.Length; index++)
                decoded[index] = (byte)(payloadBytes[index] ^ keyBytes[index % keyBytes.Length]);

            return DecodeBytes(decoded);
        }

        private static string ExtractHelperKey(string contextText, string helperName)
        {
            if (string.IsNullOrWhiteSpace(contextText) || string.IsNullOrWhiteSpace(helperName))
                return null;

            string pattern = $@"function\s+{Regex.Escape(helperName)}\s*\([^)]*\)\s*\{{[\s\S]*?var\s+k\s*=\s*(['""])(?<key>.*?)\1";
            var match = Regex.Match(contextText, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            return Nullish(match.Groups["key"].Value);
        }

        private static string DecodeOuterPlayerScript(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = _reAtobLiteral.Match(text);
            if (!match.Success)
                return null;

            byte[] rawData = SafeBase64Decode(match.Groups["payload"].Value);
            if (rawData == null || rawData.Length <= 32)
                return null;

            byte[] key = rawData.Take(32).ToArray();
            byte[] encryptedData = rawData.Skip(32).ToArray();
            var decoded = new byte[encryptedData.Length];

            for (int index = 0; index < encryptedData.Length; index++)
                decoded[index] = (byte)(encryptedData[index] ^ key[index % key.Length]);

            return DecodeBytes(decoded);
        }

        private static byte[] SafeBase64Decode(string value)
        {
            string text = value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            int remainder = text.Length % 4;
            if (remainder != 0)
                text += new string('=', 4 - remainder);

            try
            {
                return Convert.FromBase64String(text);
            }
            catch
            {
                return null;
            }
        }

        private static string DecodeBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            try
            {
                return _utf8Strict.GetString(data);
            }
            catch
            {
                return _latin1.GetString(data);
            }
        }

        private static string ExtractObjectByBraces(string text, string anchor)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(anchor))
                return null;

            int anchorIndex = text.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
            if (anchorIndex < 0)
                return null;

            int braceIndex = text.IndexOf('{', anchorIndex);
            if (braceIndex < 0)
                return null;

            int depth = 0;
            bool escaped = false;
            char? inString = null;

            for (int index = braceIndex; index < text.Length; index++)
            {
                char current = text[index];

                if (inString.HasValue)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == inString.Value)
                        inString = null;

                    continue;
                }

                if (current == '"' || current == '\'')
                {
                    inString = current;
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                    continue;
                }

                if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(braceIndex + 1, index - braceIndex - 1);
                }
            }

            return null;
        }

        private static string ExtractJsValue(string text, string key)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
                return null;

            var match = Regex.Match(text, $@"\b{Regex.Escape(key)}\b\s*:\s*", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            int index = match.Index + match.Length;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            if (index >= text.Length)
                return null;

            char token = text[index];
            if (token == '"' || token == '\'')
            {
                var (value, _) = ReadJsString(text, index);
                return value;
            }

            if (token == '[')
            {
                int endIndex = FindMatchingBracket(text, index, '[', ']');
                return endIndex >= index ? text.Substring(index, endIndex - index + 1) : null;
            }

            if (token == '{')
            {
                int endIndex = FindMatchingBracket(text, index, '{', '}');
                return endIndex >= index ? text.Substring(index, endIndex - index + 1) : null;
            }

            int stopIndex = index;
            while (stopIndex < text.Length && text[stopIndex] != ',' && text[stopIndex] != '}' && text[stopIndex] != '\n' && text[stopIndex] != '\r')
                stopIndex++;

            return text.Substring(index, stopIndex - index).Trim();
        }

        private static (string value, int nextIndex) ReadJsString(string text, int startIndex)
        {
            if (string.IsNullOrWhiteSpace(text) || startIndex < 0 || startIndex >= text.Length)
                return (null, -1);

            char quote = text[startIndex];
            if (quote != '"' && quote != '\'')
                return (null, -1);

            var buffer = new StringBuilder();
            bool escaped = false;

            for (int index = startIndex + 1; index < text.Length; index++)
            {
                char current = text[index];

                if (escaped)
                {
                    buffer.Append(current);
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quote)
                    return (buffer.ToString(), index + 1);

                buffer.Append(current);
            }

            return (null, -1);
        }

        private static int FindMatchingBracket(string text, int startIndex, char openChar, char closeChar)
        {
            if (string.IsNullOrWhiteSpace(text) || startIndex < 0 || startIndex >= text.Length || text[startIndex] != openChar)
                return -1;

            int depth = 0;
            bool escaped = false;
            char? inString = null;

            for (int index = startIndex; index < text.Length; index++)
            {
                char current = text[index];

                if (inString.HasValue)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == inString.Value)
                        inString = null;

                    continue;
                }

                if (current == '"' || current == '\'')
                {
                    inString = current;
                    continue;
                }

                if (current == openChar)
                {
                    depth++;
                    continue;
                }

                if (current == closeChar)
                {
                    depth--;
                    if (depth == 0)
                        return index;
                }
            }

            return -1;
        }

        private static JsonNode LoadJsonLoose(string value)
        {
            string text = value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string normalized = WebUtility.HtmlDecode(text).Replace("\\/", "/");
            string unescapedQuotes = normalized.Replace("\\'", "'").Replace("\\\"", "\"");
            var candidates = new[]
            {
                normalized,
                unescapedQuotes,
                RemoveTrailingCommas(normalized),
                RemoveTrailingCommas(unescapedQuotes)
            };

            foreach (string candidate in candidates.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                try
                {
                    return JsonNode.Parse(candidate);
                }
                catch
                {
                }
            }

            return null;
        }

        private static string RemoveTrailingCommas(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? value : _reTrailingComma.Replace(value, "$1");
        }

        private static string Nullish(string value)
        {
            string text = value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (text.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("undefined", StringComparison.OrdinalIgnoreCase))
                return null;

            return text;
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

            var match = _reSeason.Match(value);
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

            var episodeMatch = _reEpisode.Match(title);
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

        public static TimeSpan cacheTime(int multiaccess, int home = 5, int mikrotik = 2, OnlinesSettings init = null, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);

            int ctime = AppInit.conf.mikrotik ? mikrotik : AppInit.conf.multiaccess ? init != null && init.cache_time > 0 ? init.cache_time : multiaccess : home;
            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }

        private sealed class PlayerPayload
        {
            public string Title { get; set; }

            public object FilePayload { get; set; }
        }

        private sealed class NMoonAnimeMovieEntry
        {
            public string Title { get; set; }

            public string File { get; set; }
        }
    }
}
