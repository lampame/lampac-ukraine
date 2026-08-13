using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using LME.Common.Engine;
using LME.Franko.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;

namespace LME.Franko
{
    [Route("lite/lme_franko")]
    public class FrankoController : BaseOnlineController
    {
        ProxyManager proxyManager;

        public FrankoController() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.Franko);
        }

        [HttpGet]
        public async Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.Franko);
            if (!init.enable)
                return OnError();
            Initialization(init);

            var invoke = new FrankoInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            if (checksearch)
            {
                if (!StreamHelper.IsCheckOnlineSearchEnabled())
                    return OnError("lme_franko", refresh_proxy: true);

                // Franko працює виключно за imdb_id — без нього перевірка неможлива.
                if (string.IsNullOrWhiteSpace(imdb_id))
                    return OnError("lme_franko", refresh_proxy: true);

                return Content("data-json=", "text/plain; charset=utf-8");
            }

            if (string.IsNullOrWhiteSpace(imdb_id))
                return OnError("lme_franko", refresh_proxy: true);

            OnLog($"lme_franko: {title} (imdb_id={imdb_id}, serial={serial}, s={s}, t={t})");

            var searchResult = await invoke.Search(imdb_id);
            if (searchResult == null || searchResult.Payload == null)
                return OnError("lme_franko", refresh_proxy: true);

            if (!searchResult.IsSerial)
                return await HandleMovie(searchResult, imdb_id, title, original_title, year, t, rjson, invoke, init);

            return await HandleSerial(searchResult, imdb_id, title, original_title, year, t, s, rjson, invoke, init);
        }

        /// <summary>
        /// Відкладений резолв стріму епізоду (per-episode POST до player files API).
        /// </summary>
        [HttpGet]
        [Route("play")]
        public async Task<ActionResult> Play(string imdb_id, string title, string original_title, int year, string t, int s, int e, bool play = false, bool rjson = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.Franko);
            if (!init.enable)
                return OnError();
            Initialization(init);

            OnLog($"lme_franko play: {title} (s={s}, e={e}, t={t})");

            var invoke = new FrankoInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            var searchResult = await invoke.Search(imdb_id);
            if (searchResult == null || searchResult.Payload == null)
                return OnError("lme_franko", refresh_proxy: true);

            int? tid = int.TryParse(t, out int parsedTid) ? parsedTid : (int?)null;
            var stream = await invoke.ResolveStream(searchResult.Id, tid, s, e);
            if (stream == null || string.IsNullOrEmpty(stream.Url))
                return OnError("lme_franko", refresh_proxy: true);

            string streamUrl = BuildStreamUrl(init, stream.Url);
            string episodeTitle = $"{title ?? original_title} - {s}x{e:D2}";

            if (play)
                return UpdateService.Validate(Redirect(streamUrl));

            return UpdateService.Validate(Content(VideoTpl.ToJson("play", streamUrl, episodeTitle), "application/json; charset=utf-8"));
        }

        private async Task<ActionResult> HandleMovie(FrankoSearchResult result, string imdb_id, string title, string original_title, int year, string t, bool rjson, FrankoInvoke invoke, FrankoConfig init)
        {
            var translations = result.Payload?.translations ?? new List<FrankoTranslation>();
            if (translations.Count == 0)
                return OnError("lme_franko", refresh_proxy: true);

            // Без вибору озвучки — список перекладів (VoiceTpl).
            if (!int.TryParse(t, out int tid) || !translations.Any(x => x.id == tid))
                return VoiceTplResult(BuildVoiceTpl(translations, imdb_id, title, original_title, year, null), rjson);

            var stream = await invoke.ResolveStream(result.Id, tid, null, null);
            if (stream == null || string.IsNullOrEmpty(stream.Url))
                return OnError("lme_franko", refresh_proxy: true);

            var selected = translations.First(x => x.id == tid);
            string streamUrl = BuildStreamUrl(init, stream.Url);

            var movie_tpl = new MovieTpl(title, original_title);
            movie_tpl.Append(selected.title, streamUrl);

            return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        private async Task<ActionResult> HandleSerial(FrankoSearchResult result, string imdb_id, string title, string original_title, int year, string t, int s, bool rjson, FrankoInvoke invoke, FrankoConfig init)
        {
            var payload = result.Payload;
            var translations = payload?.translations ?? new List<FrankoTranslation>();
            if (translations.Count == 0)
                return OnError("lme_franko", refresh_proxy: true);

            // Без вибору озвучки — список перекладів (VoiceTpl).
            if (!int.TryParse(t, out int tid) || !translations.Any(x => x.id == tid))
                return VoiceTplResult(BuildVoiceTpl(translations, imdb_id, title, original_title, year, null), rjson);

            // Сезони спільні для всіх перекладів (з player payload).
            var seasons = (payload.seasons_episodes ?? new Dictionary<string, List<int>>())
                .Keys
                .Select(k => int.TryParse(k, out int n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n.Value)
                .OrderBy(n => n)
                .ToList();

            if (seasons.Count == 0)
                return OnError("lme_franko", refresh_proxy: true);

            // Вибір сезону (SeasonTpl).
            if (s == -1)
            {
                var season_tpl = new SeasonTpl();
                foreach (var seasonNum in seasons)
                {
                    string link = $"{host}/lite/lme_franko?imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title ?? string.Empty)}&original_title={HttpUtility.UrlEncode(original_title ?? string.Empty)}&year={year}&t={tid}&s={seasonNum}";
                    season_tpl.Append($"Сезон {seasonNum}", link, seasonNum.ToString());
                }

                return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
            }

            // Список епізодів сезону (EpisodeTpl) + перемикач озвучок.
            if (!payload.seasons_episodes.TryGetValue(s.ToString(), out var episodes) || episodes == null || episodes.Count == 0)
                return OnError("lme_franko", refresh_proxy: true);

            var voice_tpl = BuildVoiceTpl(translations, imdb_id, title, original_title, year, tid, s);

            var episode_tpl = new EpisodeTpl();
            foreach (var ep in episodes.OrderBy(e => e))
            {
                // Відкладений резолв (патерн "call"): стрім епізоду резолвиться через /play
                string playLink = $"{host}/lite/lme_franko/play?imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title ?? string.Empty)}&original_title={HttpUtility.UrlEncode(original_title ?? string.Empty)}&year={year}&t={tid}&s={s}&e={ep}";
                episode_tpl.Append($"Епізод {ep}", title ?? original_title, s.ToString(), ep.ToString("D2"), accsArgs(playLink), "call");
            }

            episode_tpl.Append(voice_tpl);
            if (rjson)
                return Content(episode_tpl.ToJson(), "application/json; charset=utf-8");

            return Content(episode_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        /// <summary>
        /// Список перекладів як VoiceTpl для вибору озвучки.
        /// </summary>
        private VoiceTpl BuildVoiceTpl(List<FrankoTranslation> translations, string imdb_id, string title, string original_title, int year, int? activeTid, int? s = null)
        {
            var tpl = new VoiceTpl();
            foreach (var tr in translations)
            {
                string link = $"{host}/lite/lme_franko?imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title ?? string.Empty)}&original_title={HttpUtility.UrlEncode(original_title ?? string.Empty)}&year={year}&t={tr.id}";
                if (s.HasValue)
                    link += $"&s={s.Value}";

                tpl.Append(tr.title, activeTid.HasValue && activeTid.Value == tr.id, link);
            }
            return tpl;
        }

        /// <summary>
        /// Відповідь зі списком перекладів. VoiceTpl не має ToJson — для rjson серіалізуємо ToObject().
        /// </summary>
        private ActionResult VoiceTplResult(VoiceTpl tpl, bool rjson)
            => rjson ? Content(JsonConvert.SerializeObject(tpl.ToObject()), "application/json; charset=utf-8")
                     : Content(tpl.ToHtml(), "text/html; charset=utf-8");

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
            => StreamHelper.BuildStreamUrl(init, streamLink, ModInit.ApnHostProvided, (s, l) => HostStreamProxy(s, l));

        private static void OnLog(string message)
        {
            System.Console.WriteLine(message);
        }
    }
}
