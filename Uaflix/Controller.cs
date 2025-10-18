using Shared.Models.Templates;
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
using Uaflix.Models;

namespace Uaflix.Controllers
{

    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller()
        {
            proxyManager = new ProxyManager(ModInit.UaFlix);
        }
        
        [HttpGet]
        [Route("uaflix")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, int e = -1, bool play = false, bool rjson = false, string href = null, bool checksearch = false)
        {
            var init = await loadKit(ModInit.UaFlix);
            if (await IsBadInitialization(init))
                return Forbid();

            OnLog($"=== UAFLIX INDEX START ===");
            OnLog($"Uaflix Index: title={title}, serial={serial}, s={s}, play={play}, href={href}, checksearch={checksearch}");
            OnLog($"Uaflix Index: kinopoisk_id={kinopoisk_id}, imdb_id={imdb_id}, id={id}");
            OnLog($"Uaflix Index: year={year}, source={source}, t={t}, e={e}, rjson={rjson}");

            var invoke = new UaflixInvoke(init, hybridCache, OnLog, proxyManager);

            // Обробка параметра checksearch - повертаємо спеціальну відповідь для валідації
            if (checksearch)
            {
                try
                {
                    string filmTitle = !string.IsNullOrEmpty(title) ? title : original_title;
                    string searchUrl = $"{init.host}/index.php?do=search&subaction=search&story={System.Web.HttpUtility.UrlEncode(filmTitle)}";
                    var headers = new List<HeadersModel>() { new HeadersModel("User-Agent", "Mozilla/5.0"), new HeadersModel("Referer", init.host) };

                    var searchHtml = await Http.Get(searchUrl, headers: headers, proxy: proxyManager.Get(), timeoutSeconds: 10);

                    // Швидка перевірка наявності результатів без повного парсингу
                    if (!string.IsNullOrEmpty(searchHtml) &&
                        (searchHtml.Contains("sres-wrap") || searchHtml.Contains("sres-item") || searchHtml.Contains("search-results")))
                    {
                        // Якщо знайдено контент, повертаємо "data-json=" для валідації
                        OnLog("checksearch: Content found, returning validation response");
                        OnLog("=== RETURN: checksearch validation (data-json=) ===");
                        return Content("data-json=", "text/plain; charset=utf-8");
                    }
                    else
                    {
                        // Якщо нічого не знайдено, повертаємо OnError
                        OnLog("checksearch: No content found");
                        OnLog("=== RETURN: checksearch OnError ===");
                        return OnError("uaflix", proxyManager);
                    }
                }
                catch (Exception ex)
                {
                    OnLog($"checksearch error: {ex.Message}");
                    OnLog("=== RETURN: checksearch exception OnError ===");
                    return OnError("uaflix", proxyManager);
                }
            }

            if (play)
            {
                // Визначаємо URL для парсингу - або з параметра t, або з episode_url
                string urlToParse = !string.IsNullOrEmpty(t) ? t : Request.Query["episode_url"];
                
                var playResult = await invoke.ParseEpisode(urlToParse);
                if (playResult.streams != null && playResult.streams.Count > 0)
                {
                    OnLog("=== RETURN: play redirect ===");
                    return Redirect(HostStreamProxy(init, accsArgs(playResult.streams.First().link)));
                }
                
                OnLog("=== RETURN: play no streams ===");
                return Content("Uaflix", "text/html; charset=utf-8");
            }
            
            // Якщо є episode_url але немає play=true, це виклик для отримання інформації про стрім (для method: 'call')
            string episodeUrl = Request.Query["episode_url"];
            if (!string.IsNullOrEmpty(episodeUrl))
            {
                var playResult = await invoke.ParseEpisode(episodeUrl);
                if (playResult.streams != null && playResult.streams.Count > 0)
                {
                    // Повертаємо JSON з інформацією про стрім для методу 'play'
                    string streamUrl = HostStreamProxy(init, accsArgs(playResult.streams.First().link));
                    string jsonResult = $"{{\"method\":\"play\",\"url\":\"{streamUrl}\",\"title\":\"{title ?? original_title}\"}}";
                    OnLog($"=== RETURN: call method JSON for episode_url ===");
                    return Content(jsonResult, "application/json; charset=utf-8");
                }
                
                OnLog("=== RETURN: call method no streams ===");
                return Content("Uaflix", "text/html; charset=utf-8");
            }

            string filmUrl = href;

            if (string.IsNullOrEmpty(filmUrl))
            {
                var searchResults = await invoke.Search(imdb_id, kinopoisk_id, title, original_title, year, title);
                if (searchResults == null || searchResults.Count == 0)
                {
                    OnLog("No search results found");
                    OnLog("=== RETURN: no search results OnError ===");
                    return OnError("uaflix", proxyManager);
                }
                
                // Для фільмів і серіалів показуємо вибір тільки якщо більше одного результату
                if (searchResults.Count > 1)
                {
                    var similar_tpl = new SimilarTpl(searchResults.Count);
                    foreach (var res in searchResults)
                    {
                        string link = $"{host}/uaflix?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(res.Url)}";
                        similar_tpl.Append(res.Title, res.Year.ToString(), string.Empty, link, res.PosterUrl);
                    }
                    OnLog($"=== RETURN: similar items ({searchResults.Count}) ===");
                    return rjson ? Content(similar_tpl.ToJson(), "application/json; charset=utf-8") : Content(similar_tpl.ToHtml(), "text/html; charset=utf-8");
                }

                filmUrl = searchResults[0].Url;
                OnLog($"Auto-selected first search result: {filmUrl}");
            }

            if (serial == 1)
            {
                // Агрегуємо всі озвучки з усіх плеєрів
                var structure = await invoke.AggregateSerialStructure(filmUrl);
                if (structure == null || !structure.Voices.Any())
                {
                    OnLog("No voices found in aggregated structure");
                    OnLog("=== RETURN: no voices OnError ===");
                    return OnError("uaflix", proxyManager);
                }

                OnLog($"Structure aggregated successfully: {structure.Voices.Count} voices, URL: {filmUrl}");
                foreach (var voice in structure.Voices)
                {
                    OnLog($"Voice: {voice.Key}, Type: {voice.Value.PlayerType}, Seasons: {voice.Value.Seasons.Count}");
                    foreach (var season in voice.Value.Seasons)
                    {
                        OnLog($"  Season {season.Key}: {season.Value.Count} episodes");
                    }
                }

                // s == -1: Вибір сезону
                if (s == -1)
                {
                    var allSeasons = structure.Voices
                        .SelectMany(v => v.Value.Seasons.Keys)
                        .Distinct()
                        .OrderBy(sn => sn)
                        .ToList();

                    OnLog($"Found {allSeasons.Count} seasons in structure: {string.Join(", ", allSeasons)}");

                    // Перевіряємо чи сезони містять валідні епізоди з файлами
                    var seasonsWithValidEpisodes = allSeasons.Where(season =>
                        structure.Voices.Values.Any(v =>
                            v.Seasons.ContainsKey(season) &&
                            v.Seasons[season].Any(ep => !string.IsNullOrEmpty(ep.File))
                        )
                    ).ToList();

                    OnLog($"Seasons with valid episodes: {seasonsWithValidEpisodes.Count}");
                    foreach (var season in allSeasons)
                    {
                        var episodesInSeason = structure.Voices.Values
                            .Where(v => v.Seasons.ContainsKey(season))
                            .SelectMany(v => v.Seasons[season])
                            .Where(ep => !string.IsNullOrEmpty(ep.File))
                            .ToList();
                        OnLog($"Season {season}: {episodesInSeason.Count} valid episodes");
                    }

                    if (!seasonsWithValidEpisodes.Any())
                    {
                        OnLog("No seasons with valid episodes found in structure");
                        OnLog("=== RETURN: no valid seasons OnError ===");
                        return OnError("uaflix", proxyManager);
                    }

                    var season_tpl = new SeasonTpl(seasonsWithValidEpisodes.Count);
                    foreach (var season in seasonsWithValidEpisodes)
                    {
                        string link = $"{host}/uaflix?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={season}&href={HttpUtility.UrlEncode(filmUrl)}";
                        season_tpl.Append($"{season}", link, season.ToString());
                        OnLog($"Added season {season} to template");
                    }

                    OnLog($"Returning season template with {seasonsWithValidEpisodes.Count} seasons");
                    
                    var htmlContent = rjson ? season_tpl.ToJson() : season_tpl.ToHtml();
                    OnLog($"Season template response length: {htmlContent.Length}");
                    OnLog($"Season template HTML (first 300): {htmlContent.Substring(0, Math.Min(300, htmlContent.Length))}");
                    OnLog($"=== RETURN: season template ({seasonsWithValidEpisodes.Count} seasons) ===");
                    
                    return Content(htmlContent, rjson ? "application/json; charset=utf-8" : "text/html; charset=utf-8");
                }
                // s >= 0: Показуємо озвучки + епізоди
                else if (s >= 0)
                {
                    var voicesForSeason = structure.Voices
                        .Where(v => v.Value.Seasons.ContainsKey(s))
                        .Select(v => new { DisplayName = v.Key, Info = v.Value })
                        .ToList();

                    if (!voicesForSeason.Any())
                    {
                        OnLog($"No voices found for season {s}");
                        OnLog("=== RETURN: no voices for season OnError ===");
                        return OnError("uaflix", proxyManager);
                    }

                    // Автоматично вибираємо першу озвучку якщо не вказана
                    if (string.IsNullOrEmpty(t))
                    {
                        t = voicesForSeason[0].DisplayName;
                        OnLog($"Auto-selected first voice: {t}");
                    }
                    
                    // Створюємо VoiceTpl з усіма озвучками
                    var voice_tpl = new VoiceTpl();
                    foreach (var voice in voicesForSeason)
                    {
                        string voiceLink = $"{host}/uaflix?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&t={HttpUtility.UrlEncode(voice.DisplayName)}&href={HttpUtility.UrlEncode(filmUrl)}";
                        bool isActive = voice.DisplayName == t;
                        voice_tpl.Append(voice.DisplayName, isActive, voiceLink);
                    }
                    OnLog($"Created VoiceTpl with {voicesForSeason.Count} voices, active: {t}");
                    
                    // Відображення епізодів для вибраної озвучки
                    if (!structure.Voices.ContainsKey(t))
                    {
                        OnLog($"Voice '{t}' not found in structure");
                        OnLog("=== RETURN: voice not found OnError ===");
                        return OnError("uaflix", proxyManager);
                    }

                    if (!structure.Voices[t].Seasons.ContainsKey(s))
                    {
                        OnLog($"Season {s} not found for voice '{t}'");
                        OnLog("=== RETURN: season not found for voice OnError ===");
                        return OnError("uaflix", proxyManager);
                    }

                    var episodes = structure.Voices[t].Seasons[s];
                    var episode_tpl = new EpisodeTpl();

                    foreach (var ep in episodes)
                    {
                        // Для zetvideo-vod повертаємо URL епізоду з методом call
                        // Для ashdi/zetvideo-serial повертаємо готове посилання з play
                        var voice = structure.Voices[t];
                        
                        if (voice.PlayerType == "zetvideo-vod" || voice.PlayerType == "ashdi-vod")
                        {
                            // Для zetvideo-vod та ashdi-vod використовуємо URL епізоду для виклику
                            // Потрібно передати URL епізоду в інший параметр, щоб не плутати з play=true
                            string callUrl = $"{host}/uaflix?episode_url={HttpUtility.UrlEncode(ep.File)}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&s={s}&e={ep.Number}";
                            episode_tpl.Append(
                                name: ep.Title,
                                title: title,
                                s: s.ToString(),
                                e: ep.Number.ToString(),
                                link: accsArgs(callUrl),
                                method: "call"
                            );
                        }
                        else
                        {
                            // Для багатосерійних плеєрів (ashdi-serial, zetvideo-serial) - пряме відтворення
                            string playUrl = HostStreamProxy(init, accsArgs(ep.File));
                            episode_tpl.Append(
                                name: ep.Title,
                                title: title,
                                s: s.ToString(),
                                e: ep.Number.ToString(),
                                link: playUrl
                            );
                        }
                    }

                    OnLog($"Created EpisodeTpl with {episodes.Count} episodes");
                    
                    // Повертаємо VoiceTpl + EpisodeTpl разом
                    if (rjson)
                    {
                        OnLog($"=== RETURN: episode template with voices JSON ({episodes.Count} episodes) ===");
                        return Content(episode_tpl.ToJson(voice_tpl), "application/json; charset=utf-8");
                    }
                    else
                    {
                        OnLog($"=== RETURN: voice + episode template HTML ({episodes.Count} episodes) ===");
                        return Content(voice_tpl.ToHtml() + episode_tpl.ToHtml(), "text/html; charset=utf-8");
                    }
                }

                // Fallback: якщо жоден з умов не виконався
                OnLog($"Fallback: s={s}, t={t}");
                OnLog("=== RETURN: fallback OnError ===");
                return OnError("uaflix", proxyManager);
            }
            else // Фільм
            {
                string link = $"{host}/uaflix?t={HttpUtility.UrlEncode(filmUrl)}&play=true";
                var tpl = new MovieTpl(title, original_title, 1);
                tpl.Append(title, accsArgs(link), method: "play");
                OnLog("=== RETURN: movie template ===");
                return rjson ? Content(tpl.ToJson(), "application/json; charset=utf-8") : Content(tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }
        
    }
}

