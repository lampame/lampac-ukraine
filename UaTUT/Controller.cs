using Shared.Engine;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Web;
using System.Linq;
using Shared;
using Shared.Models.Templates;
using UaTUT.Models;
using System.Text.RegularExpressions;
using Shared.Models.Online.Settings;
using Shared.Models;

namespace UaTUT
{
    [Route("uatut")]
    public class UaTUTController : BaseOnlineController
    {
        ProxyManager proxyManager;

        public UaTUTController()
        {
            proxyManager = new ProxyManager(ModInit.UaTUT);
        }

        [HttpGet]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, int season = -1, bool rjson = false)
        {
            var init = await loadKit(ModInit.UaTUT);
            if (!init.enable)
                return OnError();

            OnLog($"UaTUT: {title} (serial={serial}, s={s}, season={season}, t={t})");

            var invoke = new UaTUTInvoke(init, hybridCache, OnLog, proxyManager);

            // Використовуємо кеш для пошуку, щоб уникнути дублювання запитів
            string searchCacheKey = $"uatut:search:{imdb_id ?? original_title ?? title}";
            var searchResults = await InvokeCache<List<SearchResult>>(searchCacheKey, TimeSpan.FromMinutes(10), async () =>
            {
                return await invoke.Search(original_title ?? title, imdb_id);
            });

            if (searchResults == null || !searchResults.Any())
            {
                OnLog("UaTUT: No search results found");
                return OnError();
            }

            if (serial == 1)
            {
                return await HandleSeries(searchResults, imdb_id, kinopoisk_id, title, original_title, year, s, season, t, rjson, invoke);
            }
            else
            {
                return await HandleMovie(searchResults, rjson, invoke);
            }
        }

