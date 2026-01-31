using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models;
using System.Text.RegularExpressions;
using System.Text;
using HtmlAgilityPack;
using CikavaIdeya.Models;
using Shared.Engine;
using System.Linq;

namespace CikavaIdeya
{
    public class CikavaIdeyaInvoke
    {
        private OnlinesSettings _init;
        private IHybridCache _hybridCache;
        private Action<string> _onLog;
        private ProxyManager _proxyManager;

        public CikavaIdeyaInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
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

            return ApnHelper.WrapUrl(_init, url);
        }

        public async Task<List<CikavaIdeya.Models.EpisodeLinkInfo>> Search(string imdb_id, long kinopoisk_id, string title, string original_title, int year, bool isfilm = false)
        {
            string filmTitle = !string.IsNullOrEmpty(title) ? title : original_title;
            string memKey = $"CikavaIdeya:search:{filmTitle}:{year}:{isfilm}";
            if (_hybridCache.TryGetValue(memKey, out List<CikavaIdeya.Models.EpisodeLinkInfo> res))
                return res;

            try
            {
                // Спочатку шукаємо по title
                res = await PerformSearch(title, year);
                
                // Якщо нічого не знайдено і є original_title, шукаємо по ньому
                if ((res == null || res.Count == 0) && !string.IsNullOrEmpty(original_title) && original_title != title)
                {
                    _onLog($"No results for '{title}', trying search by original title '{original_title}'");
                    res = await PerformSearch(original_title, year);
                    // Оновлюємо ключ кешу для original_title
                    if (res != null && res.Count > 0)
                    {
                        memKey = $"CikavaIdeya:search:{original_title}:{year}:{isfilm}";
                    }
                }

                if (res != null && res.Count > 0)
                {
                    _hybridCache.Set(memKey, res, cacheTime(20));
                    return res;
                }
            }
            catch (Exception ex)
            {
                _onLog($"CikavaIdeya search error: {ex.Message}");
            }
            return null;
        }
        
        async Task<List<EpisodeLinkInfo>> PerformSearch(string searchTitle, int year)
        {
            try
            {
                string searchUrl = $"{_init.host}/index.php?do=search&subaction=search&story={System.Web.HttpUtility.UrlEncode(searchTitle)}";
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) };

                var searchHtml = await Http.Get(searchUrl, headers: headers, proxy: _proxyManager.Get());
                // Перевіряємо, чи є результати пошуку
                if (searchHtml.Contains("На жаль, пошук на сайті не дав жодних результатів"))
                {
                    _onLog($"No search results for '{searchTitle}'");
                    return new List<CikavaIdeya.Models.EpisodeLinkInfo>();
                }
                
                var doc = new HtmlDocument();
                doc.LoadHtml(searchHtml);

                var filmNodes = doc.DocumentNode.SelectNodes("//div[@class='th-item']");
                if (filmNodes == null)
                {
                    _onLog($"No film nodes found for '{searchTitle}'");
                    return new List<CikavaIdeya.Models.EpisodeLinkInfo>();
                }

                string filmUrl = null;
                foreach (var filmNode in filmNodes)
                {
                    var titleNode = filmNode.SelectSingleNode(".//div[@class='th-title']");
                    if (titleNode == null || !titleNode.InnerText.Trim().ToLower().Contains(searchTitle.ToLower())) continue;
                    
                    var descNode = filmNode.SelectSingleNode(".//div[@class='th-subtitle']");
                    if (year > 0 && (descNode?.InnerText ?? "").Contains(year.ToString()))
                    {
                        var linkNode = filmNode.SelectSingleNode(".//a[@class='th-in']");
                        if (linkNode != null)
                        {
                            filmUrl = linkNode.GetAttributeValue("href", "");
                            break;
                        }
                    }
                }

                if (filmUrl == null)
                {
                    var firstNode = filmNodes.FirstOrDefault()?.SelectSingleNode(".//a[@class='th-in']");
                    if (firstNode != null)
                        filmUrl = firstNode.GetAttributeValue("href", "");
                }

                if (filmUrl == null)
                {
                    _onLog($"No film URL found for '{searchTitle}'");
                    return new List<CikavaIdeya.Models.EpisodeLinkInfo>();
                }

                if (!filmUrl.StartsWith("http"))
                    filmUrl = _init.host + filmUrl;

                // Отримуємо список епізодів (для фільмів - один епізод, для серіалів - всі епізоди)
                var filmHtml = await Http.Get(filmUrl, headers: headers, proxy: _proxyManager.Get());
                // Перевіряємо, чи не видалено контент
                if (filmHtml.Contains("Видалено на прохання правовласника"))
                {
                    _onLog($"Content removed on copyright holder request: {filmUrl}");
                    return new List<CikavaIdeya.Models.EpisodeLinkInfo>();
                }
                
