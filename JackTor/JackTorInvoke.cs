using JackTor.Models;
using Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace JackTor
{
    public class JackTorInvoke
    {
        private readonly JackTorSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        private static readonly (string token, string label)[] _voiceTokens = new[]
        {
            ("ukr", "Українська"),
            ("укр", "Українська"),
            ("україн", "Українська"),
            ("eng", "Англійська"),
            ("rus", "Російська"),
            ("дубляж", "Дубляж"),
            ("dub", "Дубляж"),
            ("mvo", "Багатоголосий"),
            ("lostfilm", "LostFilm"),
            ("newstudio", "NewStudio"),
            ("hdrezka", "HDRezka"),
            ("anilibria", "AniLibria")
        };

        public JackTorInvoke(JackTorSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<List<JackTorParsedResult>> Search(string title, string originalTitle, int year, int serial, string originalLanguage)
        {
            string memKey = $"jacktor:search:{serial}:{year}:{(title ?? string.Empty).Trim().ToLowerInvariant()}:{(originalTitle ?? string.Empty).Trim().ToLowerInvariant()}";
            if (_hybridCache.TryGetValue(memKey, out List<JackTorParsedResult> cached))
                return cached;

            var queries = BuildQueries(title, originalTitle, year);
            if (queries.Count == 0)
                return new List<JackTorParsedResult>();

            var rawResults = new List<JackettResult>(256);
            int categoryId = ResolveCategory(serial, originalLanguage);

            foreach (string query in queries)
            {
                var chunk = await SearchRaw(query, categoryId);
                if (chunk != null && chunk.Count > 0)
                    rawResults.AddRange(chunk);
            }

            if (rawResults.Count == 0 && categoryId > 0)
            {
                foreach (string query in queries)
                {
                    var chunk = await SearchRaw(query, 0);
                    if (chunk != null && chunk.Count > 0)
                        rawResults.AddRange(chunk);
                }
            }

            var normalized = NormalizeAndFilter(rawResults, year, serial);
            CacheSources(normalized);
            _hybridCache.Set(memKey, normalized, DateTime.Now.AddMinutes(5));

            _onLog?.Invoke($"JackTor: отримано {rawResults.Count} сирих результатів, після фільтрації лишилось {normalized.Count}");
            return normalized;
        }

        public bool TryGetSource(string rid, out JackTorSourceCache source)
        {
            return _hybridCache.TryGetValue($"jacktor:source:{rid}", out source);
        }

        private async Task<List<JackettResult>> SearchRaw(string query, int categoryId)
        {
            string rawJackett = (_init.jackett ?? _init.host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawJackett))
                return null;

            string endpoint = BuildJackettEndpoint(rawJackett);
            if (string.IsNullOrWhiteSpace(endpoint))
                return null;

            string url = BuildJackettUrl(endpoint, query, categoryId);
            string referer = BuildReferer(rawJackett);
            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", referer)
            };

            try
            {
                _onLog?.Invoke($"JackTor: запит до Jackett -> {query} (cat={categoryId})");
                int timeoutSeconds = Convert.ToInt32(_init.httptimeout);
                if (timeoutSeconds <= 0)
                    timeoutSeconds = 12;

                var (root, response) = await Http.BaseGetAsync<JackettSearchRoot>(
                    _init.cors(url),
                    timeoutSeconds: timeoutSeconds,
                    headers: headers,
                    proxy: _proxyManager.Get(),
                    statusCodeOK: false,
                    IgnoreDeserializeObject: true
                );

                if (response == null || response.StatusCode != HttpStatusCode.OK)
                {
                    _onLog?.Invoke($"JackTor: Jackett відповів статусом {(int?)response?.StatusCode} для {query}");
                    return null;
                }

                if (root?.Results == null || root.Results.Length == 0)
                    return null;

                return root.Results.ToList();
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"JackTor: помилка запиту до Jackett ({query}) -> {ex.Message}");
                return null;
            }
        }

        private string BuildJackettEndpoint(string jackett)
        {
            string source = jackett.Trim().TrimEnd('/');
            if (source.Contains("/api/v2.0/indexers/all/results", StringComparison.OrdinalIgnoreCase))
                return source;

            return $"{source}/api/v2.0/indexers/all/results";
        }

        private string BuildJackettUrl(string endpoint, string query, int categoryId)
        {
            var args = new List<string>(8);

            if (!string.IsNullOrWhiteSpace(_init.apikey) && !HasParam(endpoint, "apikey"))
                args.Add($"apikey={HttpUtility.UrlEncode(_init.apikey)}");

            string encQuery = HttpUtility.UrlEncode(query ?? string.Empty);
            args.Add($"Query={encQuery}");
            args.Add($"query={encQuery}");

            if (categoryId > 0)
            {
                args.Add($"Category[]={categoryId}");
                args.Add($"cat={categoryId}");
            }

            string separator = endpoint.Contains("?") ? "&" : "?";
            return endpoint + separator + string.Join("&", args);
        }

        private bool HasParam(string url, string name)
        {
            return Regex.IsMatch(url ?? string.Empty, $"[\\?&]{Regex.Escape(name)}=", RegexOptions.IgnoreCase);
        }

        private string BuildReferer(string jackett)
        {
            if (Uri.TryCreate(jackett, UriKind.Absolute, out Uri uri))
                return $"{uri.Scheme}://{uri.Authority}/";

            return jackett.Trim().TrimEnd('/') + "/";
        }

        private List<string> BuildQueries(string title, string originalTitle, int year)
        {
            string mode = (_init.query_mode ?? "both").Trim().ToLowerInvariant();
            string cleanTitle = (title ?? string.Empty).Trim();
            string cleanOriginal = (originalTitle ?? string.Empty).Trim();

            var queries = new List<string>(6);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string query)
            {
                if (string.IsNullOrWhiteSpace(query))
                    return;

                string normalized = Regex.Replace(query, "\\s{2,}", " ").Trim();
                if (normalized.Length < 2)
                    return;

                if (unique.Add(normalized))
                    queries.Add(normalized);
            }

            void AddWithYear(string value)
            {
                Add(value);
                if (year > 1900)
                    Add($"{value} {year}");
            }

            if (mode == "original")
            {
                AddWithYear(cleanOriginal);
                AddWithYear(cleanTitle);
            }
            else if (mode == "title")
            {
                AddWithYear(cleanTitle);
                AddWithYear(cleanOriginal);
            }
            else
            {
                AddWithYear(cleanOriginal);
                AddWithYear(cleanTitle);
                Add(cleanOriginal);
                Add(cleanTitle);
            }

            return queries;
        }

        private int ResolveCategory(int serial, string originalLanguage)
        {
            string lang = (originalLanguage ?? string.Empty).Trim().ToLowerInvariant();
            if (lang == "ja" || lang == "jp" || lang.StartsWith("ja-"))
                return 0;

            return serial == 1 ? 5000 : 2000;
        }

        private List<JackTorParsedResult> NormalizeAndFilter(List<JackettResult> rawResults, int year, int serial)
        {
            if (rawResults == null || rawResults.Count == 0)
                return new List<JackTorParsedResult>();

            int yearTolerance = _init.year_tolerance < 0 ? 0 : _init.year_tolerance;
            var allowTrackers = (_init.trackers_allow ?? Array.Empty<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var blockTrackers = (_init.trackers_block ?? Array.Empty<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var byRid = new Dictionary<string, JackTorParsedResult>(StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.UtcNow;

            foreach (var raw in rawResults)
            {
                if (raw == null || string.IsNullOrWhiteSpace(raw.Title))
                    continue;

                string source = !string.IsNullOrWhiteSpace(raw.MagnetUri) ? raw.MagnetUri : raw.Link;
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                string tracker = (raw.Tracker ?? string.Empty).Trim();
                string trackerId = (raw.TrackerId ?? string.Empty).Trim();
                string trackerKey = !string.IsNullOrWhiteSpace(trackerId) ? trackerId.ToLowerInvariant() : tracker.ToLowerInvariant();
                int seeders = raw.Seeders ?? 0;
                int peers = raw.Peers ?? 0;
                long size = raw.Size ?? 0;
                DateTime publishDate = raw.PublishDate ?? DateTime.MinValue;
                double gain = raw.Gain ?? 0;

                if (allowTrackers.Count > 0 && !allowTrackers.Contains(trackerKey) && !allowTrackers.Contains(tracker.ToLowerInvariant()))
                    continue;

                if (blockTrackers.Contains(trackerKey) || blockTrackers.Contains(tracker.ToLowerInvariant()))
                    continue;

                if (seeders < _init.min_sid)
                    continue;

                if (peers < _init.min_peers)
                    continue;

                if (_init.max_serial_size > 0 && _init.max_size > 0)
                {
                    if (serial == 1)
                    {
                        if (size > _init.max_serial_size)
                            continue;
                    }
                    else if (size > _init.max_size)
                    {
                        continue;
                    }
                }
                else if (_init.max_size > 0 && size > _init.max_size)
                {
                    continue;
                }

                if (_init.max_age_days > 0 && publishDate > DateTime.MinValue)
                {
                    DateTime pubUtc = publishDate.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(publishDate, DateTimeKind.Utc)
                        : publishDate.ToUniversalTime();

                    if ((now - pubUtc).TotalDays > _init.max_age_days)
                        continue;
                }

                string searchable = $"{raw.Title} {raw.Description} {raw.CategoryDesc}";
                int quality = ParseQuality(searchable);
                if (!_init.forceAll && quality == 0)
                    continue;

                if (_init.quality_allow != null && _init.quality_allow.Length > 0 && quality > 0 && !_init.quality_allow.Contains(quality))
                    continue;

                string codec = ParseCodec(searchable);
                bool isHdr = IsHdr(searchable);
                bool isDolbyVision = Regex.IsMatch(searchable, "dolby\\s*vision", RegexOptions.IgnoreCase);

                if (!IsHdrModeAllowed(isHdr))
                    continue;

                if (!IsCodecAllowed(codec))
                    continue;

                int extractedYear = ExtractYear(searchable);
                if (year > 1900 && extractedYear > 1900 && Math.Abs(extractedYear - year) > yearTolerance)
                    continue;

                int[] seasons = ParseSeasons(searchable);
                bool serialHint = IsSerialHint(searchable);
                bool movieHint = IsMovieHint(searchable);

                if (serial == 1)
                {
                    if (!serialHint && seasons.Length == 0 && movieHint && !_init.forceAll)
                        continue;
                }
                else
                {
                    if (serialHint && !movieHint && !_init.forceAll)
                        continue;
                }

                string voice = ParseVoice(searchable, trackerKey);
                if (_init.emptyVoice == false && string.IsNullOrWhiteSpace(voice))
                    continue;

                string regexSource = $"{raw.Title}:{voice}";
                if (!string.IsNullOrWhiteSpace(_init.filter) && !IsRegexMatch(regexSource, _init.filter))
                    continue;

                if (!string.IsNullOrWhiteSpace(_init.filter_ignore) && IsRegexMatch(regexSource, _init.filter_ignore))
                    continue;

                string rid = BuildRid(raw.InfoHash, raw.Guid, raw.Details, source);

                var parsed = new JackTorParsedResult()
                {
                    Rid = rid,
                    Title = raw.Title.Trim(),
                    Tracker = tracker,
                    TrackerId = trackerId,
                    SourceUri = source.Trim(),
                    Voice = voice,
                    AudioRank = CalculateAudioRank(voice),
                    Quality = quality,
                    QualityLabel = quality > 0 ? $"{quality}p" : "невідома",
                    MediaInfo = BuildMediaInfo(size, codec, isHdr, isDolbyVision),
                    CategoryDesc = raw.CategoryDesc,
                    Codec = codec,
                    IsHdr = isHdr,
                    IsDolbyVision = isDolbyVision,
                    Seeders = seeders,
                    Peers = peers,
                    Size = size,
                    PublishDate = publishDate,
                    Seasons = seasons,
                    ExtractedYear = extractedYear,
                    Gain = gain
                };

                if (byRid.TryGetValue(rid, out JackTorParsedResult exists))
                {
                    if (Score(parsed) > Score(exists))
                        byRid[rid] = parsed;
                }
                else
                {
                    byRid[rid] = parsed;
                }
            }

            var result = CollapseNearDuplicates(byRid.Values);

            IOrderedEnumerable<JackTorParsedResult> ordered = result
                .OrderByDescending(i => i.AudioRank)
                .ThenByDescending(i => !string.IsNullOrWhiteSpace(i.Voice))
                .ThenByDescending(i => i.SourceUri.StartsWith("magnet:?xt=urn:btih:", StringComparison.OrdinalIgnoreCase));

            string sort = (_init.sort ?? "publishdate").Trim().ToLowerInvariant();
            if (sort == "size")
            {
                ordered = ordered.ThenByDescending(i => i.Size);
            }
            else if (sort == "sid")
            {
                ordered = ordered.ThenByDescending(i => i.Seeders).ThenByDescending(i => i.Peers);
            }
            else
            {
                ordered = ordered.ThenByDescending(i => i.PublishDate).ThenByDescending(i => i.Seeders);
            }

            return ordered.ToList();
        }

        private bool IsRegexMatch(string source, string pattern)
        {
            try
            {
                return Regex.IsMatch(source ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"JackTor: помилка regex '{pattern}' -> {ex.Message}");
                return false;
            }
        }

        private int ParseQuality(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            if (Regex.IsMatch(text, "(2160p|\\b4k\\b|\\buhd\\b)", RegexOptions.IgnoreCase))
                return 2160;

            if (Regex.IsMatch(text, "1080p", RegexOptions.IgnoreCase))
                return 1080;

            if (Regex.IsMatch(text, "720p", RegexOptions.IgnoreCase))
                return 720;

            if (Regex.IsMatch(text, "480p", RegexOptions.IgnoreCase))
                return 480;

            return 0;
        }

        private string ParseCodec(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (Regex.IsMatch(text, "(hevc|h\\.265|x265)", RegexOptions.IgnoreCase))
                return "H.265";

            if (Regex.IsMatch(text, "(h\\.264|x264|avc)", RegexOptions.IgnoreCase))
                return "H.264";

            return string.Empty;
        }

        private bool IsHdr(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, "(hdr10|\\bhdr\\b)", RegexOptions.IgnoreCase);
        }

        private bool IsHdrModeAllowed(bool isHdr)
        {
            string mode = (_init.hdr_mode ?? "any").Trim().ToLowerInvariant();
            return mode switch
            {
                "hdr_only" => isHdr,
                "sdr_only" => !isHdr,
                _ => true,
            };
        }

        private bool IsCodecAllowed(string codec)
        {
            string mode = (_init.codec_allow ?? "any").Trim().ToLowerInvariant();
            return mode switch
            {
                "h265" => codec == "H.265",
                "h264" => codec == "H.264",
                _ => true,
            };
        }

        private int ExtractYear(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            var match = Regex.Match(text, "\\b(19\\d{2}|20\\d{2})\\b");
            if (!match.Success)
                return 0;

            if (int.TryParse(match.Value, out int year))
                return year;

            return 0;
        }

        private int[] ParseSeasons(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<int>();

            var seasons = new HashSet<int>();

            foreach (Match match in Regex.Matches(text, "\\b[sс](\\d{1,2})(?:e\\d{1,2})?\\b", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups[1].Value, out int season) && season > 0 && season < 100)
                    seasons.Add(season);
            }

            foreach (Match match in Regex.Matches(text, "(?:season|сезон)\\s*(\\d{1,2})", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups[1].Value, out int season) && season > 0 && season < 100)
                    seasons.Add(season);
            }

            return seasons.OrderBy(i => i).ToArray();
        }

        private bool IsSerialHint(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, "(\\b[sс]\\d{1,2}e\\d{1,2}\\b|season|сезон|episodes|серії|серии|\\btv\\b)", RegexOptions.IgnoreCase);
        }

        private bool IsMovieHint(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, "(movies|movie|film|фільм|кино)", RegexOptions.IgnoreCase);
        }

        private string ParseVoice(string text, string trackerKey)
        {
            if (string.IsNullOrWhiteSpace(text))
                text = string.Empty;

            string lowered = text.ToLowerInvariant();
            var voices = new List<string>();

            foreach (var (token, label) in _voiceTokens)
            {
                if (lowered.Contains(token) && !voices.Contains(label))
                    voices.Add(label);
            }

            if (voices.Count == 0 && trackerKey == "toloka")
                voices.Add("Українська");

            return string.Join(", ", voices);
        }

        private int CalculateAudioRank(string voice)
        {
            if (string.IsNullOrWhiteSpace(voice))
                return 0;

            var prefs = _init.audio_pref ?? Array.Empty<string>();
            if (prefs.Length == 0)
                return 0;

            string lowered = voice.ToLowerInvariant();
            int best = 0;

            for (int i = 0; i < prefs.Length; i++)
            {
                string pref = (prefs[i] ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(pref))
                    continue;

                if (lowered.Contains(pref))
                {
                    int rank = prefs.Length - i;
                    if (rank > best)
                        best = rank;
                }
            }

            return best;
        }

        private string BuildMediaInfo(long size, string codec, bool isHdr, bool isDolbyVision)
        {
            var parts = new List<string>(4)
            {
                FormatSize(size)
            };

            parts.Add(isHdr ? "HDR" : "SDR");

            if (!string.IsNullOrWhiteSpace(codec))
                parts.Add(codec);

            if (isDolbyVision)
                parts.Add("Dolby Vision");

            return string.Join(" / ", parts.Where(i => !string.IsNullOrWhiteSpace(i)));
        }

        private string FormatSize(long size)
        {
            if (size <= 0)
                return "розмір невідомий";

            double gb = size / 1073741824d;
            if (gb >= 1)
                return $"{gb:0.##} GB";

            double mb = size / 1048576d;
            return $"{mb:0.##} MB";
        }

        private IEnumerable<JackTorParsedResult> CollapseNearDuplicates(IEnumerable<JackTorParsedResult> items)
        {
            var dedup = new Dictionary<string, JackTorParsedResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                string seasonKey = (item.Seasons == null || item.Seasons.Length == 0)
                    ? "-"
                    : string.Join(",", item.Seasons.OrderBy(i => i));

                string key = $"{item.Quality}|{item.Size}|{item.Seeders}|{item.Peers}|{seasonKey}|{NormalizeVoice(item.Voice)}|{NormalizeTitle(item.Title)}";
                if (dedup.TryGetValue(key, out JackTorParsedResult exists))
                {
                    if (Score(item) > Score(exists))
                        dedup[key] = item;
                }
                else
                {
                    dedup[key] = item;
                }
            }

            return dedup.Values;
        }

        private string NormalizeVoice(string voice)
        {
            if (string.IsNullOrWhiteSpace(voice))
                return string.Empty;

            return string.Join(",",
                voice.ToLowerInvariant()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(i => i.Trim())
                    .Distinct()
                    .OrderBy(i => i));
        }

        private string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            string lower = title.ToLowerInvariant();
            lower = Regex.Replace(lower, "\\s+", " ").Trim();
            return lower;
        }

        private int Score(JackTorParsedResult item)
        {
            int qualityScore = item.Quality * 100;
            int seedScore = item.Seeders * 20 + item.Peers * 5;
            int audioScore = item.AudioRank * 200;
            int gainScore = (int)Math.Max(0, Math.Min(item.Gain, 1000));
            int dateScore = item.PublishDate > DateTime.MinValue ? 100 : 0;

            return qualityScore + seedScore + audioScore + gainScore + dateScore;
        }

        private string BuildRid(string infoHash, string guid, string details, string source)
        {
            string hash = (infoHash ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(hash))
                return hash;

            string stableId = !string.IsNullOrWhiteSpace(guid)
                ? guid.Trim()
                : !string.IsNullOrWhiteSpace(details)
                    ? details.Trim()
                    : source ?? string.Empty;

            byte[] sourceBytes = Encoding.UTF8.GetBytes(stableId);
            byte[] digest = SHA1.HashData(sourceBytes);
            return Convert.ToHexString(digest).ToLowerInvariant();
        }

        private void CacheSources(List<JackTorParsedResult> items)
        {
            if (items == null || items.Count == 0)
                return;

            DateTime expires = DateTime.Now.AddHours(36);

            foreach (var item in items)
            {
                var cacheItem = new JackTorSourceCache()
                {
                    Rid = item.Rid,
                    SourceUri = item.SourceUri,
                    Title = item.Title,
                    Voice = item.Voice,
                    Quality = item.Quality,
                    Seasons = item.Seasons
                };

                _hybridCache.Set($"jacktor:source:{item.Rid}", cacheItem, expires);
            }
        }
    }
}
