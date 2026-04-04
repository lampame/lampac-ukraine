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
using System.Text;
using Shared.Models.Online.Settings;
using Shared.Models;
using Uaflix.Models;

namespace Uaflix.Controllers
{

    public class Controller : BaseOnlineController<UaflixSettings>
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.UaFlix);
        }
        
        [HttpGet]
        [Route("lite/uaflix")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, int e = -1, bool play = false, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            if (await IsRequestBlocked(rch: false))
                return badInitMsg;

            var init = this.init;
            TryEnableMagicApn(init);
            OnLog($"=== UAFLIX INDEX START ===");
            OnLog($"Uaflix Index: title={title}, serial={serial}, s={s}, play={play}, href={href}, checksearch={checksearch}");
            OnLog($"Uaflix Index: kinopoisk_id={kinopoisk_id}, imdb_id={imdb_id}, id={id}");
            OnLog($"Uaflix Index: year={year}, source={source}, t={t}, e={e}, rjson={rjson}");

            var auth = new UaflixAuth(init, memoryCache, OnLog, proxyManager);
            var invoke = new UaflixInvoke(init, hybridCache, OnLog, proxyManager, auth, httpHydra);

            // Обробка параметра checksearch - повертаємо спеціальну відповідь для валідації
            if (checksearch)
            {
                if (!IsCheckOnlineSearchEnabled())
                    return OnError("uaflix", refresh_proxy: true);

                try
                {
                    bool hasContent = await invoke.CheckSearchAvailability(title, original_title);
                    if (hasContent)
                    {
                        OnLog("checksearch: Контент знайдено, повертаю валідаційний маркер");
                        OnLog("=== RETURN: checksearch validation (data-json=) ===");
                        return Content("data-json=", "text/plain; charset=utf-8");
                    }

                    OnLog("checksearch: Контент не знайдено");
                    OnLog("=== RETURN: checksearch OnError ===");
                    return OnError("uaflix", refresh_proxy: true);
                }
                catch (Exception ex)
                {
                    OnLog($"checksearch: помилка - {ex.Message}");
                    OnLog("=== RETURN: checksearch exception OnError ===");
                    return OnError("uaflix", refresh_proxy: true);
                }
            }

            if (play)
            {
                // Визначаємо URL для парсингу - або з параметра t, або з episode_url
                string urlToParse = !string.IsNullOrEmpty(t) ? t : Request.Query["episode_url"];
                if (string.IsNullOrWhiteSpace(urlToParse))
                {
                    OnLog("=== RETURN: play missing url OnError ===");
                    return OnError("uaflix", refresh_proxy: true);
                }
                
                var playResult = await invoke.ParseEpisode(urlToParse);
                if (playResult.streams != null && playResult.streams.Count > 0)
                {
                    OnLog("=== RETURN: play redirect ===");
                    return UpdateService.Validate(Redirect(BuildStreamUrl(init, playResult.streams.First().link)));
                }
                
                OnLog("=== RETURN: play no streams ===");
                return OnError("uaflix", refresh_proxy: true);
            }
            
            // Якщо є episode_url але немає play=true, це виклик для отримання інформації про стрім (для method: 'call')
            string episodeUrl = Request.Query["episode_url"];
            if (!string.IsNullOrEmpty(episodeUrl))
            {
                var playResult = await invoke.ParseEpisode(episodeUrl);
                if (playResult.streams != null && playResult.streams.Count > 0)
                {
                    // Повертаємо JSON з інформацією про стрім для методу 'play'
                    string streamUrl = BuildStreamUrl(init, playResult.streams.First().link);
                    string jsonResult = $"{{\"method\":\"play\",\"url\":\"{streamUrl}\",\"title\":\"{title ?? original_title}\"}}";
                    OnLog($"=== RETURN: call method JSON for episode_url ===");
                    return UpdateService.Validate(Content(jsonResult, "application/json; charset=utf-8"));
                }
                
                OnLog("=== RETURN: call method no streams ===");
                return OnError("uaflix", refresh_proxy: true);
            }

            string filmUrl = href;

            if (string.IsNullOrEmpty(filmUrl))
            {
                var searchResults = await invoke.Search(imdb_id, kinopoisk_id, title, original_title, year, serial, original_language, source, title);
                if (searchResults == null || searchResults.Count == 0)
                {
                    OnLog("No search results found");
                    OnLog("=== RETURN: no search results OnError ===");
                    return OnError("uaflix", refresh_proxy: true);
                }

                var selectedResult = invoke.SelectBestSearchResult(searchResults, title, original_title, year);
                if (selectedResult == null && searchResults.Count == 1)
                    selectedResult = searchResults[0];

                if (selectedResult != null)
                {
                    filmUrl = selectedResult.Url;
                    OnLog($"Auto-selected best search result: {selectedResult.Url} (score={selectedResult.MatchScore}, year={selectedResult.Year})");
                }
                else
                {
                    var orderedResults = searchResults
                        .OrderByDescending(i => i.MatchScore)
                        .ToList();

                    var similar_tpl = new SimilarTpl(orderedResults.Count);
                    foreach (var res in orderedResults)
                    {
                        string link = $"{host}/lite/uaflix?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(res.Url)}";
                        string y = res.Year > 0 ? res.Year.ToString() : string.Empty;
                        string details = res.Category switch
                        {
                            "films" => "Фільм",
                            "serials" => "Серіал",
                            "anime" => "Аніме",
                            _ => string.Empty
                        };

                        similar_tpl.Append(res.Title, y, details, link, res.PosterUrl);
                    }

                    OnLog($"=== RETURN: similar items ({orderedResults.Count}) ===");
                    return rjson ? Content(similar_tpl.ToJson(), "application/json; charset=utf-8") : Content(similar_tpl.ToHtml(), "text/html; charset=utf-8");
                }
            }

            if (serial == 1)
            {
                // s == -1: швидкий вибір сезону без повної агрегації серіалу
                if (s == -1)
                {
                    var seasonIndex = await invoke.GetSeasonIndex(filmUrl);
                    var seasons = seasonIndex?.Seasons?.Keys
                        .Distinct()
                        .OrderBy(sn => sn)
                        .ToList();

                    if (seasons == null || seasons.Count == 0)
                    {
                        OnLog("No seasons found in season index");
                        OnLog("=== RETURN: no seasons OnError ===");
                        return OnError("uaflix", refresh_proxy: true);
                    }

                    var season_tpl = new SeasonTpl(seasons.Count);
                    foreach (int season in seasons)
                    {
                        string link = $"{host}/lite/uaflix?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={season}&href={HttpUtility.UrlEncode(filmUrl)}";
                        if (!string.IsNullOrWhiteSpace(t))
                            link += $"&t={HttpUtility.UrlEncode(t)}";

                        season_tpl.Append($"{season}", link, season.ToString());
                    }

                    OnLog($"=== RETURN: season template ({seasons.Count} seasons) ===");
                    return Content(
                        rjson ? season_tpl.ToJson() : season_tpl.ToHtml(),
                        rjson ? "application/json; charset=utf-8" : "text/html; charset=utf-8"
                    );
                }

                // s >= 0: завантажуємо тільки потрібний сезон
                if (s >= 0)
                {
                    var structure = await invoke.GetSeasonStructure(filmUrl, s);
                    if (structure == null || structure.Voices == null || structure.Voices.Count == 0)
                    {
                        OnLog($"No voices found for season {s}");
                        OnLog("=== RETURN: no voices for season OnError ===");
                        return OnError("uaflix", refresh_proxy: true);
                    }
                    var voicesForSeason = structure.Voices
                        .Select(v => new { DisplayName = v.Key, Info = v.Value })
                        .ToList();

                    if (!voicesForSeason.Any())
                    {
                        OnLog($"No voices found for season {s}");
                        OnLog("=== RETURN: no voices for season OnError ===");
                        return OnError("uaflix", refresh_proxy: true);
                    }

                    // Автоматично вибираємо першу озвучку якщо не вказана
                    if (string.IsNullOrEmpty(t))
                    {
                        t = voicesForSeason[0].DisplayName;
                        OnLog($"Auto-selected first voice: {t}");
                    }
                    else if (!structure.Voices.ContainsKey(t))
                    {
                        t = voicesForSeason[0].DisplayName;
                        OnLog($"Voice '{t}' not found, fallback to first voice: {t}");
                    }

                    VoiceInfo selectedVoice = null;
                    if (!structure.Voices.TryGetValue(t, out selectedVoice) || !selectedVoice.Seasons.ContainsKey(s) || selectedVoice.Seasons[s] == null || selectedVoice.Seasons[s].Count == 0)
                    {
                        var fallbackVoice = voicesForSeason.FirstOrDefault(v => v.Info.Seasons.ContainsKey(s) && v.Info.Seasons[s] != null && v.Info.Seasons[s].Count > 0);
                        if (fallbackVoice == null)
                        {
                            OnLog($"Season {s} not found for selected voice and fallback voice missing");
                            OnLog("=== RETURN: season not found for voice OnError ===");
                            return OnError("uaflix", refresh_proxy: true);
                        }

                        t = fallbackVoice.DisplayName;
                        selectedVoice = fallbackVoice.Info;
                        OnLog($"Selected voice had no episodes, fallback to: {t}");
                    }

                    // Створюємо VoiceTpl з усіма озвучками
                    var voice_tpl = new VoiceTpl();
                    foreach (var voice in voicesForSeason)
                    {
                        string voiceLink = $"{host}/lite/uaflix?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&href={HttpUtility.UrlEncode(filmUrl)}";
                        voiceLink += $"&s={s}&t={HttpUtility.UrlEncode(voice.DisplayName)}";

                        bool isActive = voice.DisplayName == t;
                        voice_tpl.Append(voice.DisplayName, isActive, voiceLink);
                    }
                    OnLog($"Created VoiceTpl with {voicesForSeason.Count} voices, active: {t}");

                    var episodes = selectedVoice.Seasons[s];
                    var episode_tpl = new EpisodeTpl();
                    int appendedEpisodes = 0;

                    foreach (var ep in episodes)
                    {
                        if (ep == null || string.IsNullOrWhiteSpace(ep.File))
                            continue;

                        string episodeTitle = !string.IsNullOrWhiteSpace(ep.Title) ? ep.Title : $"Епізод {ep.Number}";

                        // Для zetvideo-vod повертаємо URL епізоду з методом call
                        // Для ashdi/zetvideo-serial повертаємо готове посилання з play
                        var voice = selectedVoice;
                        
                        if (voice.PlayerType == "zetvideo-vod" || voice.PlayerType == "ashdi-vod")
                        {
                            // Для zetvideo-vod та ashdi-vod використовуємо URL епізоду для виклику
                            // Потрібно передати URL епізоду в інший параметр, щоб не плутати з play=true
                            string callUrl = $"{host}/lite/uaflix?episode_url={HttpUtility.UrlEncode(ep.File)}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&s={s}&e={ep.Number}";
                            episode_tpl.Append(
                                name: episodeTitle,
                                title: title,
                                s: s.ToString(),
                                e: ep.Number.ToString(),
                                link: accsArgs(callUrl),
                                method: "call",
                                streamlink: accsArgs($"{callUrl}&play=true")
                            );
                        }
                        else
                        {
                            // Для багатосерійних плеєрів (ashdi-serial, zetvideo-serial) - пряме відтворення
                            string playUrl = BuildStreamUrl(init, ep.File);
                            episode_tpl.Append(
                                name: episodeTitle,
                                title: title,
                                s: s.ToString(),
                                e: ep.Number.ToString(),
                                link: playUrl
                            );
                        }

                        appendedEpisodes++;
                    }

                    if (appendedEpisodes == 0)
                    {
                        OnLog($"No valid episodes after filtering for season {s}, voice {t}");
                        OnLog("=== RETURN: no valid episodes OnError ===");
                        return OnError("uaflix", refresh_proxy: true);
                    }

                    OnLog($"Created EpisodeTpl with {appendedEpisodes} episodes");
                    
                    // Повертаємо VoiceTpl + EpisodeTpl разом
                    episode_tpl.Append(voice_tpl);
                    if (rjson)
                    {
                        OnLog($"=== RETURN: episode template with voices JSON ({episodes.Count} episodes) ===");
                        return Content(episode_tpl.ToJson(), "application/json; charset=utf-8");
                    }
                    else
                    {
                        OnLog($"=== RETURN: voice + episode template HTML ({episodes.Count} episodes) ===");
                        return Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
                    }
                }

                // Fallback: якщо жоден з умов не виконався
                OnLog($"Fallback: s={s}, t={t}");
                OnLog("=== RETURN: fallback OnError ===");
                return OnError("uaflix", refresh_proxy: true);
            }
            else // Фільм
            {
                var playResult = await invoke.ParseEpisode(filmUrl);
                if (playResult?.streams == null || playResult.streams.Count == 0)
                {
                    OnLog("=== RETURN: movie no streams ===");
                    return OnError("uaflix", refresh_proxy: true);
                }

                var tpl = new MovieTpl(title, original_title, playResult.streams.Count);
                int index = 1;
                foreach (var stream in playResult.streams)
                {
                    if (stream == null || string.IsNullOrEmpty(stream.link))
                        continue;

                    string label = !string.IsNullOrWhiteSpace(stream.title)
                        ? stream.title
                        : $"Варіант {index}";

                    tpl.Append(label, BuildStreamUrl(init, stream.link));
                    index++;
                }

                if (tpl.data == null || tpl.data.Count == 0)
                {
                    OnLog("=== RETURN: movie template empty ===");
                    return OnError("uaflix", refresh_proxy: true);
                }

                OnLog("=== RETURN: movie template ===");
                return rjson ? Content(tpl.ToJson(), "application/json; charset=utf-8") : Content(tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
        {
            string link = StripLampacArgs(streamLink?.Trim());
            if (string.IsNullOrEmpty(link))
                return link;

            if (ApnHelper.IsEnabled(init))
            {
                if (ModInit.ApnHostProvided || ApnHelper.IsAshdiUrl(link))
                    return ApnHelper.WrapUrl(init, link);

                var noApn = (OnlinesSettings)init.Clone();
                noApn.apnstream = false;
                noApn.apn = null;
                return HostStreamProxy(noApn, link);
            }

            return HostStreamProxy(init, link);
        }

        private void TryEnableMagicApn(OnlinesSettings init)
        {
            if (init == null
                || init.apn != null
                || init.streamproxy
                || string.IsNullOrWhiteSpace(ModInit.MagicApnAshdiHost))
                return;

            string player = new RchClient(HttpContext, host, init, requestInfo).InfoConnected()?.player;
            bool useInnerPlayer = string.IsNullOrWhiteSpace(player)
                || player.Equals("inner", StringComparison.OrdinalIgnoreCase);
            if (!useInnerPlayer)
                return;

            ApnHelper.ApplyInitConf(true, ModInit.MagicApnAshdiHost, init);
            OnLog($"Uaflix: увімкнено magic_apn для Ashdi (player={player ?? "unknown"}).");
        }

        private static string StripLampacArgs(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                url,
                @"([?&])(account_email|uid|nws_id)=[^&]*",
                "$1",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            cleaned = cleaned.Replace("?&", "?").Replace("&&", "&").TrimEnd('?', '&');
            return cleaned;
        }

        private static bool IsCheckOnlineSearchEnabled()
        {
            try
            {
                var onlineType = Type.GetType("Online.ModInit");
                if (onlineType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        onlineType = asm.GetType("Online.ModInit");
                        if (onlineType != null)
                            break;
                    }
                }
                var confField = onlineType?.GetField("conf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var conf = confField?.GetValue(null);
                var checkProp = conf?.GetType().GetProperty("checkOnlineSearch", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (checkProp?.GetValue(conf) is bool enabled)
                    return enabled;
            }
            catch
            {
            }

            return true;
        }

        private static void OnLog(string message)
        {
            System.Console.WriteLine(message);
        }
    }
}