                doc.LoadHtml(filmHtml);

                // Знаходимо JavaScript з даними про епізоди
                var scriptNodes = doc.DocumentNode.SelectNodes("//script");
                if (scriptNodes != null)
                {
                    foreach (var scriptNode in scriptNodes)
                    {
                        var scriptContent = scriptNode.InnerText;
                        if (scriptContent.Contains("switches = Object"))
                        {
                            _onLog($"Found switches script: {scriptContent}");
                            // Парсимо структуру switches
                            var match = Regex.Match(scriptContent, @"switches = Object\((\{.*\})\);", RegexOptions.Singleline);
                            if (match.Success)
                            {
                                string switchesJson = match.Groups[1].Value;
                                _onLog($"Parsed switches JSON: {switchesJson}");
                                // Спрощений парсинг JSON-подібної структури
                                var res = ParseSwitchesJson(switchesJson, _init.host, filmUrl);
                                _onLog($"Parsed episodes count: {res.Count}");
                                foreach (var ep in res)
                                {
                                    _onLog($"Episode: season={ep.season}, episode={ep.episode}, title={ep.title}, url={ep.url}");
                                }
                                return res;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _onLog($"PerformSearch error for '{searchTitle}': {ex.Message}");
            }
            return new List<EpisodeLinkInfo>();
        }

        List<CikavaIdeya.Models.EpisodeLinkInfo> ParseSwitchesJson(string json, string host, string baseUrl)
        {
            var result = new List<CikavaIdeya.Models.EpisodeLinkInfo>();
            
            try
            {
                _onLog($"Parsing switches JSON: {json}");
                // Спрощений парсинг JSON-подібної структури
                // Приклад для серіалу: {"Player1":{"1 сезон":{"1 серія":"https://ashdi.vip/vod/57364",...},"2 сезон":{"1 серія":"https://ashdi.vip/vod/118170",...}}}
                // Приклад для фільму: {"Player1":"https://ashdi.vip/vod/162246"}
                
                // Знаходимо плеєр Player1
                // Спочатку спробуємо знайти об'єкт Player1
                var playerObjectMatch = Regex.Match(json, @"""Player1""\s*:\s*(\{(?:[^{}]|(?<open>\{)|(?<-open>\}))+(?(open)(?!)))", RegexOptions.Singleline);
                if (playerObjectMatch.Success)
                {
                    string playerContent = playerObjectMatch.Groups[1].Value;
                    _onLog($"Player1 object content: {playerContent}");
                    
                    // Це серіал, парсимо сезони
                    var seasonMatches = Regex.Matches(playerContent, @"""([^""]+?сезон[^""]*?)""\s*:\s*\{((?:[^{}]|(?<open>\{)|(?<-open>\}))+(?(open)(?!)))\}", RegexOptions.Singleline);
                    _onLog($"Found {seasonMatches.Count} seasons");
                    foreach (Match seasonMatch in seasonMatches)
                    {
                        string seasonName = seasonMatch.Groups[1].Value;
                        string seasonContent = seasonMatch.Groups[2].Value;
                        _onLog($"Season: {seasonName}, Content: {seasonContent}");
                        
                        // Витягуємо номер сезону
                        var seasonNumMatch = Regex.Match(seasonName, @"(\d+)");
                        int seasonNum = seasonNumMatch.Success ? int.Parse(seasonNumMatch.Groups[1].Value) : 1;
                        _onLog($"Season number: {seasonNum}");
                        
                        // Парсимо епізоди
                        var episodeMatches = Regex.Matches(seasonContent, @"""([^""]+?)""\s*:\s*""([^""]+?)""", RegexOptions.Singleline);
                        _onLog($"Found {episodeMatches.Count} episodes in season {seasonNum}");
                        foreach (Match episodeMatch in episodeMatches)
                        {
                            string episodeName = episodeMatch.Groups[1].Value;
                            string episodeUrl = episodeMatch.Groups[2].Value;
                            _onLog($"Episode: {episodeName}, URL: {episodeUrl}");
                            
                            // Витягуємо номер епізоду
                            var episodeNumMatch = Regex.Match(episodeName, @"(\d+)");
                            int episodeNum = episodeNumMatch.Success ? int.Parse(episodeNumMatch.Groups[1].Value) : 1;
                            
                            result.Add(new CikavaIdeya.Models.EpisodeLinkInfo
                            {
                                url = episodeUrl,
                                title = episodeName,
                                season = seasonNum,
                                episode = episodeNum
                            });
                        }
                    }
                }
                else
                {
                    // Якщо не знайшли об'єкт, спробуємо знайти просте значення
                    var playerStringMatch = Regex.Match(json, @"""Player1""\s*:\s*(""([^""]+)"")", RegexOptions.Singleline);
                    if (playerStringMatch.Success)
                    {
                        string playerContent = playerStringMatch.Groups[1].Value;
                        _onLog($"Player1 string content: {playerContent}");
                        
                        // Якщо це фільм (просте значення)
                        if (playerContent.StartsWith("\"") && playerContent.EndsWith("\""))
                        {
                            string filmUrl = playerContent.Trim('"');
                            result.Add(new CikavaIdeya.Models.EpisodeLinkInfo
                            {
                                url = filmUrl,
                                title = "Фільм",
                                season = 1,
                                episode = 1
                            });
                        }
                    }
                    else
                    {
                        _onLog("Player1 not found");
                    }
                }
            }
            catch (Exception ex)
            {
                _onLog($"ParseSwitchesJson error: {ex.Message}");
            }
            
            return result;
        }

        public async Task<CikavaIdeya.Models.PlayResult> ParseEpisode(string url)
        {
            var result = new CikavaIdeya.Models.PlayResult() { streams = new List<(string, string)>() };
            try
            {
                // Якщо це вже iframe URL (наприклад, з switches), повертаємо його
                if (url.Contains("ashdi.vip"))
                {
                    _onLog($"ParseEpisode: URL contains ashdi.vip, calling GetStreamUrlFromAshdi");
                    string streamUrl = await GetStreamUrlFromAshdi(url);
                    _onLog($"ParseEpisode: GetStreamUrlFromAshdi returned {streamUrl}");
                    if (!string.IsNullOrEmpty(streamUrl))
                    {
                        result.streams.Add((streamUrl, "hls"));
                        _onLog($"ParseEpisode: added stream URL to result.streams");
                        return result;
                    }
                    // Якщо не вдалося отримати посилання на поток, повертаємо iframe URL
                    _onLog($"ParseEpisode: stream URL is null or empty, setting iframe_url");
                    result.iframe_url = url;
                    return result;
                }
                
                // Інакше парсимо сторінку
                string html = await Http.Get(url, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", _init.host) }, proxy: _proxyManager.Get());
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var iframe = doc.DocumentNode.SelectSingleNode("//div[@class='video-box']//iframe");
                if (iframe != null)
                {
                    string iframeUrl = iframe.GetAttributeValue("src", "").Replace("&", "&");
                    if (iframeUrl.StartsWith("//"))
                        iframeUrl = "https:" + iframeUrl;

                    result.iframe_url = iframeUrl;
                    return result;
                }
            }
            catch (Exception ex)
            {
                _onLog($"ParseEpisode error: {ex.Message}");
            }
            return result;
        }
        public async Task<string> GetStreamUrlFromAshdi(string url)
        {
            try
            {
                _onLog($"GetStreamUrlFromAshdi: trying to get stream URL from {url}");
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", "https://ashdi.vip/") };
                string html = await Http.Get(AshdiRequestUrl(url), headers: headers, proxy: _proxyManager.Get());
                _onLog($"GetStreamUrlFromAshdi: received HTML, length={html.Length}");
                
                // Знаходимо JavaScript код з об'єктом player
                var match = Regex.Match(html, @"var\s+player\s*=\s*new\s+Playerjs[\s\S]*?\(\s*({[\s\S]*?})\s*\)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    _onLog($"GetStreamUrlFromAshdi: found player object");
                    string playerJson = match.Groups[1].Value;
                    _onLog($"GetStreamUrlFromAshdi: playerJson={playerJson}");
                    // Знаходимо поле file
                    var fileMatch = Regex.Match(playerJson, @"file\s*:\s*['""]([^'""]+)['""]", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (fileMatch.Success)
                    {
                        _onLog($"GetStreamUrlFromAshdi: found file URL: {fileMatch.Groups[1].Value}");
                        return fileMatch.Groups[1].Value;
                    }
                    else
                    {
                        _onLog($"GetStreamUrlFromAshdi: file URL not found in playerJson");
                    }
                }
                else
                {
                    _onLog($"GetStreamUrlFromAshdi: player object not found in HTML");
                }
            }
            catch (Exception ex)
            {
                _onLog($"GetStreamUrlFromAshdi error: {ex.Message}");
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
