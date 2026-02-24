using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models;
using System.Text.Json;
using System.Linq;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using AnimeON.Models;
using Shared.Engine;

namespace AnimeON
{
    public class AnimeONInvoke
    {
        private static readonly Regex Quality4kRegex = new Regex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase);
        private static readonly Regex QualityFhdRegex = new Regex(@"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b", RegexOptions.IgnoreCase);

        private OnlinesSettings _init;
        private IHybridCache _hybridCache;
        private Action<string> _onLog;
        private ProxyManager _proxyManager;

        public AnimeONInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        string AshdiRequestUrl(string url)
        {
            if (!ApnHelper.IsAshdiUrl(url))
                return url;

            if (!string.IsNullOrWhiteSpace(_init.webcorshost))
                return url;

            return ApnHelper.WrapUrl(_init, url);
        }

        public async Task<List<SearchModel>> Search(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int serial)
        {
            string memKey = $"AnimeON:search:{kinopoisk_id}:{imdb_id}";
            if (_hybridCache.TryGetValue(memKey, out List<SearchModel> res))
                return res;

            try
            {
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) };

                async Task<List<SearchModel>> FindAnime(string query)
                {
                    if (string.IsNullOrEmpty(query))
                        return null;

                    string searchUrl = $"{_init.host}/api/anime/search?text={System.Web.HttpUtility.UrlEncode(query)}";

                    _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {searchUrl}");
                    string searchJson = await Http.Get(_init.cors(searchUrl), headers: headers, proxy: _proxyManager.Get());
                    if (string.IsNullOrEmpty(searchJson))
                        return null;

                    var searchResponse = JsonSerializer.Deserialize<SearchResponseModel>(searchJson);
                    return searchResponse?.Result;
                }

                var searchResults = await FindAnime(title) ?? await FindAnime(original_title);
                if (searchResults == null)
                    return null;

                if (serial == 1 && searchResults.Count > 0)
                {
                    string fallbackTitleEn = searchResults.FirstOrDefault()?.TitleEn;
                    if (!string.IsNullOrWhiteSpace(fallbackTitleEn))
                    {
                        var extraResults = await FindAnime(fallbackTitleEn);
                        if (extraResults != null && extraResults.Count > 0)
                        {
                            searchResults = searchResults
                                .Concat(extraResults)
                                .GroupBy(a => a.Id)
                                .Select(g => g.First())
                                .ToList();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(imdb_id))
                {
                    var seasons = searchResults.Where(a => a.ImdbId == imdb_id).ToList();
                    if (seasons.Count > 0)
                    {
                        _hybridCache.Set(memKey, seasons, cacheTime(5));
                        return seasons;
                    }
                }

                // Fallback to first result if no imdb match
                var firstResult = searchResults.FirstOrDefault();
                if (firstResult != null)
                {
                    var list = new List<SearchModel> { firstResult };
                    _hybridCache.Set(memKey, list, cacheTime(5));
                    return list;
                }

                return null;
            }
            catch (Exception ex)
            {
                _onLog($"AnimeON error: {ex.Message}");
            }
            
            return null;
        }

