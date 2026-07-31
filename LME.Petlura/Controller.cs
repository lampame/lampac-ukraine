using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using LME.Petlura.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace LME.Petlura.Controllers
{
    public class Controller : BaseOnlineController<PetluraSettings>
    {
        public Controller() : base(ModInit.Settings)
        {
        }

        [HttpGet]
        [Route("lite/lme_petlura")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = this.init;
            if (!init.enable)
                return Forbid();

            var invoke = new PetluraInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            // Petlura працює тільки з imdb_id
            if (string.IsNullOrWhiteSpace(imdb_id))
                return OnError("lme_petlura", refresh_proxy: true);

            string embedId = null;

            if (checksearch)
            {
                if (!StreamHelper.IsCheckOnlineSearchEnabled())
                    return OnError("lme_petlura", refresh_proxy: true);

                embedId = await invoke.ResolveEmbedTail(imdb_id);
                if (!string.IsNullOrEmpty(embedId))
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("lme_petlura", refresh_proxy: true);
            }

            if (!string.IsNullOrEmpty(href))
                embedId = href;
            else
            {
                embedId = await invoke.ResolveEmbedTail(imdb_id);
                if (string.IsNullOrEmpty(embedId))
                    return OnError("lme_petlura", refresh_proxy: true);
            }

            if (serial == 1)
            {
                var seasons = await invoke.ParseSeasons(embedId);
                if (seasons == null || seasons.Count == 0)
                    return OnError("lme_petlura", refresh_proxy: true);

                if (s == -1)
                {
                    var seasonTpl = new SeasonTpl(seasons.Count);
                    foreach (var season in seasons)
                    {
                        int seasonNum = ExtractSeasonNumber(season.title);
                        string link = $"{host}/lite/lme_petlura?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={seasonNum}&href={HttpUtility.UrlEncode(embedId)}";
                        seasonTpl.Append(season.title, link, seasonNum.ToString());
                    }
                    return rjson ? Content(seasonTpl.ToJson(), "application/json; charset=utf-8") : Content(seasonTpl.ToHtml(), "text/html; charset=utf-8");
                }

                var selectedSeason = seasons.FirstOrDefault(sn => ExtractSeasonNumber(sn.title) == s) ?? seasons[0];
                if (selectedSeason?.folder == null || selectedSeason.folder.Count == 0)
                    return OnError("lme_petlura", refresh_proxy: true);

                var voices = selectedSeason.folder;
                if (string.IsNullOrEmpty(t))
                    t = voices[0].title;

                var voiceTpl = new VoiceTpl(voices.Count);
                foreach (var voice in voices)
                {
                    string voiceLink = $"{host}/lite/lme_petlura?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&t={HttpUtility.UrlEncode(voice.title)}&href={HttpUtility.UrlEncode(embedId)}";
                    voiceTpl.Append(voice.title, string.Equals(voice.title, t, StringComparison.OrdinalIgnoreCase), voiceLink);
                }

                var selectedVoice = voices.FirstOrDefault(v => string.Equals(v.title, t, StringComparison.OrdinalIgnoreCase)) ?? voices[0];
                if (selectedVoice.folder == null || selectedVoice.folder.Count == 0)
                    return OnError("lme_petlura", refresh_proxy: true);

                var episodeTpl = new EpisodeTpl(selectedVoice.folder.Count);
                int epIndex = 1;
                foreach (var ep in selectedVoice.folder)
                {
                    string epName = ep.title ?? $"Епізод {epIndex}";
                    int epNum = ExtractEpisodeNumber(ep.title, epIndex);

                    SubtitleTpl subtitles = null;
                    var subInfo = invoke.ParseSubtitle(ep.subtitle);
                    if (subInfo != null)
                    {
                        subtitles = new SubtitleTpl(1);
                        subtitles.Append(subInfo.Lang, subInfo.Url);
                    }

                    string streamUrl = BuildStreamUrl(init, ep.file);
                    episodeTpl.Append(epName, title ?? original_title, s.ToString(), epNum.ToString("D2"), streamUrl, subtitles: subtitles);
                    epIndex++;
                }

                episodeTpl.Append(voiceTpl);
                return rjson ? Content(episodeTpl.ToJson(), "application/json; charset=utf-8") : Content(episodeTpl.ToHtml(), "text/html; charset=utf-8");
            }
            else
            {
                string streamUrl = await invoke.GetMovieStream(embedId);
                if (string.IsNullOrEmpty(streamUrl))
                    return OnError("lme_petlura", refresh_proxy: true);

                var movieTpl = new MovieTpl(title, original_title);
                movieTpl.Append("HD", BuildStreamUrl(init, streamUrl));
                return rjson ? Content(movieTpl.ToJson(), "application/json; charset=utf-8") : Content(movieTpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        private static int ExtractSeasonNumber(string title)
        {
            if (string.IsNullOrEmpty(title)) return 1;
            var match = System.Text.RegularExpressions.Regex.Match(title, @"(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 1;
        }

        private static int ExtractEpisodeNumber(string title, int defaultNum)
        {
            if (string.IsNullOrEmpty(title)) return defaultNum;
            var match = System.Text.RegularExpressions.Regex.Match(title, @"(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : defaultNum;
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
            => StreamHelper.BuildStreamUrl(init, streamLink, ModInit.ApnHostProvided, (s, l) => HostStreamProxy(s, l));

        private static void OnLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            // ponytail: filter out success logs, keep only errors/cancellations
            if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("помилка", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("reject", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("limit", StringComparison.OrdinalIgnoreCase))
            {
                System.Console.WriteLine(message);
            }
        }
    }
}
