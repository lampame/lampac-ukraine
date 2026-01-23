using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using StarLight.Models;

namespace StarLight
{
    public class StarLightInvoke
    {
        private const string PlayerApi = "https://vcms-api2.starlight.digital/player-api";
        private const string PlayerReferer = "https://teleportal.ua/";
        private const string Language = "ua";
        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public StarLightInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<List<SearchResult>> Search(string title, string original_title)
        {
            string query = !string.IsNullOrEmpty(title) ? title : original_title;
            if (string.IsNullOrEmpty(query))
                return null;

            string memKey = $"StarLight:search:{query}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchResult> cached))
                return cached;

            string url = $"{_init.host}/{Language}/live-search?q={HttpUtility.UrlEncode(query)}";

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host)
            };

            try
            {
                _onLog?.Invoke($"StarLight search: {url}");
                string payload = await Http.Get(url, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(payload))
                    return null;

                var results = new List<SearchResult>();
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        string typeSlug = item.TryGetProperty("typeSlug", out var typeProp) ? typeProp.GetString() : null;
                        string channelSlug = item.TryGetProperty("channelSlug", out var channelProp) ? channelProp.GetString() : null;
                        string projectSlug = item.TryGetProperty("projectSlug", out var projectProp) ? projectProp.GetString() : null;
                        if (string.IsNullOrEmpty(typeSlug) || string.IsNullOrEmpty(channelSlug) || string.IsNullOrEmpty(projectSlug))
                            continue;

                        string href = $"{_init.host}/{Language}/{typeSlug}/{channelSlug}/{projectSlug}";
                        results.Add(new SearchResult
                        {
                            Title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null,
                            Type = typeSlug,
                            Href = href,
                            Channel = channelSlug,
                            Project = projectSlug
                        });
                    }
                }

                if (results.Count > 0)
                    _hybridCache.Set(memKey, results, cacheTime(15, init: _init));

                return results;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"StarLight search error: {ex.Message}");
                return null;
            }
        }

        public async Task<ProjectInfo> GetProject(string href)
        {
            if (string.IsNullOrEmpty(href))
                return null;

            string memKey = $"StarLight:project:{href}";
            if (_hybridCache.TryGetValue(memKey, out ProjectInfo cached))
                return cached;

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host)
            };

            try
            {
                _onLog?.Invoke($"StarLight project: {href}");
                string payload = await Http.Get(href, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(payload))
                    return null;

                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                var project = new ProjectInfo
                {
                    Title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null,
                    Description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
                    Poster = NormalizeImage(root.TryGetProperty("image", out var imageProp) ? imageProp.GetString() : null),
                    Hash = root.TryGetProperty("hash", out var hashProp) ? hashProp.GetString() : null,
                    Type = root.TryGetProperty("typeSlug", out var typeProp) ? typeProp.GetString() : null,
                    Channel = root.TryGetProperty("channelTitle", out var channelProp) ? channelProp.GetString() : null
                };

                if (root.TryGetProperty("seasons", out var seasonsListProp) && seasonsListProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seasonItem in seasonsListProp.EnumerateArray())
                    {
                        string seasonTitle = seasonItem.TryGetProperty("title", out var sTitle) ? sTitle.GetString() : null;
                        string seasonSlug = seasonItem.TryGetProperty("seasonSlug", out var sSlug) ? sSlug.GetString() : null;
                        AddSeason(project, seasonTitle, seasonSlug);
                    }
                }

                if (root.TryGetProperty("seasonsGallery", out var seasonsProp) && seasonsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seasonItem in seasonsProp.EnumerateArray())
                    {
                        string seasonTitle = seasonItem.TryGetProperty("title", out var sTitle) ? sTitle.GetString() : null;
                        string seasonSlug = seasonItem.TryGetProperty("seasonSlug", out var sSlug) ? sSlug.GetString() : null;
                        AddSeason(project, seasonTitle, seasonSlug);

                        if (seasonItem.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in itemsProp.EnumerateArray())
                            {
                                project.Episodes.Add(new EpisodeInfo
                                {
                                    Title = item.TryGetProperty("title", out var eTitle) ? eTitle.GetString() : null,
                                    Hash = item.TryGetProperty("hash", out var eHash) ? eHash.GetString() : null,
                                    VideoSlug = item.TryGetProperty("videoSlug", out var eSlug) ? eSlug.GetString() : null,
                                    Date = item.TryGetProperty("dateOfBroadcast", out var eDate) ? eDate.GetString() : (item.TryGetProperty("timeUploadVideo", out var eDate2) ? eDate2.GetString() : null),
                                    SeasonSlug = seasonSlug,
                                    Number = ParseEpisodeNumber(item.TryGetProperty("seriesTitle", out var eSeries) ? eSeries.GetString() : null)
                                });
                            }
                        }
                    }
                }

                await LoadMissingSeasonEpisodes(project, href, headers);

                _hybridCache.Set(memKey, project, cacheTime(10, init: _init));
                return project;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"StarLight project error: {ex.Message}");
                return null;
            }
        }

        private async Task LoadMissingSeasonEpisodes(ProjectInfo project, string href, List<HeadersModel> headers)
        {
            if (project == null || string.IsNullOrEmpty(href))
                return;

            var missing = project.Seasons
                .Where(s => !string.IsNullOrEmpty(s.Slug))
                .Where(s => !project.Episodes.Any(e => e.SeasonSlug == s.Slug))
                .ToList();

            foreach (var seasonInfo in missing)
            {
                string seasonUrl = $"{href}/{seasonInfo.Slug}";
                try
                {
                    _onLog?.Invoke($"StarLight season: {seasonUrl}");
                    string payload = await Http.Get(seasonUrl, headers: headers, proxy: _proxyManager.Get());
                    if (string.IsNullOrEmpty(payload))
                        continue;

                    using var document = JsonDocument.Parse(payload);
                    var root = document.RootElement;

                    if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsProp.EnumerateArray())
                        {
                            string hash = item.TryGetProperty("hash", out var eHash) ? eHash.GetString() : null;
                            if (string.IsNullOrEmpty(hash))
                                continue;

                            if (project.Episodes.Any(e => e.SeasonSlug == seasonInfo.Slug && e.Hash == hash))
                                continue;

                            project.Episodes.Add(new EpisodeInfo
                            {
                                Title = item.TryGetProperty("title", out var eTitle) ? eTitle.GetString() : null,
                                Hash = hash,
                                VideoSlug = item.TryGetProperty("videoSlug", out var eSlug) ? eSlug.GetString() : null,
                                Date = item.TryGetProperty("dateOfBroadcast", out var eDate) ? eDate.GetString() : (item.TryGetProperty("timeUploadVideo", out var eDate2) ? eDate2.GetString() : null),
                                SeasonSlug = seasonInfo.Slug,
                                Number = ParseEpisodeNumber(item.TryGetProperty("seriesTitle", out var eSeries) ? eSeries.GetString() : null)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _onLog?.Invoke($"StarLight season error: {ex.Message}");
                }
            }
        }

        private static void AddSeason(ProjectInfo project, string title, string slug)
        {
            if (project == null || string.IsNullOrEmpty(slug))
                return;

            if (project.Seasons.Any(s => s.Slug == slug))
                return;

            project.Seasons.Add(new SeasonInfo { Title = title, Slug = slug });
        }

        public List<EpisodeInfo> GetEpisodes(ProjectInfo project, string seasonSlug)
        {
            if (project == null || project.Seasons.Count == 0)
                return new List<EpisodeInfo>();

            if (string.IsNullOrEmpty(seasonSlug))
                return project.Episodes;

            return project.Episodes.Where(e => e.SeasonSlug == seasonSlug).ToList();
        }

        private static int? ParseEpisodeNumber(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                return number;

            return null;
        }

        public async Task<StreamResult> ResolveStream(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return null;

            string url = $"{PlayerApi}/{hash}?referer={HttpUtility.UrlEncode(PlayerReferer)}&lang={Language}";

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", PlayerReferer)
            };

            try
            {
                _onLog?.Invoke($"StarLight stream: {url}");
                string payload = await Http.Get(url, headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(payload))
                    return null;

                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                string stream = null;
                if (root.TryGetProperty("video", out var videoProp) && videoProp.ValueKind == JsonValueKind.Array)
                {
                    var video = videoProp.EnumerateArray().FirstOrDefault();
                    if (video.ValueKind != JsonValueKind.Undefined)
                    {
                        if (video.TryGetProperty("mediaHlsNoAdv", out var hlsNoAdv))
                            stream = hlsNoAdv.GetString();
                        if (string.IsNullOrEmpty(stream) && video.TryGetProperty("mediaHls", out var hls))
                            stream = hls.GetString();
                        if (string.IsNullOrEmpty(stream) && video.TryGetProperty("media", out var mediaProp) && mediaProp.ValueKind == JsonValueKind.Array)
                        {
                            var media = mediaProp.EnumerateArray().FirstOrDefault();
                            if (media.TryGetProperty("url", out var mediaUrl))
                                stream = mediaUrl.GetString();
                        }
                    }
                }

                if (string.IsNullOrEmpty(stream))
                    return null;

                var multiStreams = ParseMultiHlsStreams(stream);
                if (multiStreams != null && multiStreams.Count > 0)
                    stream = multiStreams[0].link;

                return new StreamResult
                {
                    Stream = stream,
                    Poster = root.TryGetProperty("poster", out var posterProp) ? posterProp.GetString() : null,
                    Name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null,
                    Streams = multiStreams
                };
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"StarLight stream error: {ex.Message}");
                return null;
            }
        }

        private string NormalizeImage(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return path;

            return $"{_init.host}{path}";
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

        private static List<(string link, string quality)> ParseMultiHlsStreams(string streamUrl)
        {
            if (string.IsNullOrEmpty(streamUrl))
                return null;

            if (!streamUrl.Contains("/hls/multi", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri))
                return null;

            var query = HttpUtility.ParseQueryString(uri.Query);
            var files = query.GetValues("file");
            if (files == null || files.Length == 0)
                return null;

            var result = new List<(string link, string quality)>(files.Length);
            var qualityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                if (string.IsNullOrEmpty(file))
                    continue;

                var decoded = HttpUtility.UrlDecode(file);
                var quality = DetectQuality(decoded);
                quality = EnsureUniqueQuality(quality, qualityCounts);
                result.Add((decoded, quality));
            }

            return result.Count > 0 ? result : null;
        }

        private static string DetectQuality(string url)
        {
            if (string.IsNullOrEmpty(url))
                return "auto";

            var lower = url.ToLowerInvariant();
            if (lower.Contains("/lq."))
                return "360";
            if (lower.Contains("/mq."))
                return "480";
            if (lower.Contains("/hq."))
                return "720";
            if (lower.Contains("/sd."))
                return "480";
            if (lower.Contains("/hd."))
                return "720";

            var match = Regex.Match(lower, "(\\d{3,4})p");
            if (match.Success)
                return match.Groups[1].Value;

            return "auto";
        }

        private static string EnsureUniqueQuality(string quality, Dictionary<string, int> counts)
        {
            if (string.IsNullOrEmpty(quality))
                quality = "auto";

            if (!counts.TryGetValue(quality, out var count))
            {
                counts[quality] = 1;
                return quality;
            }

            count++;
            counts[quality] = count;
            return $"{quality}_{count}";
        }
    }
}