        public async Task<List<FundubModel>> GetFundubs(int animeId)
        {
            string fundubsUrl = $"{_init.host}/api/player/{animeId}/translations";

            _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {fundubsUrl}");
            string fundubsJson = await Http.Get(_init.cors(fundubsUrl), headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) }, proxy: _proxyManager.Get());
            if (string.IsNullOrEmpty(fundubsJson))
                return null;

            var fundubsResponse = JsonSerializer.Deserialize<FundubsResponseModel>(fundubsJson);
            if (fundubsResponse?.Translations == null || fundubsResponse.Translations.Count == 0)
                return null;

            var fundubs = new List<FundubModel>();
            foreach (var translation in fundubsResponse.Translations)
            {
                var fundubModel = new FundubModel
                {
                    Fundub = translation.Translation,
                    Player = translation.Player
                };
                fundubs.Add(fundubModel);
            }
            return fundubs;
        }

        public async Task<EpisodeModel> GetEpisodes(int animeId, int playerId, int fundubId)
        {
            string episodesUrl = $"{_init.host}/api/player/{animeId}/episodes?take=100&skip=-1&playerId={playerId}&translationId={fundubId}";

            _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {episodesUrl}");
            string episodesJson = await Http.Get(_init.cors(episodesUrl), headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) }, proxy: _proxyManager.Get());
            if (string.IsNullOrEmpty(episodesJson))
                return null;

            return JsonSerializer.Deserialize<EpisodeModel>(episodesJson);
        }

        public async Task<string> ParseMoonAnimePage(string url)
        {
            try
            {
                string requestUrl = $"{url}?player=animeon.club";
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", "https://animeon.club/")
                };

                _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
                string html = await Http.Get(_init.cors(requestUrl), headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return null;

                var match = System.Text.RegularExpressions.Regex.Match(html, @"file:\s*""([^""]+\.m3u8)""");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                _onLog($"AnimeON ParseMoonAnimePage error: {ex.Message}");
            }

            return null;
        }

        public async Task<string> ParseAshdiPage(string url, bool disableAshdiMultivoiceForVod = false)
        {
            var streams = await ParseAshdiPageStreams(url, disableAshdiMultivoiceForVod);
            return streams?.FirstOrDefault().link;
        }

        public async Task<List<(string title, string link)>> ParseAshdiPageStreams(string url, bool disableAshdiMultivoiceForVod = false)
        {
            var streams = new List<(string title, string link)>();
            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", "https://ashdi.vip/")
                };

                string requestUrl = AshdiRequestUrl(WithAshdiMultivoice(url, enable: !disableAshdiMultivoiceForVod));
                _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
                string html = await Http.Get(_init.cors(requestUrl), headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return streams;

                string rawArray = ExtractPlayerFileArray(html);
                if (!string.IsNullOrWhiteSpace(rawArray))
                {
                    string json = WebUtility.HtmlDecode(rawArray)
                        .Replace("\\/", "/")
                        .Replace("\\'", "'")
                        .Replace("\\\"", "\"");

                    using var jsonDoc = JsonDocument.Parse(json);
                    if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        int index = 1;
                        foreach (var item in jsonDoc.RootElement.EnumerateArray())
                        {
                            if (!item.TryGetProperty("file", out var fileProp))
                                continue;

                            string file = fileProp.GetString();
                            if (string.IsNullOrWhiteSpace(file))
                                continue;

                            string rawTitle = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                            streams.Add((BuildDisplayTitle(rawTitle, file, index), file));
                            index++;
                        }

                        if (streams.Count > 0)
                            return streams;
                    }
                }

                var match = Regex.Match(html, @"file\s*:\s*['""]([^'""]+)['""]");
                if (match.Success)
                {
                    string file = match.Groups[1].Value;
                    streams.Add((BuildDisplayTitle("Основне джерело", file, 1), file));
                }
            }
            catch (Exception ex)
            {
                _onLog($"AnimeON ParseAshdiPage error: {ex.Message}");
            }

            return streams;
        }

        public async Task<string> ResolveEpisodeStream(int episodeId, bool disableAshdiMultivoiceForVod = false)
        {
            try
            {
                string url = $"{_init.host}/api/player/{episodeId}/episode";

                _onLog($"AnimeON: using proxy {_proxyManager.CurrentProxyIp} for {url}");
                string json = await Http.Get(_init.cors(url), headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) }, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(json))
                    return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("fileUrl", out var fileProp))
                {
                    string fileUrl = fileProp.GetString();
                    if (!string.IsNullOrEmpty(fileUrl))
                        return fileUrl;
                }

                if (root.TryGetProperty("videoUrl", out var videoProp))
                {
                    string videoUrl = videoProp.GetString();
                    return await ResolveVideoUrl(videoUrl, disableAshdiMultivoiceForVod);
                }
            }
            catch (Exception ex)
            {
                _onLog($"AnimeON ResolveEpisodeStream error: {ex.Message}");
            }

            return null;
        }

        public async Task<string> ResolveVideoUrl(string url, bool disableAshdiMultivoiceForVod = false)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (url.Contains("moonanime.art"))
                return await ParseMoonAnimePage(url);

            if (url.Contains("ashdi.vip/vod"))
                return await ParseAshdiPage(url, disableAshdiMultivoiceForVod);

            return url;
        }

        private static string WithAshdiMultivoice(string url, bool enable = true)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            if (!enable)
                return url;

            if (url.IndexOf("ashdi.vip/vod/", StringComparison.OrdinalIgnoreCase) < 0)
                return url;

            if (url.IndexOf("multivoice", StringComparison.OrdinalIgnoreCase) >= 0)
                return url;

            return url.Contains("?") ? $"{url}&multivoice" : $"{url}?multivoice";
        }

        private static string BuildDisplayTitle(string rawTitle, string link, int index)
        {
            string normalized = string.IsNullOrWhiteSpace(rawTitle) ? $"Варіант {index}" : StripMoviePrefix(WebUtility.HtmlDecode(rawTitle).Trim());
            string qualityTag = DetectQualityTag($"{normalized} {link}");
            if (string.IsNullOrWhiteSpace(qualityTag))
                return normalized;

            if (normalized.StartsWith("[4K]", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("[FHD]", StringComparison.OrdinalIgnoreCase))
                return normalized;

            return $"{qualityTag} {normalized}";
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

        private static string ExtractPlayerFileArray(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            int searchIndex = 0;
            while (searchIndex >= 0 && searchIndex < html.Length)
            {
                int fileIndex = html.IndexOf("file", searchIndex, StringComparison.OrdinalIgnoreCase);
                if (fileIndex < 0)
                    return null;

                int colonIndex = html.IndexOf(':', fileIndex);
                if (colonIndex < 0)
                    return null;

                int startIndex = colonIndex + 1;
                while (startIndex < html.Length && char.IsWhiteSpace(html[startIndex]))
                    startIndex++;

                if (startIndex < html.Length && (html[startIndex] == '\'' || html[startIndex] == '"'))
                {
                    startIndex++;
                    while (startIndex < html.Length && char.IsWhiteSpace(html[startIndex]))
                        startIndex++;
                }

                if (startIndex >= html.Length || html[startIndex] != '[')
                {
                    searchIndex = fileIndex + 4;
                    continue;
                }

                return ExtractBracketArray(html, startIndex);
            }

            return null;
        }

        private static string ExtractBracketArray(string text, int startIndex)
        {
            if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '[')
                return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            char quoteChar = '\0';

            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escaped = true;
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

        public static TimeSpan cacheTime(int multiaccess, int home = 5, int mikrotik = 2, OnlinesSettings init = null, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);

            int ctime = AppInit.conf.mikrotik ? mikrotik : AppInit.conf.multiaccess ? init != null && init.cache_time > 0 ? init.cache_time : multiaccess : home;
            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }
        public async Task<AnimeON.Models.AnimeONAggregatedStructure> AggregateSerialStructure(int animeId, int season)
        {
            string memKey = $"AnimeON:aggregated:{animeId}:{season}";
            if (_hybridCache.TryGetValue(memKey, out AnimeON.Models.AnimeONAggregatedStructure cached))
                return cached;

            try
            {
                var structure = new AnimeON.Models.AnimeONAggregatedStructure
                {
                    AnimeId = animeId,
                    Season = season,
                    Voices = new Dictionary<string, AnimeON.Models.AnimeONVoiceInfo>()
                };

                var fundubs = await GetFundubs(animeId);
                if (fundubs == null || fundubs.Count == 0)
                    return null;

                foreach (var fundub in fundubs)
                {
                    if (fundub?.Fundub == null || fundub.Player == null)
                        continue;

                    foreach (var player in fundub.Player)
                    {
                        string display = $"[{player.Name}] {fundub.Fundub.Name}";

                        var episodesData = await GetEpisodes(animeId, player.Id, fundub.Fundub.Id);
                        if (episodesData?.Episodes == null || episodesData.Episodes.Count == 0)
                            continue;

                        var voiceInfo = new AnimeON.Models.AnimeONVoiceInfo
                        {
                            Name = fundub.Fundub.Name,
                            PlayerType = player.Name?.ToLower(),
                            DisplayName = display,
                            PlayerId = player.Id,
                            FundubId = fundub.Fundub.Id,
                            Episodes = episodesData.Episodes
                                .OrderBy(ep => ep.EpisodeNum)
                                .Select(ep => new AnimeON.Models.AnimeONEpisodeInfo
                                {
                                    Number = ep.EpisodeNum,
                                    Title = ep.Name,
                                    Hls = ep.Hls,
                                    VideoUrl = ep.VideoUrl,
                                    EpisodeId = ep.Id
                                })
                                .ToList()
                        };

                        structure.Voices[display] = voiceInfo;
                    }
                }

                if (!structure.Voices.Any())
                    return null;

                _hybridCache.Set(memKey, structure, cacheTime(20, init: _init));
                return structure;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AnimeON AggregateSerialStructure error: {ex.Message}");
                return null;
            }
        }
    }
}
