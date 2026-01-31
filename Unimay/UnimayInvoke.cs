using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models;
using System.Linq;
using Unimay.Models;
using Shared.Engine;
using System.Net;
using System.Text;

namespace Unimay
{
    public class UnimayInvoke
    {
        private OnlinesSettings _init;
        private ProxyManager _proxyManager;
        private IHybridCache _hybridCache;
        private Action<string> _onLog;

        public UnimayInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async Task<SearchResponse> Search(string title, string original_title, int serial)
        {
            string memKey = $"unimay:search:{title}:{original_title}:{serial}";
            if (_hybridCache.TryGetValue(memKey, out SearchResponse searchResults))
                return searchResults;

            try
            {
                string searchQuery = System.Web.HttpUtility.UrlEncode(title ?? original_title ?? "");
                string searchUrl = $"{_init.host}/release/search?page=0&page_size=10&title={searchQuery}";

                var headers = httpHeaders(_init);
                SearchResponse root = await Http.Get<SearchResponse>(searchUrl, timeoutSeconds: 8, proxy: _proxyManager.Get(), headers: headers);

                if (root == null || root.Content == null || root.Content.Count == 0)
                {
                    // Refresh proxy on failure
                    _proxyManager.Refresh();
                    return null;
                }

                _hybridCache.Set(memKey, root, cacheTime(30, init: _init));
                return root;
            }
            catch (Exception ex)
            {
                _onLog($"Unimay search error: {ex.Message}");
                return null;
            }
        }

        public async Task<ReleaseResponse> Release(string code)
        {
            string memKey = $"unimay:release:{code}";
            if (_hybridCache.TryGetValue(memKey, out ReleaseResponse releaseDetail))
                return releaseDetail;

            try
            {
                string releaseUrl = $"{_init.host}/release?code={code}";

                var headers = httpHeaders(_init);
                ReleaseResponse root = await Http.Get<ReleaseResponse>(releaseUrl, timeoutSeconds: 8, proxy: _proxyManager.Get(), headers: headers);

                if (root == null)
                {
                    // Refresh proxy on failure
                    _proxyManager.Refresh();
                    return null;
                }

                _hybridCache.Set(memKey, root, cacheTime(60, init: _init));
                return root;
            }
            catch (Exception ex)
            {
                _onLog($"Unimay release error: {ex.Message}");
                return null;
            }
        }

        public List<(string title, string year, string type, string url)> GetSearchResults(string host, SearchResponse searchResults, string title, string original_title, int serial)
        {
            var results = new List<(string title, string year, string type, string url)>();

            foreach (var item in searchResults.Content)
            {
                // Filter by serial if specified (0: movie "Фільм", 1: serial "Телесеріал")
                if (serial != -1)
                {
                    bool isMovie = item.Type == "Фільм";
                    if ((serial == 0 && !isMovie) || (serial == 1 && isMovie))
                        continue;
                }

                string itemTitle = item.Names?.Ukr ?? item.Names?.Eng ?? item.Title;
                string releaseUrl = $"{host}/unimay?code={item.Code}&title={System.Web.HttpUtility.UrlEncode(itemTitle)}&original_title={System.Web.HttpUtility.UrlEncode(original_title ?? "")}&serial={serial}";
                results.Add((itemTitle, item.Year, item.Type, releaseUrl));
            }

            return results;
        }

        public (string title, string link) GetMovieResult(string host, ReleaseResponse releaseDetail, string title, string original_title)
        {
            if (releaseDetail.Playlist == null || releaseDetail.Playlist.Count == 0)
                return (null, null);

            var movieEpisode = releaseDetail.Playlist[0];
            string movieLink = $"{host}/unimay?code={releaseDetail.Code}&title={System.Web.HttpUtility.UrlEncode(title)}&original_title={System.Web.HttpUtility.UrlEncode(original_title ?? "")}&serial=0&play=true";
            string movieTitle = movieEpisode.Title ?? title;

            return (movieTitle, movieLink);
        }

        public (string seasonName, string seasonUrl, string seasonId) GetSeasonInfo(string host, string code, string title, string original_title)
        {
            string seasonUrl = $"{host}/unimay?code={code}&title={System.Web.HttpUtility.UrlEncode(title)}&original_title={System.Web.HttpUtility.UrlEncode(original_title ?? "")}&serial=1&s=1";
            return ("Сезон 1", seasonUrl, "1");
        }

        public List<(string episodeTitle, string episodeUrl)> GetEpisodesForSeason(string host, ReleaseResponse releaseDetail, string title, string original_title)
        {
            var episodes = new List<(string episodeTitle, string episodeUrl)>();

            if (releaseDetail.Playlist == null)
                return episodes;

            foreach (var ep in releaseDetail.Playlist.Where(ep => ep.Number >= 1 && ep.Number <= 24).OrderBy(ep => ep.Number))
            {
                string epTitle = ep.Title ?? $"Епізод {ep.Number}";
                string epLink = $"{host}/unimay?code={releaseDetail.Code}&title={System.Web.HttpUtility.UrlEncode(title)}&original_title={System.Web.HttpUtility.UrlEncode(original_title ?? "")}&serial=1&s=1&e={ep.Number}&play=true";
                episodes.Add((epTitle, epLink));
            }

            return episodes;
        }

        public string GetStreamUrl(Episode episode)
        {
            return episode.Hls?.Master;
        }

        private List<HeadersModel> httpHeaders(OnlinesSettings init)
        {
            return new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", "Mozilla/5.0"),
                new HeadersModel("Referer", init.host),
                new HeadersModel("Accept", "application/json")
            };
        }

        public static TimeSpan cacheTime(int multiaccess, int home = 5, int mikrotik = 2, OnlinesSettings init = null, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);

            int ctime = AppInit.conf.mikrotik ? mikrotik : AppInit.conf.multiaccess ? init != null && init.cache_time > 0 ? init.cache_time : multiaccess : home;
            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }
    }
}
