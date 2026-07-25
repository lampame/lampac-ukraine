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

            string embedTail = null;

            if (checksearch)
            {
                if (!StreamHelper.IsCheckOnlineSearchEnabled())
                    return OnError("lme_petlura", refresh_proxy: true);

                // Перевіряємо чи є embed для цього imdb_id
                embedTail = await invoke.ResolveEmbedTail(imdb_id);
                if (!string.IsNullOrEmpty(embedTail))
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("lme_petlura", refresh_proxy: true);
            }

            // Отримуємо embed tail
            if (!string.IsNullOrEmpty(href))
            {
                embedTail = href;
            }
            else
            {
                embedTail = await invoke.ResolveEmbedTail(imdb_id);
                if (string.IsNullOrEmpty(embedTail))
                    return OnError("lme_petlura", refresh_proxy: true);
            }

            if (serial == 1)
            {
                var serialInfo = await invoke.GetSerialEpisodes(embedTail);
                if (serialInfo == null || serialInfo.Voices.Count == 0)
                    return OnError("lme_petlura", refresh_proxy: true);

                var voice_tpl = new VoiceTpl();
                var episode_tpl = new EpisodeTpl();

                // Вибір голосу
                string selectedVoice;
                var availableVoices = serialInfo.Voices;

                if (string.IsNullOrEmpty(t))
                    selectedVoice = availableVoices[0].Name;
                else
                    selectedVoice = t;

                // Шукаємо вибраний голос, або беремо перший
                var voiceEpisodes = availableVoices.FirstOrDefault(v =>
                    string.Equals(v.Name, selectedVoice, StringComparison.OrdinalIgnoreCase));
                if (voiceEpisodes == null)
                    voiceEpisodes = availableVoices[0];

                // Формуємо VoiceTpl
                foreach (var voice in availableVoices)
                {
                    string voiceLink = $"{host}/lite/lme_petlura?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&t={HttpUtility.UrlEncode(voice.Name)}&href={HttpUtility.UrlEncode(embedTail)}";
                    bool isActive = string.Equals(voice.Name, voiceEpisodes.Name, StringComparison.OrdinalIgnoreCase);
                    voice_tpl.Append(voice.Name, isActive, voiceLink);
                }

                // Формуємо EpisodeTpl
                int index = 1;
                foreach (var ep in voiceEpisodes.Episodes.OrderBy(e => e.Episode ?? int.MaxValue))
                {
                    int episodeNumber = ep.Episode ?? index;
                    string episodeName = string.IsNullOrEmpty(ep.Title) ? $"Епізод {episodeNumber}" : ep.Title;
                    string streamUrl = BuildStreamUrl(init, ep.Url);
                    episode_tpl.Append(episodeName, title ?? original_title, "1", episodeNumber.ToString("D2"), streamUrl);
                    index++;
                }

                episode_tpl.Append(voice_tpl);

                if (rjson)
                    return Content(episode_tpl.ToJson(), "application/json; charset=utf-8");

                return Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
            }
            else // Фільм
            {
                var streams = await invoke.GetMovieStreams(embedTail);
                if (streams == null || streams.Count == 0)
                    return OnError("lme_petlura", refresh_proxy: true);

                var movie_tpl = new MovieTpl(title, original_title);
                for (int i = 0; i < streams.Count; i++)
                {
                    var stream = streams[i];
                    string label = !string.IsNullOrEmpty(stream.Title) ? stream.Title : $"Варіант {i + 1}";
                    string streamUrl = BuildStreamUrl(init, stream.Url);

                    SubtitleTpl subtitles = null;
                    if (stream.Subtitles != null && stream.Subtitles.Count > 0)
                    {
                        subtitles = new SubtitleTpl();
                        foreach (var sub in stream.Subtitles)
                        {
                            if (!string.IsNullOrEmpty(sub.Lang) && !string.IsNullOrEmpty(sub.Url))
                                subtitles.Append(sub.Lang, sub.Url);
                        }
                    }

                    movie_tpl.Append(label, streamUrl, subtitles: subtitles);
                }

                return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
            => StreamHelper.BuildStreamUrl(init, streamLink, ModInit.ApnHostProvided, (s, l) => HostStreamProxy(s, l));

        private static void OnLog(string message)
        {
            System.Console.WriteLine(message);
        }
    }
}
