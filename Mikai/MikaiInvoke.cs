using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Net;
using System.Text.RegularExpressions;
using Mikai.Models;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;

namespace Mikai
{
    public class MikaiInvoke
    {
        private static readonly Regex Quality4kRegex = new Regex(@"(^|[^0-9])(2160p?)([^0-9]|$)|\b4k\b|\buhd\b", RegexOptions.IgnoreCase);
        private static readonly Regex QualityFhdRegex = new Regex(@"(^|[^0-9])(1080p?)([^0-9]|$)|\bfhd\b", RegexOptions.IgnoreCase);

        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;

        public MikaiInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<List<MikaiAnime>> Search(string title, string original_title, int year)
        {
            string memKey = $"Mikai:search:{title}:{original_title}:{year}";
            if (_hybridCache.TryGetValue(memKey, out List<MikaiAnime> cached))
                return cached;

            try
            {
                async Task<List<MikaiAnime>> FindAnime(string query)
                {
                    if (string.IsNullOrWhiteSpace(query))
                        return null;

                    string searchUrl = $"{_init.apihost}/anime/search?page=1&limit=24&sort=year&order=desc&name={HttpUtility.UrlEncode(query)}";
                    var headers = DefaultHeaders();

                    _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {searchUrl}");
                    string json = await Http.Get(_init.cors(searchUrl), headers: headers, proxy: _proxyManager.Get());
                    if (string.IsNullOrEmpty(json))
                        return null;

                    var response = JsonSerializer.Deserialize<SearchResponse>(json);
                    if (response?.Result == null || response.Result.Count == 0)
                        return null;

                    if (year > 0)
                    {
                        var byYear = response.Result.Where(r => r.Year == year).ToList();
                        if (byYear.Count > 0)
                            return byYear;
                    }

                    return response.Result;
                }

                var results = await FindAnime(title) ?? await FindAnime(original_title);
                if (results == null || results.Count == 0)
                    return null;

                _hybridCache.Set(memKey, results, cacheTime(10, init: _init));
                return results;
            }
            catch (Exception ex)
            {
                _onLog($"Mikai Search error: {ex.Message}");
                return null;
            }
        }

        public async Task<MikaiAnime> GetDetails(int id)
        {
            string memKey = $"Mikai:details:{id}";
            if (_hybridCache.TryGetValue(memKey, out MikaiAnime cached))
                return cached;

            try
            {
                string url = $"{_init.apihost}/anime/{id}";
                var headers = DefaultHeaders();

                _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {url}");
                string json = await Http.Get(_init.cors(url), headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(json))
                    return null;

                var response = JsonSerializer.Deserialize<DetailResponse>(json);
                if (response?.Result == null)
                    return null;

                _hybridCache.Set(memKey, response.Result, cacheTime(20, init: _init));
                return response.Result;
            }
            catch (Exception ex)
            {
                _onLog($"Mikai Details error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> ResolveVideoUrl(string url, bool disableAshdiMultivoiceForVod = false)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (url.Contains("moonanime.art", StringComparison.OrdinalIgnoreCase))
                return await ParseMoonAnimePage(url);

            if (url.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase))
                return await ParseAshdiPage(url, disableAshdiMultivoiceForVod);

            return url;
        }

        public async Task<string> ParseMoonAnimePage(string url)
        {
            try
            {
                string requestUrl = url;
                if (!requestUrl.Contains("player=", StringComparison.OrdinalIgnoreCase))
                {
                    requestUrl = requestUrl.Contains("?")
                        ? $"{requestUrl}&player=mikai.me"
                        : $"{requestUrl}?player=mikai.me";
                }

                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", _init.host)
                };

                _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
                string html = await Http.Get(_init.cors(requestUrl), headers: headers, proxy: _proxyManager.Get());
                if (string.IsNullOrEmpty(html))
                    return null;

                var match = System.Text.RegularExpressions.Regex.Match(html, @"file:\s*""([^""]+\.m3u8)""");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch (Exception ex)
            {
                _onLog($"Mikai ParseMoonAnimePage error: {ex.Message}");
            }

            return null;
        }

        string AshdiRequestUrl(string url)
        {
            if (!ApnHelper.IsAshdiUrl(url))
                return url;

            if (!string.IsNullOrWhiteSpace(_init.webcorshost))
                return url;

            return ApnHelper.WrapUrl(_init, url);
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
                _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
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
                _onLog($"Mikai ParseAshdiPage error: {ex.Message}");
            }

            return streams;
        }

        private List<HeadersModel> DefaultHeaders()
        {
            return new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", _init.host),
                new HeadersModel("Accept", "application/json")
            };
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
    }
}
