using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Bamboo.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace Bamboo.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.Bamboo);
        }

        [HttpGet]
        [Route("lite/bamboo")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.Bamboo);
            if (!init.enable)
                return Forbid();

            var invoke = new BambooInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

            if (checksearch)
            {
                if (!IsCheckOnlineSearchEnabled())
                    return OnError("bamboo", refresh_proxy: true);

                var searchResults = await invoke.Search(title, original_title);
                if (searchResults != null && searchResults.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("bamboo", refresh_proxy: true);
            }

            string itemUrl = href;
            if (string.IsNullOrEmpty(itemUrl))
            {
                var searchResults = await invoke.Search(title, original_title);
                if (searchResults == null || searchResults.Count == 0)
                    return OnError("bamboo", refresh_proxy: true);

                if (searchResults.Count > 1)
                {
                    var similar_tpl = new SimilarTpl(searchResults.Count);
                    foreach (var res in searchResults)
                    {
                        string link = $"{host}/lite/bamboo?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(res.Url)}";
                        similar_tpl.Append(res.Title, string.Empty, string.Empty, link, res.Poster);
                    }

                    return rjson ? Content(similar_tpl.ToJson(), "application/json; charset=utf-8") : Content(similar_tpl.ToHtml(), "text/html; charset=utf-8");
                }

                itemUrl = searchResults[0].Url;
            }

            if (serial == 1)
            {
                var series = await invoke.GetSeriesEpisodes(itemUrl);
                if (series == null || (series.Sub.Count == 0 && series.Dub.Count == 0))
                    return OnError("bamboo", refresh_proxy: true);

                var voice_tpl = new VoiceTpl();
                var episode_tpl = new EpisodeTpl();

                var availableVoices = new List<(string key, string name, List<EpisodeInfo> episodes)>();
                if (series.Sub.Count > 0)
                    availableVoices.Add(("sub", "Субтитри", series.Sub));
                if (series.Dub.Count > 0)
                    availableVoices.Add(("dub", "Озвучення", series.Dub));

                if (string.IsNullOrEmpty(t))
                    t = availableVoices.First().key;

                foreach (var voice in availableVoices)
                {
                    string voiceLink = $"{host}/lite/bamboo?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&t={voice.key}&href={HttpUtility.UrlEncode(itemUrl)}";
                    voice_tpl.Append(voice.name, voice.key == t, voiceLink);
                }

                var selected = availableVoices.FirstOrDefault(v => v.key == t);
                if (selected.episodes == null || selected.episodes.Count == 0)
                    return OnError("bamboo", refresh_proxy: true);

                int index = 1;
                foreach (var ep in selected.episodes.OrderBy(e => e.Episode ?? int.MaxValue))
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
            else
            {
                var streams = await invoke.GetMovieStreams(itemUrl);
                if (streams == null || streams.Count == 0)
                    return OnError("bamboo", refresh_proxy: true);

                var movie_tpl = new MovieTpl(title, original_title);
                for (int i = 0; i < streams.Count; i++)
                {
                    var stream = streams[i];
                    string label = !string.IsNullOrEmpty(stream.Title) ? stream.Title : $"Варіант {i + 1}";
                    string streamUrl = BuildStreamUrl(init, stream.Url);
                    movie_tpl.Append(label, streamUrl);
                }

                return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
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
