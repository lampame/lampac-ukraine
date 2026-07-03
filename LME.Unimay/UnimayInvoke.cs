using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models;
using System.Linq;
using LME.Unimay.Models;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LME.Unimay
{
    public class UnimayInvoke
    {
        private OnlinesSettings _init;
        private ProxyManager _proxyManager;
        private IHybridCache _hybridCache;
        private Action<string> _onLog;
        private readonly HttpHydra _httpHydra;

        public UnimayInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
            _httpHydra = httpHydra;
        }

        public async Task<SearchResponse> Search(string title, string original_title, int serial)
        {
            string memKey = $"lme_unimay:search:{title}:{original_title}:{serial}";
            if (_hybridCache.TryGetValue(memKey, out SearchResponse searchResults))
                return searchResults;

            try
            {
                string searchQuery = System.Web.HttpUtility.UrlEncode(title ?? original_title ?? "");
                string searchUrl = $"{_init.host}/release/search?page=0&page_size=10&title={searchQuery}";

                var headers = httpHeaders(_init);
                string json = await HttpHelper.GetAsync(_httpHydra, _init, searchUrl, headers, _proxyManager);
                SearchResponse root = json != null ? JsonSerializer.Deserialize<SearchResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;

                if (root == null || root.Content == null || root.Content.Count == 0)
                {
                    // Refresh proxy on failure
                    _proxyManager.Refresh();
                    return null;
                }

                _hybridCache.Set(memKey, root, CacheHelper.CacheTime(30, init: _init));
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
            string memKey = $"lme_unimay:release:{code}";
            if (_hybridCache.TryGetValue(memKey, out ReleaseResponse releaseDetail))
                return releaseDetail;

            try
            {
                string releaseUrl = $"{_init.host}/release?code={code}";

                var headers = httpHeaders(_init);
                string json = await HttpHelper.GetAsync(_httpHydra, _init, releaseUrl, headers, _proxyManager);
                ReleaseResponse root = json != null ? JsonSerializer.Deserialize<ReleaseResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;

                if (root == null)
                {
                    // Refresh proxy on failure
                    _proxyManager.Refresh();
                    return null;
                }

                _hybridCache.Set(memKey, root, CacheHelper.CacheTime(60, init: _init));
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
                string releaseUrl = $"{host}/lite/lme_unimay?code={item.Code}&title={System.Web.HttpUtility.UrlEncode(itemTitle)}&original_title={System.Web.HttpUtility.UrlEncode(original_title ?? "")}&serial={serial}";
                results.Add((itemTitle, item.Year, item.Type, releaseUrl));
            }

            return results;
        }

        public (string title, string link) GetMovieResult(string host, ReleaseResponse releaseDetail, string title, string original_title)
        {
            if (releaseDetail.Playlist == null || releaseDetail.Playlist.Count == 0)
                return (null, null);

            var movieEpisode = releaseDetail.Playlist[0];
            string movieLink = $"{host}/lite/lme_unimay?code={releaseDetail.Code}&title={System.Web.HttpUtility.UrlEncode(title)}&original_title={System.Web.HttpUtility.UrlEncode(original_title ?? "")}&serial=0&play=true";
            string movieTitle = movieEpisode.Title ?? title;

            return (movieTitle, movieLink);
        }

        public (string seasonName, string seasonUrl, string seasonId) GetSeasonInfo(string host, string code, string title, string original_title)
        {
            string seasonUrl = $"{host}/lite/lme_unimay?code={code}&title={System.Web.HttpUtility.UrlEncode(title)}&original_title={System.Web.HttpUtility.UrlEncode(original_title ?? "")}&serial=1&s=1";
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
                string epLink = $"{host}/lite/lme_unimay?code={releaseDetail.Code}&title={System.Web.HttpUtility.UrlEncode(title)}&original_title={System.Web.HttpUtility.UrlEncode(original_title ?? "")}&serial=1&s=1&e={ep.Number}&play=true";
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

    }
}
