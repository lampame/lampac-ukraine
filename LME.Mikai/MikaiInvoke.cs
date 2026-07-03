using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Web;
using System.Net;
using System.Text.RegularExpressions;
using LME.Mikai.Models;
using LME.Common.Playerjs;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace LME.Mikai
{
    public class AshdiStream
    {
        public string Title { get; set; }
        public string Link { get; set; }
        public SubtitleTpl Subtitles { get; set; }
    }

    public class MikaiInvoke
    {

        private readonly OnlinesSettings _init;
        private readonly IHybridCache _hybridCache;
        private readonly Action<string> _onLog;
        private readonly ProxyManager _proxyManager;
        private readonly HttpHydra _httpHydra;

        public MikaiInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
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
                    string json = await HttpHelper.GetAsync(_httpHydra, _init, searchUrl, headers, _proxyManager);
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

                _hybridCache.Set(memKey, results, CacheHelper.CacheTime(10, init: _init));
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
                string json = await HttpHelper.GetAsync(_httpHydra, _init, url, headers, _proxyManager);
                if (string.IsNullOrEmpty(json))
                    return null;

                var response = JsonSerializer.Deserialize<DetailResponse>(json);
                if (response?.Result == null)
                    return null;

                _hybridCache.Set(memKey, response.Result, CacheHelper.CacheTime(20, init: _init));
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

        #region MoonAnime Decryption
        private static string CleanMoonUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            string cleaned = Regex.Replace(url, @"([?&])player=[^&]*", "$1", RegexOptions.IgnoreCase);
            cleaned = cleaned.Replace("?&", "?").Replace("&&", "&").TrimEnd('?', '&');
            return cleaned;
        }


        #endregion

        public async Task<string> ParseMoonAnimePage(string url)
        {
            try
            {
                string requestUrl = CleanMoonUrl(url);
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0")
                };

                _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
                string html = await HttpHelper.GetAsync(_httpHydra, _init, requestUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(html))
                    return null;

                var atobMatch = Regex.Match(html, @"=atob\(""([^""]+)""\)");
                if (!atobMatch.Success)
                {
                    atobMatch = Regex.Match(html, @"=atob\('([^']+)'\)");
                }

                if (atobMatch.Success)
                {
                    string blob = atobMatch.Groups[1].Value;
                    string decryptedJs = TortugaDecoder.MoonDecode(blob);
                    if (!string.IsNullOrEmpty(decryptedJs))
                    {
                        var keyMatch = Regex.Match(decryptedJs, @"var k=""([^""]+)""");
                        if (!keyMatch.Success)
                            keyMatch = Regex.Match(decryptedJs, @"var k='([^']+)'");

                        var fileMatch = Regex.Match(decryptedJs, @"file\s*:\s*_0xd\(""([^""]+)""\)");
                        if (!fileMatch.Success)
                            fileMatch = Regex.Match(decryptedJs, @"file\s*:\s*_0xd\('([^']+)'\)");

                        if (keyMatch.Success && fileMatch.Success)
                        {
                            string key = keyMatch.Groups[1].Value;
                            string fileEncrypted = fileMatch.Groups[1].Value;
                            string streams = TortugaDecoder.MoonXorDecrypt(fileEncrypted, key);
                            if (!string.IsNullOrEmpty(streams))
                            {
                                return streams;
                            }
                        }
                        else
                        {
                            var directFileMatch = Regex.Match(decryptedJs, @"file\s*:\s*""([^""]+)""");
                            if (!directFileMatch.Success)
                                directFileMatch = Regex.Match(decryptedJs, @"file\s*:\s*'([^']+)'");

                            if (directFileMatch.Success)
                            {
                                return directFileMatch.Groups[1].Value;
                            }
                        }
                    }
                }

                var payload = PlayerJsDecoder.ExtractPlayerPayload(html);
                if (payload?.FilePayload != null)
                {
                    var streamUrls = ExtractStreamUrls(payload.FilePayload);
                    return streamUrls?.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                _onLog($"Mikai ParseMoonAnimePage error: {ex.Message}");
            }

            return null;
        }

        private List<string> ExtractStreamUrls(object filePayload)
        {
            var urls = new List<string>();
            if (filePayload == null)
                return urls;

            // Обробка string значення
            if (filePayload is string strPayload)
            {
                urls.Add(strPayload);
                return urls;
            }

            // Обробка JsonValue
            if (filePayload is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string strValue))
            {
                urls.Add(strValue);
                return urls;
            }

            // Обробка JsonObject — витягти 'file' поле
            if (filePayload is JsonObject objPayload)
            {
                if (objPayload.TryGetPropertyValue("file", out JsonNode fileNode))
                {
                    string fileStr = fileNode?.ToString();
                    if (!string.IsNullOrEmpty(fileStr))
                        urls.Add(fileStr);
                }
                return urls;
            }

            // Обробка JsonArray
            if (filePayload is JsonArray arrayPayload)
            {
                foreach (var item in arrayPayload)
                {
                    if (item is JsonObject itemObj && itemObj.TryGetPropertyValue("file", out JsonNode fileProp))
                    {
                        string fileStr = fileProp?.ToString();
                        if (!string.IsNullOrEmpty(fileStr))
                            urls.Add(fileStr);
                    }
                    else if (item is JsonValue itemValue && itemValue.TryGetValue<string>(out string itemStr))
                    {
                        if (!string.IsNullOrEmpty(itemStr))
                            urls.Add(itemStr);
                    }
                }
                return urls;
            }

            return urls;
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
            return streams?.FirstOrDefault()?.Link;
        }

        public async Task<List<AshdiStream>> ParseAshdiPageStreams(string url, bool disableAshdiMultivoiceForVod = false)
        {
            var streams = new List<AshdiStream>();
            try
            {
                var headers = new List<HeadersModel>()
                {
                    new HeadersModel("User-Agent", "Mozilla/5.0"),
                    new HeadersModel("Referer", "https://ashdi.vip/")
                };

                string requestUrl = AshdiRequestUrl(ApnExtensions.WithAshdiMultivoice(url, enable: !disableAshdiMultivoiceForVod));
                _onLog($"Mikai: using proxy {_proxyManager.CurrentProxyIp} for {requestUrl}");
                string html = await HttpHelper.GetAsync(_httpHydra, _init, requestUrl, headers, _proxyManager);
                if (string.IsNullOrEmpty(html))
                    return streams;

                string rawArray = AshdiParser.ExtractPlayerFileArray(html);
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
                            streams.Add(new AshdiStream
                            {
                                Title = QualityHelper.BuildDisplayTitle(rawTitle, file, index),
                                Link = file,
                                Subtitles = ApnHelper.ParseSubtitles(item.TryGetProperty("subtitle", out var subtitleProp) ? subtitleProp.GetString() : null)
                            });
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
                    streams.Add(new AshdiStream
                    {
                        Title = QualityHelper.BuildDisplayTitle("Основне джерело", file, 1),
                        Link = file,
                        Subtitles = ApnHelper.ParseSubtitles(ApnHelper.ExtractPlayerSubtitle(html))
                    });
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

    }
}
