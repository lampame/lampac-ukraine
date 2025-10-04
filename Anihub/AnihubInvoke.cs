using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models;
using System.Text.Json;
using System.Linq;
using Anihub.Models;
using Shared.Engine;
using System.Net;
using System.Web;
using Microsoft.Extensions.Caching.Memory;
using Shared.Models.Templates;
using System.Net.Http;
using System.Text;

namespace Anihub
{
    public class AnihubInvoke
    {
        private OnlinesSettings _init;
        private HybridCache _hybridCache;
        private Action<string> _onLog;
        private ProxyManager _proxyManager;

        public AnihubInvoke(OnlinesSettings init, HybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager)
        {
            _init = init;
            _hybridCache = hybridCache;
            _onLog = onLog;
            _proxyManager = proxyManager;
        }

        public async ValueTask<AnihubSearchResponse?> Search(string title, string original_title, string year, string t)
        {
            var headers = HeadersModel.Init(
                ("Referer", _init.host)
            );

            string searchQuery = string.IsNullOrEmpty(title) ? original_title : title;
            string searchUrl = $"{_init.apihost}/anime/?search={HttpUtility.UrlEncode(searchQuery)}";

            string response = await Http.Get(searchUrl, headers: headers, proxy: _proxyManager.Get());

            if (string.IsNullOrEmpty(response))
            {
                _onLog?.Invoke($"Anihub search failed for: {searchQuery}");
                return null;
            }

            try
            {
                var searchResponse = JsonSerializer.Deserialize<AnihubSearchResponse>(response);
                _onLog?.Invoke($"Anihub search: {searchQuery} -> {searchResponse?.Count ?? 0} results");
                return searchResponse;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Anihub search parse error: {searchQuery} - {ex.Message}");
                return null;
            }
        }

        public async ValueTask<AnihubEpisodeSourcesResponse?> Embed(string animeId, string? searchUri = null)
        {
            if (!int.TryParse(animeId, out int parsedAnimeId))
            {
                _onLog?.Invoke($"Anihub embed: invalid animeId {animeId}");
                return null;
            }

            var headers = HeadersModel.Init(
                ("Referer", _init.host)
            );

            string sourcesUrl = $"{_init.apihost}/episode-sources/{parsedAnimeId}";
            string response = await Http.Get(sourcesUrl, headers: headers, proxy: _proxyManager.Get());

            if (string.IsNullOrEmpty(response))
            {
                _onLog?.Invoke($"Anihub sources failed for animeId: {parsedAnimeId}");
                return null;
            }

            try
            {
                var sourcesResponse = JsonSerializer.Deserialize<AnihubEpisodeSourcesResponse>(response);
                int moonCount = sourcesResponse?.Moonanime?.Count ?? 0;
                int ashdiCount = sourcesResponse?.Ashdi?.Count ?? 0;
                _onLog?.Invoke($"Anihub sources: animeId {parsedAnimeId} -> {moonCount} Moon, {ashdiCount} Ashdi");
                return sourcesResponse;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Anihub sources parse error: animeId {parsedAnimeId} - {ex.Message}");
                return null;
            }
        }

        public async Task<string?> Auth()
        {
            // Видаляємо цей метод, оскільки OnlinesSettings не має login/passwd полів
            // або можна додати перевірку на token з _init
            return null;
        }

        public async Task<List<MovieTpl>> GetMovies(AnihubResult anime)
        {
            var movies = new List<MovieTpl>();

            try
            {
                var movie = new MovieTpl(anime.TitleUkrainian, anime.TitleEnglish, 1);
                movies.Add(movie);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Anihub GetMovies error: {ex.Message}");
            }

            return movies;
        }

        public async Task<List<VoiceTpl>> GetVoices(AnihubEpisodeSourcesResponse sources)
        {
            var voices = new List<VoiceTpl>();

            try
            {
                var voice_tpl = new VoiceTpl();

                // Add Moonanime sources
                foreach (var source in sources.Moonanime)
                {
                    string voiceName = $"[Moon] {source.StudioName}";
                    string voiceToken = $"moonanime:{source.Id}";
                    voice_tpl.Append(voiceName, false, voiceToken);
                }

                // Add Ashdi sources
                foreach (var source in sources.Ashdi)
                {
                    string voiceName = $"[Ashdi] {source.StudioName}";
                    string voiceToken = $"ashdi:{source.Id}";
                    voice_tpl.Append(voiceName, false, voiceToken);
                }

                voices.Add(voice_tpl);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Anihub GetVoices error: {ex.Message}");
            }

            return voices;
        }

        public async Task<List<SeasonTpl>> GetSeasons(AnihubEpisodeSourcesResponse sources)
        {
            var seasons = new List<SeasonTpl>();

            try
            {
                var seasonNumbers = new HashSet<int>();

                // Collect unique season numbers from Moonanime sources
                foreach (var source in sources.Moonanime)
                {
                    seasonNumbers.Add(source.SeasonNumber);
                }

                // Collect unique season numbers from Ashdi sources
                foreach (var source in sources.Ashdi)
                {
                    seasonNumbers.Add(source.SeasonNumber);
                }

                // Create season templates
                var season_tpl = new SeasonTpl();
                foreach (var seasonNum in seasonNumbers.OrderBy(x => x))
                {
                    string seasonName = $"Сезон {seasonNum}";
                    string seasonToken = seasonNum.ToString();
                    season_tpl.Append(seasonName, seasonToken, seasonToken);
                }

                seasons.Add(season_tpl);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Anihub GetSeasons error: {ex.Message}");
            }

            return seasons;
        }

        public async Task<List<EpisodeTpl>> GetEpisodes(AnihubEpisodeSourcesResponse sources, int seasonId)
        {
            var episodes = new List<EpisodeTpl>();

            try
            {
                var episode_tpl = new EpisodeTpl();

                // Get episodes from Moonanime sources
                var moonanimeSource = sources.Moonanime.FirstOrDefault(s => s.SeasonNumber == seasonId);
                if (moonanimeSource != null)
                {
                    foreach (var ep in moonanimeSource.Episodes.OrderBy(e => e.EpisodeNumber))
                    {
                        string episodeName = ep.Title ?? $"Епізод {ep.EpisodeNumber}";
                        string seasonStr = seasonId.ToString();
                        string episodeStr = ep.EpisodeNumber.ToString();
                        string link = $"{_init.host}/embed/{ep.Id}";
                        episode_tpl.Append(episodeName, "Anihub", seasonStr, episodeStr, link, "call");
                    }
                }

                // Get episodes from Ashdi sources
                var ashdiSource = sources.Ashdi.FirstOrDefault(s => s.SeasonNumber == seasonId);
                if (ashdiSource != null)
                {
                    foreach (var ep in ashdiSource.EpisodesData.OrderBy(e => e.EpisodeNumber))
                    {
                        string episodeName = ep.Title ?? $"Епізод {ep.EpisodeNumber} (Ashdi)";
                        string seasonStr = seasonId.ToString();
                        string episodeStr = ep.EpisodeNumber.ToString();
                        string link = ep.Url ?? $"{_init.host}/embed/{ep.Id}";
                        episode_tpl.Append(episodeName, "Anihub", seasonStr, episodeStr, link, "call");
                    }
                }

                episodes.Add(episode_tpl);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Anihub GetEpisodes error: {ex.Message}");
            }

            return episodes;
        }
    }
}
