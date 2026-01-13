using Shared.Engine;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Web;
using System.Linq;
using HtmlAgilityPack;
using Shared;
using Shared.Models.Templates;
using System.Text.RegularExpressions;
using Shared.Models.Online.Settings;
using Shared.Models;
using CikavaIdeya.Models;

namespace CikavaIdeya.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller()
        {
            proxyManager = new ProxyManager(ModInit.CikavaIdeya);
        }
        
        [HttpGet]
        [Route("cikavaideya")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, int e = -1, bool play = false, bool rjson = false)
        {
            var init = await loadKit(ModInit.CikavaIdeya);
            if (!init.enable)
                return Forbid();

            var invoke = new CikavaIdeyaInvoke(init, hybridCache, OnLog, proxyManager);

            var episodesInfo = await invoke.Search(imdb_id, kinopoisk_id, title, original_title, year, serial == 0);
            if (episodesInfo == null)
                return Content("CikavaIdeya", "text/html; charset=utf-8");

            if (play)
            {
                var episode = episodesInfo.FirstOrDefault(ep => ep.season == s && ep.episode == e);
                if (serial == 0) // для фильма берем первый
                    episode = episodesInfo.FirstOrDefault();

                if (episode == null)
                    return Content("CikavaIdeya", "text/html; charset=utf-8");
                
                OnLog($"Controller: calling invoke.ParseEpisode with URL: {episode.url}");
                var playResult = await invoke.ParseEpisode(episode.url);
                OnLog($"Controller: invoke.ParseEpisode returned playResult with streams.Count={playResult.streams?.Count ?? 0}, iframe_url={playResult.iframe_url}");
                
                if (playResult.streams != null && playResult.streams.Count > 0)
                {
                    string streamLink = playResult.streams.First().link;
                    List<HeadersModel> streamHeaders = null;
                    bool forceProxy = false;

                    if (streamLink.Contains("ashdi.vip", StringComparison.OrdinalIgnoreCase) && init.streamproxy)
                    {
                        streamHeaders = new List<HeadersModel>()
                        {
                            new HeadersModel("User-Agent", "Mozilla/5.0"),
                            new HeadersModel("Referer", "https://ashdi.vip/")
                        };
                        forceProxy = true;
                    }

                    string streamUrl = HostStreamProxy(init, accsArgs(streamLink), headers: streamHeaders, force_streamproxy: forceProxy);
                    OnLog($"Controller: redirecting to stream URL: {streamUrl}");
                    return Redirect(streamUrl);
                }
                
                if (!string.IsNullOrEmpty(playResult.iframe_url))
                {
                    OnLog($"Controller: redirecting to iframe URL: {playResult.iframe_url}");
                    // Для CikavaIdeya ми просто повертаємо iframe URL
                    return Redirect(playResult.iframe_url);
                }

                if (playResult.streams != null && playResult.streams.Count > 0)
                    return Redirect(HostStreamProxy(init, accsArgs(playResult.streams.First().link)));
                
                return Content("CikavaIdeya", "text/html; charset=utf-8");
            }

            if (serial == 1)
            {
                if (s == -1) // Выбор сезона
                {
                    var seasons = episodesInfo.GroupBy(ep => ep.season).ToDictionary(k => k.Key, v => v.ToList());
                    OnLog($"Grouped seasons count: {seasons.Count}");
                    foreach (var season in seasons)
                    {
                        OnLog($"Season {season.Key}: {season.Value.Count} episodes");
                    }
                    var season_tpl = new SeasonTpl(seasons.Count);
                    foreach (var season in seasons.OrderBy(i => i.Key))
                    {
                        string link = $"{host}/cikavaideya?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={season.Key}";
                        season_tpl.Append($"Сезон {season.Key}", link, $"{season.Key}");
                    }
                    OnLog("Before generating season template HTML");
                    string htmlContent = season_tpl.ToHtml();
                    OnLog($"Season template HTML: {htmlContent}");
                    return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(htmlContent, "text/html; charset=utf-8");
                }
                
                // Выбор эпизода
                var episodes = episodesInfo.Where(ep => ep.season == s).OrderBy(ep => ep.episode).ToList();
                var movie_tpl = new MovieTpl(title, original_title, episodes.Count);
                foreach(var ep in episodes)
                {
                    string link = $"{host}/cikavaideya?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&e={ep.episode}&play=true";
                    movie_tpl.Append(ep.title, accsArgs(link), method: "play");
                }
                return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
            }
            else // Фильм
            {
                string link = $"{host}/cikavaideya?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&play=true";
                var tpl = new MovieTpl(title, original_title, 1);
                tpl.Append(title, accsArgs(link), method: "play");
                return rjson ? Content(tpl.ToJson(), "application/json; charset=utf-8") : Content(tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        async ValueTask<List<EpisodeLinkInfo>> search(OnlinesSettings init, string imdb_id, long kinopoisk_id, string title, string original_title, int year, bool isfilm = false)
        {
            string filmTitle = !string.IsNullOrEmpty(title) ? title : original_title;
            string memKey = $"CikavaIdeya:search:{filmTitle}:{year}:{isfilm}";
            if (hybridCache.TryGetValue(memKey, out List<EpisodeLinkInfo> res))
                return res;

            try
            {
                // Спочатку шукаємо по title
                res = await PerformSearch(init, title, year);
                
                // Якщо нічого не знайдено і є original_title, шукаємо по ньому
                if ((res == null || res.Count == 0) && !string.IsNullOrEmpty(original_title) && original_title != title)
                {
                    OnLog($"No results for '{title}', trying search by original title '{original_title}'");
                    res = await PerformSearch(init, original_title, year);
                    // Оновлюємо ключ кешу для original_title
                    if (res != null && res.Count > 0)
                    {
                        memKey = $"CikavaIdeya:search:{original_title}:{year}:{isfilm}";
                    }
                }

                if (res != null && res.Count > 0)
                {
                    hybridCache.Set(memKey, res, cacheTime(20));
                    return res;
                }
            }
            catch (Exception ex)
            {
                OnLog($"CikavaIdeya search error: {ex.Message}");
            }
            return null;
        }
        
        async Task<List<EpisodeLinkInfo>> PerformSearch(OnlinesSettings init, string searchTitle, int year)
        {
            try
            {
                string searchUrl = $"{init.host}/index.php?do=search&subaction=search&story={HttpUtility.UrlEncode(searchTitle)}";
                var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", init.host) };

                var searchHtml = await Http.Get(searchUrl, headers: headers);
                // Перевіряємо, чи є результати пошуку
                if (searchHtml.Contains("На жаль, пошук на сайті не дав жодних результатів"))
                {
                    OnLog($"No search results for '{searchTitle}'");
                    return new List<EpisodeLinkInfo>();
                }
                
                var doc = new HtmlDocument();
                doc.LoadHtml(searchHtml);

                var filmNodes = doc.DocumentNode.SelectNodes("//div[@class='th-item']");
                if (filmNodes == null)
                {
                    OnLog($"No film nodes found for '{searchTitle}'");
                    return new List<EpisodeLinkInfo>();
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
                    OnLog($"No film URL found for '{searchTitle}'");
                    return new List<EpisodeLinkInfo>();
                }

                if (!filmUrl.StartsWith("http"))
                    filmUrl = init.host + filmUrl;

                // Отримуємо список епізодів (для фільмів - один епізод, для серіалів - всі епізоди)
                var filmHtml = await Http.Get(filmUrl, headers: headers);
                // Перевіряємо, чи не видалено контент
                if (filmHtml.Contains("Видалено на прохання правовласника"))
                {
                    OnLog($"Content removed on copyright holder request: {filmUrl}");
                    return new List<EpisodeLinkInfo>();
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
                            OnLog($"Found switches script: {scriptContent}");
                            // Парсимо структуру switches
                            var match = Regex.Match(scriptContent, @"switches = Object\((\{.*\})\);", RegexOptions.Singleline);
                            if (match.Success)
                            {
                                string switchesJson = match.Groups[1].Value;
                                OnLog($"Parsed switches JSON: {switchesJson}");
                                // Спрощений парсинг JSON-подібної структури
                                var res = ParseSwitchesJson(switchesJson, init.host, filmUrl);
                                OnLog($"Parsed episodes count: {res.Count}");
                                foreach (var ep in res)
                                {
                                    OnLog($"Episode: season={ep.season}, episode={ep.episode}, title={ep.title}, url={ep.url}");
                                }
                                return res;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog($"PerformSearch error for '{searchTitle}': {ex.Message}");
            }
            return new List<EpisodeLinkInfo>();
        }

        List<EpisodeLinkInfo> ParseSwitchesJson(string json, string host, string baseUrl)
        {
            var result = new List<EpisodeLinkInfo>();
            
            try
            {
                OnLog($"Parsing switches JSON: {json}");
                // Спрощений парсинг JSON-подібної структури
                // Приклад для серіалу: {"Player1":{"1 сезон":{"1 серія":"https://ashdi.vip/vod/57364",...},"2 сезон":{"1 серія":"https://ashdi.vip/vod/118170",...}}}
                // Приклад для фільму: {"Player1":"https://ashdi.vip/vod/162246"}
                
                // Знаходимо плеєр Player1
                // Спочатку спробуємо знайти об'єкт Player1
                var playerObjectMatch = Regex.Match(json, @"""Player1""\s*:\s*(\{(?:[^{}]|(?<open>\{)|(?<-open>\}))+(?(open)(?!)))", RegexOptions.Singleline);
                if (playerObjectMatch.Success)
                {
                    string playerContent = playerObjectMatch.Groups[1].Value;
                    OnLog($"Player1 object content: {playerContent}");
                    
                    // Це серіал, парсимо сезони
                    var seasonMatches = Regex.Matches(playerContent, @"""([^""]+?сезон[^""]*?)""\s*:\s*\{((?:[^{}]|(?<open>\{)|(?<-open>\}))+(?(open)(?!)))\}", RegexOptions.Singleline);
                    OnLog($"Found {seasonMatches.Count} seasons");
                    foreach (Match seasonMatch in seasonMatches)
                    {
                        string seasonName = seasonMatch.Groups[1].Value;
                        string seasonContent = seasonMatch.Groups[2].Value;
                        OnLog($"Season: {seasonName}, Content: {seasonContent}");
                        
                        // Витягуємо номер сезону
                        var seasonNumMatch = Regex.Match(seasonName, @"(\d+)");
                        int seasonNum = seasonNumMatch.Success ? int.Parse(seasonNumMatch.Groups[1].Value) : 1;
                        OnLog($"Season number: {seasonNum}");
                        
                        // Парсимо епізоди
                        var episodeMatches = Regex.Matches(seasonContent, @"""([^""]+?)""\s*:\s*""([^""]+?)""", RegexOptions.Singleline);
                        OnLog($"Found {episodeMatches.Count} episodes in season {seasonNum}");
                        foreach (Match episodeMatch in episodeMatches)
                        {
                            string episodeName = episodeMatch.Groups[1].Value;
                            string episodeUrl = episodeMatch.Groups[2].Value;
                            OnLog($"Episode: {episodeName}, URL: {episodeUrl}");
                            
                            // Витягуємо номер епізоду
                            var episodeNumMatch = Regex.Match(episodeName, @"(\d+)");
                            int episodeNum = episodeNumMatch.Success ? int.Parse(episodeNumMatch.Groups[1].Value) : 1;
                            
                            result.Add(new EpisodeLinkInfo
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
                        OnLog($"Player1 string content: {playerContent}");
                        
                        // Якщо це фільм (просте значення)
                        if (playerContent.StartsWith("\"") && playerContent.EndsWith("\""))
                        {
                            string filmUrl = playerContent.Trim('"');
                            result.Add(new EpisodeLinkInfo
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
                        OnLog("Player1 not found");
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog($"ParseSwitchesJson error: {ex.Message}");
            }
            
            return result;
        }

        async Task<PlayResult> ParseEpisode(OnlinesSettings init, string url)
        {
            var result = new PlayResult() { streams = new List<(string, string)>() };
            try
            {
                // Якщо це вже iframe URL (наприклад, з switches), повертаємо його
                if (url.Contains("ashdi.vip"))
                {
                    result.iframe_url = url;
                    return result;
                }
                
                // Інакше парсимо сторінку
                string html = await Http.Get(url, headers: new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", init.host) });
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
                OnLog($"ParseEpisode error: {ex.Message}");
            }
            return result;
        }
    }
}
