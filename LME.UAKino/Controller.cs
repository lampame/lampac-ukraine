using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using LME.UAKino.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace LME.UAKino.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.UAKino);
        }

        [HttpGet]
        [Route("lite/lme_uakino")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.UAKino);
            if (!init.enable)
                return Forbid();

            var invoke = new UAKinoInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            if (checksearch)
            {
                if (!IsCheckOnlineSearchEnabled())
                    return OnError("lme_uakino", refresh_proxy: true);

                var searchResults = await invoke.Search(title, original_title, year, imdb_id);
                if (searchResults != null && searchResults.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("lme_uakino", refresh_proxy: true);
            }

            string newsId = null;
            string itemUrl = href;

            if (string.IsNullOrEmpty(itemUrl))
            {
                // === ПЕРШИЙ ЗАПИТ: пошук ===
                var searchResults = await invoke.Search(title, original_title, year, imdb_id);
                if (searchResults == null || searchResults.Count == 0)
                    return OnError("lme_uakino", refresh_proxy: true);

                if (serial == 1)
                {
                    // Серіал
                    if (searchResults.Count == 1)
                    {
                        var sr = searchResults[0];
                        if (sr.Seasons.Count > 1 && s == -1)
                        {
                            // Кілька сезонів — показуємо SeasonTpl для вибору
                            return HandleSeasonSelection(sr, id, imdb_id, kinopoisk_id, title, original_title, year, rjson);
                        }
                        // Один сезон — використовуємо його
                        itemUrl = sr.Seasons[0].Url;
                        newsId = sr.Seasons[0].NewsId;
                    }
                    else
                    {
                        // Кілька різних шоу — обирає
                        return ShowSimilarTpl(searchResults, id, imdb_id, kinopoisk_id, title, original_title, year, serial, rjson);
                    }
                }
                else
                {
                    // Фільм
                    if (searchResults.Count > 1)
                    {
                        return ShowSimilarTpl(searchResults, id, imdb_id, kinopoisk_id, title, original_title, year, serial, rjson);
                    }
                    itemUrl = searchResults[0].Seasons[0].Url;
                    newsId = searchResults[0].Seasons[0].NewsId;
                }
            }
            else
            {
                // Повторний запит (з селектора сезонів або озвучок)
                newsId = UAKinoInvoke.ExtractNewsId(itemUrl);
            }

            if (string.IsNullOrEmpty(newsId))
                return OnError("lme_uakino", refresh_proxy: true);

            var voices = await invoke.GetPlaylist(newsId);
            if (voices == null || voices.Count == 0)
            {
                // Fallback: playlist API повернув ERR_NOT_DATA — пробуємо зі сторінки
                string fallbackUrl = await invoke.GetPageFallbackUrl(itemUrl);
                if (!string.IsNullOrEmpty(fallbackUrl))
                {
                    if (serial == 1)
                    {
                        var voice_tpl = new VoiceTpl();
                        var episode_tpl = new EpisodeTpl();
                        string resolvedUrl = await invoke.ResolveAshdiVod(fallbackUrl);
                        string streamUrl = BuildStreamUrl(init, resolvedUrl);
                        voice_tpl.Append("Озвучення", true, null);
                        episode_tpl.Append("Епізод 1", title ?? original_title, s >= 0 ? s.ToString() : "1", "01", streamUrl);
                        episode_tpl.Append(voice_tpl);
                        return rjson
                            ? Content(episode_tpl.ToJson(), "application/json; charset=utf-8")
                            : Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
                    }
                    else
                    {
                        var resolvedStreams = await invoke.ResolveAshdiVodAll(fallbackUrl);
                        var movie_tpl = new MovieTpl(title, original_title, resolvedStreams.Count);
                        foreach (var (file, label) in resolvedStreams)
                        {
                            string displayLabel = !string.IsNullOrEmpty(label) ? label : "Фільм";
                            string streamUrl = BuildStreamUrl(init, file);
                            movie_tpl.Append(displayLabel, streamUrl);
                        }
                        return rjson
                            ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8")
                            : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
                    }
                }
                return OnError("lme_uakino", refresh_proxy: true);
            }

            if (serial == 1)
            {
                return await HandleSerial(init, voices, title, original_title, imdb_id, kinopoisk_id, itemUrl, s, t, rjson, invoke);
            }
            else
            {
                return await HandleMovie(init, voices, title, original_title, rjson, invoke);
            }
        }

        /// <summary>Вибір сезону для багатосезонного серіалу</summary>
        private ActionResult HandleSeasonSelection(SearchResult sr, long id, string imdb_id, long kinopoisk_id, string title, string original_title, int year, bool rjson)
        {
            var season_tpl = new SeasonTpl(sr.Seasons.Count);
            foreach (var season in sr.Seasons)
            {
                string link = $"{host}/lite/lme_uakino?id={id}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={season.SeasonNumber}&href={HttpUtility.UrlEncode(season.Url)}";
                season_tpl.Append($"Сезон {season.SeasonNumber}", link, season.SeasonNumber.ToString());
            }

            return rjson
                ? Content(season_tpl.ToJson(), "application/json; charset=utf-8")
                : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        /// <summary>Вибір між різними шоу/фільмами</summary>
        private ActionResult ShowSimilarTpl(List<SearchResult> searchResults, long id, string imdb_id, long kinopoisk_id, string title, string original_title, int year, int serial, bool rjson)
        {
            var similar_tpl = new SimilarTpl(searchResults.Count);
            foreach (var res in searchResults)
            {
                string seasonUrl = res.Seasons.Count > 0 ? res.Seasons[0].Url : "";
                string yearStr = res.Seasons.Count > 0 ? (res.Seasons[0].Year?.ToString() ?? "") : (res.Year?.ToString() ?? "");
                string link = $"{host}/lite/lme_uakino?id={id}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(seasonUrl)}";
                similar_tpl.Append(res.Title, yearStr, res.OriginalTitle ?? "", link, res.Poster);
            }

            return rjson
                ? Content(similar_tpl.ToJson(), "application/json; charset=utf-8")
                : Content(similar_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        /// <summary>Серіал: озвучки + епізоди</summary>
        private async Task<ActionResult> HandleSerial(OnlinesSettings init, List<VoiceGroup> voices, string title, string original_title, string imdb_id, long kinopoisk_id, string itemUrl, int s, string t, bool rjson, UAKinoInvoke invoke)
        {
            var voice_tpl = new VoiceTpl();
            var episode_tpl = new EpisodeTpl();

            if (string.IsNullOrEmpty(t))
                t = voices.First().DataId;

            foreach (var voice in voices)
            {
                string voiceLink = $"{host}/lite/lme_uakino?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial=1&s={s}&t={voice.DataId}&href={HttpUtility.UrlEncode(itemUrl)}";
                voice_tpl.Append(voice.Name, voice.DataId == t, voiceLink);
            }

            var selected = voices.FirstOrDefault(v => v.DataId == t);
            if (selected == null || selected.Episodes.Count == 0)
                return OnError("lme_uakino", refresh_proxy: true);

            string seasonStr = s >= 0 ? s.ToString() : "1";
            foreach (var ep in selected.Episodes.OrderBy(e => e.EpisodeNumber ?? int.MaxValue))
            {
                int epNum = ep.EpisodeNumber ?? 1;
                string epName = string.IsNullOrEmpty(ep.Title) ? $"Епізод {epNum}" : ep.Title;
                string fileUrl = await invoke.ResolveAshdiVod(ep.FileUrl);
                string streamUrl = BuildStreamUrl(init, fileUrl);
                episode_tpl.Append(epName, title ?? original_title, seasonStr, epNum.ToString("D2"), streamUrl);
            }

            episode_tpl.Append(voice_tpl);

            return rjson
                ? Content(episode_tpl.ToJson(), "application/json; charset=utf-8")
                : Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        /// <summary>Фільм: список стрімів</summary>
        private async Task<ActionResult> HandleMovie(OnlinesSettings init, List<VoiceGroup> voices, string title, string original_title, bool rjson, UAKinoInvoke invoke)
        {
            var processed = new HashSet<string>();
            var movie_tpl = new MovieTpl(title, original_title);

            foreach (var voice in voices)
            {
                foreach (var ep in voice.Episodes)
                {
                    string label = voice.Name;
                    if (voices.Count == 1 && voice.Episodes.Count > 1)
                        label = ep.Title;

                    string fileUrl = ep.FileUrl;
                    // Резолвимо Ashdi VOD — отримуємо реальний .m3u8 стрім
                    string resolvedUrl = await invoke.ResolveAshdiVod(fileUrl);
                    // Дедуплікація: якщо той самий стрім — пропускаємо
                    if (!processed.Add(resolvedUrl))
                        continue;

                    string streamUrl = BuildStreamUrl(init, resolvedUrl);
                    movie_tpl.Append(label, streamUrl);
                }
            }

            return rjson
                ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8")
                : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
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