        private async Task<ActionResult> HandleSeries(List<SearchResult> searchResults, string imdb_id, long kinopoisk_id, string title, string original_title, int year, int s, int season, string t, bool rjson, UaTUTInvoke invoke)
        {
            var init = ModInit.UaTUT;

            // Фільтруємо тільки серіали та аніме
            var seriesResults = searchResults.Where(r => r.Category == "Серіал" || r.Category == "Аніме").ToList();

            if (!seriesResults.Any())
            {
                OnLog("UaTUT: No series found in search results");
                return OnError();
            }

            if (s == -1) // Крок 1: Відображення списку серіалів
            {
                var season_tpl = new SeasonTpl();
                for (int i = 0; i < seriesResults.Count; i++)
                {
                    var series = seriesResults[i];
                    string seasonName = $"{series.Title} ({series.Year})";
                    string link = $"{host}/uatut?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={i}";
                    season_tpl.Append(seasonName, link, i.ToString());
                }

                OnLog($"UaTUT: generated {seriesResults.Count} series options");
                return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
            }
            else if (season == -1) // Крок 2: Відображення сезонів для вибраного серіалу
            {
                if (s >= seriesResults.Count)
                    return OnError();

                var selectedSeries = seriesResults[s];

                // Використовуємо кеш для уникнення повторних запитів
                string cacheKey = $"uatut:player_data:{selectedSeries.Id}";
                var playerData = await InvokeCache<PlayerData>(cacheKey, TimeSpan.FromMinutes(10), async () =>
                {
                    return await GetPlayerDataCached(selectedSeries, invoke);
                });

                if (playerData?.Voices == null || !playerData.Voices.Any())
                    return OnError();

                // Використовуємо першу озвучку для отримання списку сезонів
                var firstVoice = playerData.Voices.First();

                var season_tpl = new SeasonTpl();
                for (int i = 0; i < firstVoice.Seasons.Count; i++)
                {
                    var seasonItem = firstVoice.Seasons[i];
                    string seasonName = seasonItem.Title ?? $"Сезон {i + 1}";
                    string link = $"{host}/uatut?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&season={i}";
                    season_tpl.Append(seasonName, link, i.ToString());
                }

                OnLog($"UaTUT: found {firstVoice.Seasons.Count} seasons");
                return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
            }
            else // Крок 3: Відображення озвучок та епізодів для вибраного сезону
            {
                if (s >= seriesResults.Count)
                    return OnError();

                var selectedSeries = seriesResults[s];

                // Використовуємо той самий кеш
                string cacheKey = $"uatut:player_data:{selectedSeries.Id}";
                var playerData = await InvokeCache<PlayerData>(cacheKey, TimeSpan.FromMinutes(10), async () =>
                {
                    return await GetPlayerDataCached(selectedSeries, invoke);
                });

                if (playerData?.Voices == null || !playerData.Voices.Any())
                    return OnError();

                // Перевіряємо чи існує вибраний сезон
                if (season >= playerData.Voices.First().Seasons.Count)
                    return OnError();

                var voice_tpl = new VoiceTpl();
                var episode_tpl = new EpisodeTpl();

                // Автоматично вибираємо першу озвучку якщо не вибрана
                string selectedVoice = t;
                if (string.IsNullOrEmpty(selectedVoice) && playerData.Voices.Any())
                {
                    selectedVoice = "0"; // Перша озвучка
                }

                // Додаємо всі озвучки
                for (int i = 0; i < playerData.Voices.Count; i++)
                {
                    var voice = playerData.Voices[i];
                    string voiceName = voice.Name ?? $"Озвучка {i + 1}";
                    string voiceLink = $"{host}/uatut?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&season={season}&t={i}";
                    bool isActive = selectedVoice == i.ToString();
                    voice_tpl.Append(voiceName, isActive, voiceLink);
                }

                // Додаємо епізоди тільки для вибраного сезону та озвучки
                if (!string.IsNullOrEmpty(selectedVoice) && int.TryParse(selectedVoice, out int voiceIndex) && voiceIndex < playerData.Voices.Count)
                {
                    var selectedVoiceData = playerData.Voices[voiceIndex];

                    if (season < selectedVoiceData.Seasons.Count)
                    {
                        var selectedSeason = selectedVoiceData.Seasons[season];

                        // Сортуємо епізоди та додаємо правильну нумерацію
                        var sortedEpisodes = selectedSeason.Episodes.OrderBy(e => ExtractEpisodeNumber(e.Title)).ToList();

                        for (int i = 0; i < sortedEpisodes.Count; i++)
                        {
                            var episode = sortedEpisodes[i];
                            string episodeName = episode.Title;
                            string episodeFile = episode.File;

                            if (!string.IsNullOrEmpty(episodeFile))
                            {
                                // Створюємо прямий лінк на епізод через play action
                                string episodeLink = $"{host}/uatut/play?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&s={s}&season={season}&t={selectedVoice}&episodeId={episode.Id}";

                                // Використовуємо правильний синтаксис EpisodeTpl.Append без poster параметра
                                episode_tpl.Append(episodeName, title ?? original_title, season.ToString(), (i + 1).ToString("D2"), episodeLink, "call");
                            }
                        }
                    }
                }

                int voiceCount = playerData.Voices.Count;
                int episodeCount = 0;
                if (!string.IsNullOrEmpty(selectedVoice) && int.TryParse(selectedVoice, out int vIndex) && vIndex < playerData.Voices.Count)
                {
                    var selectedVoiceData = playerData.Voices[vIndex];
                    if (season < selectedVoiceData.Seasons.Count)
                    {
                        episodeCount = selectedVoiceData.Seasons[season].Episodes.Count;
                    }
                }

                OnLog($"UaTUT: generated {voiceCount} voices, {episodeCount} episodes");

                if (rjson)
                    return Content(episode_tpl.ToJson(voice_tpl), "application/json; charset=utf-8");

                return Content(voice_tpl.ToHtml() + episode_tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        // Допоміжний метод для кешованого отримання даних плеєра
        private async Task<PlayerData> GetPlayerDataCached(SearchResult selectedSeries, UaTUTInvoke invoke)
        {
            var pageContent = await invoke.GetMoviePageContent(selectedSeries.Id);
            if (string.IsNullOrEmpty(pageContent))
                return null;

            var playerUrl = await invoke.GetPlayerUrl(pageContent);
            if (string.IsNullOrEmpty(playerUrl))
                return null;

            return await invoke.GetPlayerData(playerUrl);
        }

        // Допоміжний метод для витягування номера епізоду з назви
        private int ExtractEpisodeNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return 0;

            var match = Regex.Match(title, @"(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private async Task<ActionResult> HandleMovie(List<SearchResult> searchResults, bool rjson, UaTUTInvoke invoke)
        {
            var init = ModInit.UaTUT;

            // Фільтруємо тільки фільми
            var movieResults = searchResults.Where(r => r.Category == "Фільм").ToList();

            if (!movieResults.Any())
            {
                OnLog("UaTUT: No movies found in search results");
                return OnError();
            }

            var movie_tpl = new MovieTpl(title: "UaTUT Movies", original_title: "UaTUT Movies");

            foreach (var movie in movieResults)
            {
                var pageContent = await invoke.GetMoviePageContent(movie.Id);
                if (string.IsNullOrEmpty(pageContent))
                    continue;

                var playerUrl = await invoke.GetPlayerUrl(pageContent);
                if (string.IsNullOrEmpty(playerUrl))
                    continue;

                var playerData = await invoke.GetPlayerData(playerUrl);
                if (playerData?.File == null)
                    continue;

                string movieName = $"{movie.Title} ({movie.Year})";
                string movieLink = $"{host}/uatut/play/movie?imdb_id={movie.Id}&title={HttpUtility.UrlEncode(movie.Title)}&year={movie.Year}";
                movie_tpl.Append(movieName, movieLink, "call");
            }

            if (movie_tpl.IsEmpty())
            {
                OnLog("UaTUT: No playable movies found");
                return OnError();
            }

            OnLog($"UaTUT: found {movieResults.Count} movies");
            return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        [HttpGet]
        [Route("play/movie")]
        async public Task<ActionResult> PlayMovie(long imdb_id, string title, int year, bool play = false, bool rjson = false)
        {
            var init = await loadKit(ModInit.UaTUT);
            if (!init.enable)
                return OnError();

            OnLog($"UaTUT PlayMovie: {title} ({year}) play={play}");

            var invoke = new UaTUTInvoke(init, hybridCache, OnLog, proxyManager);

            // Використовуємо кеш для пошуку
            string searchCacheKey = $"uatut:search:{title}";
            var searchResults = await InvokeCache<List<SearchResult>>(searchCacheKey, TimeSpan.FromMinutes(10), async () =>
            {
                return await invoke.Search(title, null);
            });

            if (searchResults == null || !searchResults.Any())
            {
                OnLog("UaTUT PlayMovie: No search results found");
                return OnError();
            }

            // Шукаємо фільм за ID
            var movie = searchResults.FirstOrDefault(r => r.Id == imdb_id.ToString() && r.Category == "Фільм");
            if (movie == null)
            {
                OnLog("UaTUT PlayMovie: Movie not found");
                return OnError();
            }

            var pageContent = await invoke.GetMoviePageContent(movie.Id);
            if (string.IsNullOrEmpty(pageContent))
                return OnError();

            var playerUrl = await invoke.GetPlayerUrl(pageContent);
            if (string.IsNullOrEmpty(playerUrl))
                return OnError();

            var playerData = await invoke.GetPlayerData(playerUrl);
            if (playerData?.File == null)
                return OnError();

            OnLog($"UaTUT PlayMovie: Found direct file: {playerData.File}");

            string streamUrl = HostStreamProxy(init, playerData.File);

            // Якщо play=true, робимо Redirect, інакше повертаємо JSON
            if (play)
                return Redirect(streamUrl);
            else
                return Content(VideoTpl.ToJson("play", streamUrl, title), "application/json; charset=utf-8");
        }

        [HttpGet]
        [Route("play")]
        async public Task<ActionResult> Play(long id, string imdb_id, long kinopoisk_id, string title, string original_title, int year, int s, int season, string t, string episodeId, bool play = false, bool rjson = false)
        {
            var init = await loadKit(ModInit.UaTUT);
            if (!init.enable)
                return OnError();

            OnLog($"UaTUT Play: {title} (s={s}, season={season}, t={t}, episodeId={episodeId}) play={play}");

            var invoke = new UaTUTInvoke(init, hybridCache, OnLog, proxyManager);

            // Використовуємо кеш для пошуку
            string searchCacheKey = $"uatut:search:{imdb_id ?? original_title ?? title}";
            var searchResults = await InvokeCache<List<SearchResult>>(searchCacheKey, TimeSpan.FromMinutes(10), async () =>
            {
                return await invoke.Search(original_title ?? title, imdb_id);
            });

            if (searchResults == null || !searchResults.Any())
            {
                OnLog("UaTUT Play: No search results found");
                return OnError();
            }

            // Фільтруємо тільки серіали та аніме
            var seriesResults = searchResults.Where(r => r.Category == "Серіал" || r.Category == "Аніме").ToList();

            if (!seriesResults.Any() || s >= seriesResults.Count)
            {
                OnLog("UaTUT Play: No series found or invalid series index");
                return OnError();
            }

            var selectedSeries = seriesResults[s];

            // Використовуємо той самий кеш як і в HandleSeries
            string cacheKey = $"uatut:player_data:{selectedSeries.Id}";
            var playerData = await InvokeCache<PlayerData>(cacheKey, TimeSpan.FromMinutes(10), async () =>
            {
                return await GetPlayerDataCached(selectedSeries, invoke);
            });

            if (playerData?.Voices == null || !playerData.Voices.Any())
            {
                OnLog("UaTUT Play: No player data or voices found");
                return OnError();
            }

            // Знаходимо потрібний епізод в конкретному сезоні та озвучці
            if (int.TryParse(t, out int voiceIndex) && voiceIndex < playerData.Voices.Count)
            {
                var selectedVoice = playerData.Voices[voiceIndex];

                if (season >= 0 && season < selectedVoice.Seasons.Count)
                {
                    var selectedSeasonData = selectedVoice.Seasons[season];

                    foreach (var episode in selectedSeasonData.Episodes)
                    {
                        if (episode.Id == episodeId && !string.IsNullOrEmpty(episode.File))
                        {
                            OnLog($"UaTUT Play: Found episode {episode.Title}, stream: {episode.File}");

                            string streamUrl = HostStreamProxy(init, episode.File);
                            string episodeTitle = $"{title ?? original_title} - {episode.Title}";

                            // Якщо play=true, робимо Redirect, інакше повертаємо JSON
                            if (play)
                                return Redirect(streamUrl);
                            else
                                return Content(VideoTpl.ToJson("play", streamUrl, episodeTitle), "application/json; charset=utf-8");
                        }
                    }
                }
                else
                {
                    OnLog($"UaTUT Play: Invalid season {season}, available seasons: {selectedVoice.Seasons.Count}");
                }
            }
            else
            {
                OnLog($"UaTUT Play: Invalid voice index {t}, available voices: {playerData.Voices.Count}");
            }

            OnLog("UaTUT Play: Episode not found");
            return OnError();
        }
    }
}
