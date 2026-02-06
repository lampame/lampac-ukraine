using Shared.Engine;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Web;
using Newtonsoft.Json.Linq;
using Shared.Models.Templates;
using Shared.Models.Online.Settings;
using Shared;

namespace Unimay.Controllers
{
    public class Controller : BaseOnlineController
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.Unimay);
        }

        [HttpGet]
        [Route("unimay")]
        async public ValueTask<ActionResult> Index(string title, string original_title, string code, int serial = -1, int s = -1, int e = -1, bool play = false, bool rjson = false, bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.Unimay);
            if (await IsBadInitialization(init, rch: false))
                return badInitMsg;

            var invoke = new UnimayInvoke(init, hybridCache, OnLog, proxyManager);

            if (checksearch)
            {
                if (AppInit.conf?.online?.checkOnlineSearch != true)
                    return OnError("unimay");

                var searchResults = await invoke.Search(title, original_title, serial);
                if (searchResults?.Content != null && searchResults.Content.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("unimay");
            }

            if (!string.IsNullOrEmpty(code))
            {
                // Fetch release details
                return await Release(invoke, init, code, title, original_title, serial, s, e, play, rjson);
            }
            else
            {
                // Search
                return await Search(invoke, init, title, original_title, serial, rjson);
            }
        }

        async ValueTask<ActionResult> Search(UnimayInvoke invoke, OnlinesSettings init, string title, string original_title, int serial, bool rjson)
        {
            string memKey = $"unimay:search:{title}:{original_title}:{serial}";

            return await InvkSemaphore(memKey, async () =>
            {
                var searchResults = await invoke.Search(title, original_title, serial);
                if (searchResults == null || searchResults.Content.Count == 0)
                    return OnError("no results");

                var stpl = new SimilarTpl(searchResults.Content.Count);
                var results = invoke.GetSearchResults(host, searchResults, title, original_title, serial);

                foreach (var (itemTitle, itemYear, itemType, releaseUrl) in results)
                {
                    stpl.Append(itemTitle, itemYear, itemType, releaseUrl);
                }

                return ContentTo(rjson ? stpl.ToJson() : stpl.ToHtml());
            });
        }

        async ValueTask<ActionResult> Release(UnimayInvoke invoke, OnlinesSettings init, string code, string title, string original_title, int serial, int s, int e, bool play, bool rjson)
        {
            string memKey = $"unimay:release:{code}";

            return await InvkSemaphore(memKey, async () =>
            {
                var releaseDetail = await invoke.Release(code);
                if (releaseDetail == null)
                    return OnError("no release detail");

                string itemType = releaseDetail.Type;
                var playlist = releaseDetail.Playlist;

                if (playlist == null || playlist.Count == 0)
                    return OnError("no playlist");

                if (play)
                {
                    // Get specific episode
                    Unimay.Models.Episode episode = null;
                    if (itemType == "Телесеріал")
                    {
                        if (s <= 0 || e <= 0) return OnError("invalid episode");
                        episode = playlist.FirstOrDefault(ep => ep.Number == e);
                    }
                    else // Movie
                    {
                        episode = playlist[0];
                    }

                    if (episode == null)
                        return OnError("episode not found");

                    string masterUrl = invoke.GetStreamUrl(episode);
                    if (string.IsNullOrEmpty(masterUrl))
                        return OnError("no stream");

                    string cleaned = StripLampacArgs(masterUrl?.Trim());
                    return UpdateService.Validate(Redirect(HostStreamProxy(init, cleaned, proxy: proxyManager.Get())));
                }

                if (itemType == "Фільм")
                {
                    var (movieTitle, movieLink) = invoke.GetMovieResult(host, releaseDetail, title, original_title);
                    var mtpl = new MovieTpl(title, original_title, 1);
                    mtpl.Append(movieTitle, accsArgs(movieLink), method: "play");
                    return ContentTo(rjson ? mtpl.ToJson() : mtpl.ToHtml());
                }
                else if (itemType == "Телесеріал")
                {
                    if (s == -1)
                    {
                        // Assume single season
                        var (seasonName, seasonUrl, seasonId) = invoke.GetSeasonInfo(host, code, title, original_title);
                        var stpl = new SeasonTpl();
                        stpl.Append(seasonName, seasonUrl, seasonId);
                        return ContentTo(rjson ? stpl.ToJson() : stpl.ToHtml());
                    }
                    else
                    {
                        // Episodes for season 1
                        var episodes = invoke.GetEpisodesForSeason(host, releaseDetail, title, original_title);
                        var mtpl = new MovieTpl(title, original_title, episodes.Count);
                        foreach (var (epTitle, epLink) in episodes)
                        {
                            mtpl.Append(epTitle, accsArgs(epLink), method: "play");
                        }
                        return ContentTo(rjson ? mtpl.ToJson() : mtpl.ToHtml());
                    }
                }

                return OnError("unsupported type");
            });
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
    }
}
