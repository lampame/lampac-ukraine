using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using KlonFUN.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace KlonFUN.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.KlonFUN);
        }

        [HttpGet]
        [Route("klonfun")]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.KlonFUN);
            if (!init.enable)
                return Forbid();

            var invoke = new KlonFUNInvoke(init, hybridCache, OnLog, proxyManager);

            if (checksearch)
            {
                if (AppInit.conf?.online?.checkOnlineSearch != true)
                    return OnError("klonfun", proxyManager);

                var checkResults = await invoke.Search(imdb_id, title, original_title);
                if (checkResults != null && checkResults.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("klonfun", proxyManager);
            }

            string itemUrl = href;
            if (string.IsNullOrWhiteSpace(itemUrl))
            {
                var searchResults = await invoke.Search(imdb_id, title, original_title);
                if (searchResults == null || searchResults.Count == 0)
                    return OnError("klonfun", proxyManager);

                if (searchResults.Count > 1)
                {
                    var similarTpl = new SimilarTpl(searchResults.Count);
                    foreach (SearchResult result in searchResults)
                    {
                        string link = $"{host}/klonfun?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(result.Url)}";
                        similarTpl.Append(result.Title, result.Year > 0 ? result.Year.ToString() : string.Empty, string.Empty, link, result.Poster);
                    }

                    return rjson
                        ? Content(similarTpl.ToJson(), "application/json; charset=utf-8")
                        : Content(similarTpl.ToHtml(), "text/html; charset=utf-8");
                }

                itemUrl = searchResults[0].Url;
            }

            var item = await invoke.GetItem(itemUrl);
            if (item == null || string.IsNullOrWhiteSpace(item.PlayerUrl))
            {
                OnLog($"KlonFUN: не знайдено iframe-плеєр для {itemUrl}");
                return OnError("klonfun", proxyManager);
            }

            string contentTitle = !string.IsNullOrWhiteSpace(title) ? title : item.Title;
            if (string.IsNullOrWhiteSpace(contentTitle))
                contentTitle = "KlonFUN";

            string contentOriginalTitle = !string.IsNullOrWhiteSpace(original_title) ? original_title : contentTitle;

            bool isSerial = serial == 1 || item.IsSerialPlayer;
            if (isSerial)
            {
                var serialStructure = await invoke.GetSerialStructure(item.PlayerUrl);
                if (serialStructure == null || serialStructure.Voices.Count == 0)
                    return OnError("klonfun", proxyManager);

                if (s == -1)
                {
                    List<int> seasons;
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        var selectedVoice = serialStructure.Voices.FirstOrDefault(v => v.Key.Equals(t, StringComparison.OrdinalIgnoreCase));
                        if (selectedVoice != null)
                        {
                            seasons = selectedVoice.Seasons.Keys.OrderBy(sn => sn).ToList();
                        }
                        else
                        {
                            seasons = serialStructure.Voices
                                .SelectMany(v => v.Seasons.Keys)
                                .Distinct()
                                .OrderBy(sn => sn)
                                .ToList();
                        }
                    }
                    else
                    {
                        seasons = serialStructure.Voices
                            .SelectMany(v => v.Seasons.Keys)
                            .Distinct()
                            .OrderBy(sn => sn)
                            .ToList();
                    }

                    if (seasons.Count == 0)
                        return OnError("klonfun", proxyManager);

                    var seasonTpl = new SeasonTpl(seasons.Count);
                    foreach (int seasonNumber in seasons)
                    {
                        string link = $"{host}/klonfun?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={seasonNumber}&href={HttpUtility.UrlEncode(itemUrl)}";
                        if (!string.IsNullOrWhiteSpace(t))
                            link += $"&t={HttpUtility.UrlEncode(t)}";

                        seasonTpl.Append($"{seasonNumber}", link, seasonNumber.ToString());
                    }

                    return rjson
                        ? Content(seasonTpl.ToJson(), "application/json; charset=utf-8")
                        : Content(seasonTpl.ToHtml(), "text/html; charset=utf-8");
                }

                var voicesForSeason = serialStructure.Voices
                    .Where(v => v.Seasons.ContainsKey(s))
                    .ToList();

                if (voicesForSeason.Count == 0)
                    return OnError("klonfun", proxyManager);

                var selectedVoiceForSeason = voicesForSeason
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(t) && v.Key.Equals(t, StringComparison.OrdinalIgnoreCase))
                    ?? voicesForSeason[0];

                var voiceTpl = new VoiceTpl(voicesForSeason.Count);
                foreach (var voice in voicesForSeason)
                {
                    string voiceLink = $"{host}/klonfun?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&t={HttpUtility.UrlEncode(voice.Key)}&href={HttpUtility.UrlEncode(itemUrl)}";
                    voiceTpl.Append(voice.DisplayName, voice.Key.Equals(selectedVoiceForSeason.Key, StringComparison.OrdinalIgnoreCase), voiceLink);
                }

                if (!selectedVoiceForSeason.Seasons.TryGetValue(s, out List<SerialEpisode> episodes) || episodes.Count == 0)
                    return OnError("klonfun", proxyManager);

                var episodeTpl = new EpisodeTpl(episodes.Count);
                foreach (SerialEpisode episode in episodes.OrderBy(e => e.Number))
                {
                    string episodeTitle = !string.IsNullOrWhiteSpace(episode.Title)
                        ? episode.Title
                        : $"Серія {episode.Number}";

                    string streamUrl = BuildStreamUrl(init, episode.Link);
                    episodeTpl.Append(episodeTitle, contentTitle, s.ToString(), episode.Number.ToString("D2"), streamUrl);
                }

                episodeTpl.Append(voiceTpl);
                if (rjson)
                    return Content(episodeTpl.ToJson(), "application/json; charset=utf-8");

                return Content(episodeTpl.ToHtml(), "text/html; charset=utf-8");
            }
            else
            {
                var streams = await invoke.GetMovieStreams(item.PlayerUrl);
                if (streams == null || streams.Count == 0)
                    return OnError("klonfun", proxyManager);

                var movieTpl = new MovieTpl(contentTitle, contentOriginalTitle, streams.Count);
                for (int i = 0; i < streams.Count; i++)
                {
                    var stream = streams[i];
                    string label = !string.IsNullOrWhiteSpace(stream.Title)
                        ? stream.Title
                        : $"Варіант {i + 1}";

                    string streamUrl = BuildStreamUrl(init, stream.Link);
                    movieTpl.Append(label, streamUrl);
                }

                return rjson
                    ? Content(movieTpl.ToJson(), "application/json; charset=utf-8")
                    : Content(movieTpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        string BuildStreamUrl(OnlinesSettings init, string streamLink)
        {
            string link = StripLampacArgs(streamLink?.Trim());
            if (string.IsNullOrWhiteSpace(link))
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
            if (string.IsNullOrWhiteSpace(url))
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
    }
}
