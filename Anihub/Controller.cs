using Shared.Engine;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Web;
using System.Linq;
using Shared;
using Shared.Models.Templates;
using Anihub.Models;
using System.Text.RegularExpressions;
using Shared.Models.Online.Settings;
using Shared.Models;
using System.Net;
using System.Net.Http;

namespace Anihub
{
    [Route("anihub")]
    public class AnihubController : BaseOnlineController
    {
        ProxyManager proxyManager;

        public AnihubController()
        {
            proxyManager = new ProxyManager(ModInit.Anihub);
        }

        [HttpGet]
        async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false)
        {
            var init = await loadKit(ModInit.Anihub);
            if (!init.enable)
                return OnError();

            OnLog($"Anihub: {title} (serial={serial}, s={s}, t={t})");

            var invoke = new AnihubInvoke(init, hybridCache, OnLog, proxyManager);

            var searchResponse = await invoke.Search(title, original_title, "0", year.ToString());
            if (searchResponse == null || searchResponse.IsEmpty)
                return OnError();

            if (serial == 1)
            {
                if (s == -1) // Відображення списку аніме як "сезонів"
                {
                    var season_tpl = new SeasonTpl();
                    for (int i = 0; i < searchResponse.content.Count; i++)
                    {
                        var anime = searchResponse.content[i];
                        string seasonName = anime.TitleUkrainian ?? anime.TitleEnglish ?? anime.TitleOriginal;
                        string link = $"{host}/anihub?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={i}";
                        season_tpl.Append(seasonName, link, i.ToString());
                    }

                    OnLog($"Anihub: generated {searchResponse.content.Count} seasons");
                    return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
                }
                else // Відображення озвучок та епізодів для вибраного аніме
                {
                    if (s >= searchResponse.content.Count)
                        return OnError();

                    var selectedAnime = searchResponse.content[s];
                    var episodesResponse = await invoke.Embed(selectedAnime.Id.ToString());
                    if (episodesResponse == null)
                        return OnError();

                    var voice_tpl = new VoiceTpl();
                    var episode_tpl = new EpisodeTpl();

                    // Автоматично вибираємо першу озвучку якщо не вибрана
                    string selectedVoice = t;
                    if (string.IsNullOrEmpty(selectedVoice))
                    {
                        if (episodesResponse.Moonanime.Count > 0)
                            selectedVoice = $"moon_{episodesResponse.Moonanime.First().Id}";
                        else if (episodesResponse.Ashdi.Count > 0)
                            selectedVoice = $"ashdi_{episodesResponse.Ashdi.First().Id}";
                    }

                    // Додаємо озвучки з Moonanime
                    foreach (var moonSource in episodesResponse.Moonanime)
                    {
                        string voiceName = $"[Moon] {moonSource.StudioName}";
                        string voiceLink = $"{host}/anihub?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&t=moon_{moonSource.Id}";
                        bool isActive = selectedVoice != null && selectedVoice.StartsWith("moon_") && selectedVoice.Split('_')[1] == moonSource.Id.ToString();
                        voice_tpl.Append(voiceName, isActive, voiceLink);
                    }

                    // Додаємо озвучки з Ashdi
                    foreach (var ashdiSource in episodesResponse.Ashdi)
                    {
                        string voiceName = $"[Ashdi] {ashdiSource.StudioName}";
                        string voiceLink = $"{host}/anihub?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={s}&t=ashdi_{ashdiSource.Id}";
                        bool isActive = selectedVoice != null && selectedVoice.StartsWith("ashdi_") && selectedVoice.Split('_')[1] == ashdiSource.Id.ToString();
                        voice_tpl.Append(voiceName, isActive, voiceLink);
                    }

                    // Завжди додаємо епізоди для вибраної озвучки (або автоматично вибраної)
                    if (!string.IsNullOrEmpty(selectedVoice))
                    {
                        if (selectedVoice.StartsWith("moon_"))
                        {
                            var parts = selectedVoice.Split('_');
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int sourceId))
                            {
                                var selectedSource = episodesResponse.Moonanime.FirstOrDefault(m => m.Id == sourceId);
                                if (selectedSource != null)
                                {
                                    foreach (var episode in selectedSource.Episodes.OrderBy(e => e.EpisodeNumber))
                                    {
                                        string episodeName = !string.IsNullOrEmpty(episode.Title) ? episode.Title : $"Епізод {episode.EpisodeNumber}";
                                        string episodeLink = $"{host}/anihub/play?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&s={s}&e={episode.EpisodeNumber}&t={selectedVoice}_{episode.Id}";
                                        episode_tpl.Append(episodeName, title ?? original_title, s.ToString(), episode.EpisodeNumber.ToString("D2"), episodeLink, "call");
                                    }
                                }
                            }
                        }
                        else if (selectedVoice.StartsWith("ashdi_"))
                        {
                            var parts = selectedVoice.Split('_');
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int sourceId))
                            {
                                var selectedSource = episodesResponse.Ashdi.FirstOrDefault(a => a.Id == sourceId);
                                if (selectedSource != null)
                                {
                                    foreach (var episodeData in selectedSource.EpisodesData.OrderBy(e => e.EpisodeNumber))
                                    {
                                        string episodeName = !string.IsNullOrEmpty(episodeData.Title) ? episodeData.Title : $"Епізод {episodeData.EpisodeNumber}";
                                        string episodeLink = $"{host}/anihub/play?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&s={s}&e={episodeData.EpisodeNumber}&t={selectedVoice}_{episodeData.Id}";
                                        episode_tpl.Append(episodeName, title ?? original_title, s.ToString(), episodeData.EpisodeNumber.ToString("D2"), episodeLink, "call");
                                    }
                                }
                            }
                        }
                    }

                    int voiceCount = episodesResponse.Moonanime.Count + episodesResponse.Ashdi.Count;
                    int episodeCount = 0;
                    if (!string.IsNullOrEmpty(selectedVoice))
                    {
                        if (selectedVoice.StartsWith("moon_"))
                        {
                            var parts = selectedVoice.Split('_');
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int sourceId))
                            {
                                var selectedSource = episodesResponse.Moonanime.FirstOrDefault(m => m.Id == sourceId);
                                episodeCount = selectedSource?.Episodes?.Count ?? 0;
                            }
                        }
                        else if (selectedVoice.StartsWith("ashdi_"))
                        {
                            var parts = selectedVoice.Split('_');
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int sourceId))
                            {
                                var selectedSource = episodesResponse.Ashdi.FirstOrDefault(a => a.Id == sourceId);
                                episodeCount = selectedSource?.EpisodesData?.Count ?? 0;
                            }
                        }
                    }

                    OnLog($"Anihub: generated {voiceCount} voices, {episodeCount} episodes");

                    if (rjson)
                        return Content(episode_tpl.ToJson(voice_tpl), "application/json; charset=utf-8");

                    return Content(voice_tpl.ToHtml() + episode_tpl.ToHtml(), "text/html; charset=utf-8");
                }
            }
            else // Фільм
            {
                var firstAnime = searchResponse.content.FirstOrDefault();
                if (firstAnime == null)
                    return OnError();

                var episodesResponse = await invoke.Embed(firstAnime.Id.ToString());
                if (episodesResponse == null)
                    return OnError();

                var movie_tpl = new MovieTpl(title, original_title);

                // Обробляємо джерела Moonanime
                foreach (var moonSource in episodesResponse.Moonanime)
                {
                    foreach (var episode in moonSource.Episodes)
                    {
                        string voiceName = $"[Moon] {moonSource.StudioName}";
                        string link = $"{host}/anihub/play?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&s=0&e={episode.EpisodeNumber}&t=moon_{moonSource.Id}_{episode.Id}";
                        movie_tpl.Append(voiceName, link, "call");
                    }
                }

                // Обробляємо джерела Ashdi
                foreach (var ashdiSource in episodesResponse.Ashdi)
                {
                    foreach (var episodeData in ashdiSource.EpisodesData)
                    {
                        string voiceName = $"[Ashdi] {ashdiSource.StudioName}";
                        string link = $"{host}/anihub/play?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&s=0&e={episodeData.EpisodeNumber}&t=ashdi_{ashdiSource.Id}_{episodeData.Id}";
                        movie_tpl.Append(voiceName, link, "call");
                    }
                }

                int totalOptions = 0;
                foreach (var moonSource in episodesResponse.Moonanime)
                    totalOptions += moonSource.Episodes.Count;
                foreach (var ashdiSource in episodesResponse.Ashdi)
                    totalOptions += ashdiSource.EpisodesData.Count;

                OnLog($"Anihub: generated {totalOptions} movie options");
                return rjson ? Content(movie_tpl.ToJson(), "application/json; charset=utf-8") : Content(movie_tpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        [HttpGet]
        [Route("play")]
        async public Task<ActionResult> Play(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int s, int e, string t, bool play = false)
        {
            var init = await loadKit(ModInit.Anihub);
            if (!init.enable)
                return OnError();

            OnLog($"Anihub play: {title} s={s} e={e} ({t})");

            var invoke = new AnihubInvoke(init, hybridCache, OnLog, proxyManager);

            // Парсимо токен озвучки/джерела
            if (string.IsNullOrEmpty(t))
                return OnError();

            var parts = t.Split('_');
            if (parts.Length < 3)
                return OnError();

            string sourceType = parts[0];
            if (!int.TryParse(parts[1], out int sourceId) || !int.TryParse(parts[2], out int episodeId))
                return OnError();

            // Знаходимо аніме та отримуємо джерела епізодів
            var searchResponse = await invoke.Search(title, original_title, "0", year.ToString());
            if (searchResponse == null || searchResponse.IsEmpty || s >= searchResponse.content.Count)
                return OnError();

            var selectedAnime = searchResponse.content[s];
            var episodesResponse = await invoke.Embed(selectedAnime.Id.ToString());
            if (episodesResponse == null)
                return OnError();

            string iframeUrl = null;

            if (sourceType == "moon")
            {
                var moonSource = episodesResponse.Moonanime.FirstOrDefault(m => m.Id == sourceId);
                var episode = moonSource?.Episodes.FirstOrDefault(ep => ep.Id == episodeId);
                iframeUrl = episode?.IframeLink;
            }
            else if (sourceType == "ashdi")
            {
                var ashdiSource = episodesResponse.Ashdi.FirstOrDefault(a => a.Id == sourceId);
                var episodeData = ashdiSource?.EpisodesData.FirstOrDefault(ep => ep.Id == episodeId.ToString());
                iframeUrl = episodeData?.VodUrl;
            }

            if (string.IsNullOrEmpty(iframeUrl))
            {
                OnLog($"Anihub play: iframe URL not found for {sourceType}");
                return OnError();
            }

            // Отримуємо пряме посилання на потік
            string streamUrl = await ExtractStreamUrl(iframeUrl, sourceType);

            if (string.IsNullOrEmpty(streamUrl))
            {
                OnLog($"Anihub play: stream extraction failed for {sourceType}");
                return OnError();
            }

            OnLog($"Anihub play: extracted {sourceType} stream");

            // Якщо play=true, робимо Redirect, інакше повертаємо JSON
            if (play)
                return Redirect(streamUrl);
            else
                return Content(VideoTpl.ToJson("play", streamUrl, title ?? original_title), "application/json; charset=utf-8");
        }

        private async Task<string> ExtractStreamUrl(string iframeUrl, string sourceType)
        {
            try
            {
                string requestUrl = iframeUrl;

                // Додаємо параметр player тільки для Moon
                if (sourceType == "moon")
                {
                    requestUrl = iframeUrl + (iframeUrl.Contains("?") ? "&" : "?") + $"player={host}";
                }

                // Створюємо HTTP клієнт з правильними заголовками
                using var httpClient = new HttpClient();

                if (sourceType == "moon")
                {
                    httpClient.DefaultRequestHeaders.Add("Referer", "https://moonanime.art/");
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                    httpClient.DefaultRequestHeaders.Add("Accept-Language", "uk-UA,uk;q=0.9,en-US;q=0.8,en;q=0.7");
                }
                else
                {
                    httpClient.DefaultRequestHeaders.Add("Referer", host);
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0.1 Safari/605.1.15");
                }

                // Робимо запит до плеєра
                OnLog($"Anihub player: requesting {sourceType} iframe");
                var response = await httpClient.GetStringAsync(requestUrl);

                if (string.IsNullOrEmpty(response))
                    return null;

                // Парсимо відповідь для отримання file URL
                string streamUrl = null;

                if (sourceType == "ashdi")
                {
                    var match = Regex.Match(response, @"file:'([^']+)'");
                    if (match.Success)
                        streamUrl = match.Groups[1].Value;
                }
                else if (sourceType == "moon")
                {
                    var match = Regex.Match(response, @"file:\s*""([^""]+)""");
                    if (match.Success)
                        streamUrl = match.Groups[1].Value;
                }

                if (!string.IsNullOrEmpty(streamUrl))
                    OnLog($"Anihub player: extracted {sourceType} stream");

                return streamUrl;
            }
            catch (Exception ex)
            {
                OnLog($"Anihub player: {sourceType} error - {ex.Message}");
                return null;
            }
        }
    }
}
