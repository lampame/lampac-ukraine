using Shared.Engine;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Shared;
using Shared.Models.Templates;
using Shared.Models.Online.Settings;
using Shared.Models;
using Makhno.Models;

namespace Makhno
{
    [Route("makhno")]
    public class MakhnoController : BaseOnlineController
    {
        private readonly ProxyManager proxyManager;

        public MakhnoController() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.Makhno);
        }

        [HttpGet]
        public async Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, int season = -1, bool rjson = false, bool checksearch = false)
        {
            if (checksearch)
            {
                if (AppInit.conf?.online?.checkOnlineSearch != true)
                    return OnError();

                return Content("data-json=", "text/plain; charset=utf-8");
            }

            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.Makhno);
            if (!init.enable)
                return OnError();
            Initialization(init);

            OnLog($"Makhno: {title} (serial={serial}, s={s}, season={season}, t={t})");

            var invoke = new MakhnoInvoke(init, hybridCache, OnLog, proxyManager);

            var resolved = await ResolvePlaySource(imdb_id, title, original_title, year, serial, invoke);
            if (resolved == null || string.IsNullOrEmpty(resolved.PlayUrl))
                return OnError();

            if (resolved.ShouldEnrich)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await EnrichWormhole(imdb_id, year, resolved, invoke);
                    }
                    catch (Exception ex)
                    {
                        OnLog($"Makhno wormhole enrich failed: {ex.Message}");
                    }
                });
            }

            if (resolved.IsSerial)
                return await HandleSerial(resolved.PlayUrl, imdb_id, title, original_title, year, t, season, rjson, invoke);

            return await HandleMovie(resolved.PlayUrl, imdb_id, title, original_title, year, rjson, invoke);
        }

        [HttpGet]
        [Route("play")]
        public async Task<ActionResult> Play(long id, string imdb_id, long kinopoisk_id, string title, string original_title, int year, int s, int season, string t, string episodeId, bool play = false, bool rjson = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.Makhno);
            if (!init.enable)
                return OnError();
            Initialization(init);

            OnLog($"Makhno Play: {title} (s={s}, season={season}, t={t}, episodeId={episodeId}) play={play}");

            var invoke = new MakhnoInvoke(init, hybridCache, OnLog, proxyManager);
            var resolved = await ResolvePlaySource(imdb_id, title, original_title, year, serial: 1, invoke);
            if (resolved == null || string.IsNullOrEmpty(resolved.PlayUrl))
                return OnError();

            var playerData = await InvokeCache<PlayerData>($"makhno:player:{resolved.PlayUrl}", TimeSpan.FromMinutes(10), async () =>
            {
                return await invoke.GetPlayerData(resolved.PlayUrl);
            });

            if (playerData?.Voices == null || !playerData.Voices.Any())
            {
                OnLog("Makhno Play: no voices parsed");
                return OnError();
            }

            if (string.IsNullOrEmpty(t) || !int.TryParse(t, out int voiceIndex) || voiceIndex >= playerData.Voices.Count)
                return OnError();

            var selectedVoice = playerData.Voices[voiceIndex];
            int seasonIndex = season > 0 ? season - 1 : season;
            if (seasonIndex < 0 || seasonIndex >= selectedVoice.Seasons.Count)
                return OnError();

            var selectedSeason = selectedVoice.Seasons[seasonIndex];
            foreach (var episode in selectedSeason.Episodes)
            {
                if (episode.Id == episodeId && !string.IsNullOrEmpty(episode.File))
                {
                    OnLog($"Makhno Play: Found episode {episode.Title}, stream: {episode.File}");

                    string streamUrl = BuildStreamUrl(init, episode.File);
                    string episodeTitle = $"{title ?? original_title} - {episode.Title}";

                    if (play)
                        return UpdateService.Validate(Redirect(streamUrl));

                    return UpdateService.Validate(Content(VideoTpl.ToJson("play", streamUrl, episodeTitle), "application/json; charset=utf-8"));
                }
            }

            OnLog("Makhno Play: Episode not found");
            return OnError();
        }

        [HttpGet]
        [Route("play/movie")]
        public async Task<ActionResult> PlayMovie(long id, string imdb_id, string title, string original_title, int year, bool play = false, bool rjson = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = await loadKit(ModInit.Makhno);
            if (!init.enable)
                return OnError();
            Initialization(init);

            OnLog($"Makhno PlayMovie: {title} ({year}) play={play}");

            var invoke = new MakhnoInvoke(init, hybridCache, OnLog, proxyManager);
            var resolved = await ResolvePlaySource(imdb_id, title, original_title, year, serial: 0, invoke);
            if (resolved == null || string.IsNullOrEmpty(resolved.PlayUrl))
                return OnError();

            var playerData = await InvokeCache<PlayerData>($"makhno:player:{resolved.PlayUrl}", TimeSpan.FromMinutes(10), async () =>
            {
                return await invoke.GetPlayerData(resolved.PlayUrl);
            });

            if (playerData?.File == null)
            {
                OnLog("Makhno PlayMovie: no file parsed");
                return OnError();
            }

            string streamUrl = BuildStreamUrl(init, playerData.File);

            if (play)
                return UpdateService.Validate(Redirect(streamUrl));

            return UpdateService.Validate(Content(VideoTpl.ToJson("play", streamUrl, title ?? original_title), "application/json; charset=utf-8"));
        }

        private async Task<ActionResult> HandleMovie(string playUrl, string imdb_id, string title, string original_title, int year, bool rjson, MakhnoInvoke invoke)
        {
            var init = ModInit.Makhno;
            var playerData = await InvokeCache<PlayerData>($"makhno:player:{playUrl}", TimeSpan.FromMinutes(10), async () =>
            {
                return await invoke.GetPlayerData(playUrl);
            });

            var movieStreams = playerData?.Movies?
                .Where(m => m != null && !string.IsNullOrEmpty(m.File))
                .ToList() ?? new List<MovieVariant>();

            if (movieStreams.Count == 0 && !string.IsNullOrEmpty(playerData?.File))
            {
                movieStreams.Add(new MovieVariant
                {
                    File = playerData.File,
                    Title = "Основне джерело",
                    Quality = "auto"
                });
            }

            if (movieStreams.Count == 0)
            {
                OnLog("Makhno HandleMovie: no file parsed");
                return OnError();
            }

            var tpl = new MovieTpl(title ?? original_title, original_title, movieStreams.Count);
            int index = 1;
            foreach (var stream in movieStreams)
            {
                string label = !string.IsNullOrWhiteSpace(stream.Title)
                    ? stream.Title
                    : $"Варіант {index}";

                tpl.Append(label, BuildStreamUrl(init, stream.File));
                index++;
            }

            return rjson ? Content(tpl.ToJson(), "application/json; charset=utf-8") : Content(tpl.ToHtml(), "text/html; charset=utf-8");
        }

        private async Task<ActionResult> HandleSerial(string playUrl, string imdb_id, string title, string original_title, int year, string t, int season, bool rjson, MakhnoInvoke invoke)
        {
            var init = ModInit.Makhno;

            var playerData = await InvokeCache<PlayerData>($"makhno:player:{playUrl}", TimeSpan.FromMinutes(10), async () =>
            {
                return await invoke.GetPlayerData(playUrl);
            });

            if (playerData?.Voices == null || !playerData.Voices.Any())
            {
                OnLog("Makhno HandleSerial: no voices parsed");
                return OnError();
            }

            var voiceSeasons = playerData.Voices
                .Select((voice, index) => new
                {
                    Voice = voice,
                    Index = index,
                    Seasons = GetSeasonsWithNumbers(voice)
                })
                .Where(v => v.Seasons.Count > 0)
                .ToList();

            // Debug logging disabled to avoid noisy output in production.

            var seasonNumbers = voiceSeasons
                .SelectMany(v => v.Seasons.Select(s => s.Number))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            if (seasonNumbers.Count == 0)
                return OnError();

            if (season == -1)
            {
                int? seasonVoiceIndex = null;
                if (int.TryParse(t, out int tIndex) && tIndex >= 0 && tIndex < playerData.Voices.Count)
                    seasonVoiceIndex = tIndex;

                if (seasonVoiceIndex.HasValue)
                {
                    var seasonsForVoice = GetSeasonsWithNumbers(playerData.Voices[seasonVoiceIndex.Value])
                        .Select(s => s.Number)
                        .Distinct()
                        .OrderBy(n => n)
                        .ToList();

                    if (seasonsForVoice.Count > 0)
                        seasonNumbers = seasonsForVoice;
                }

                var season_tpl = new SeasonTpl();
                foreach (var seasonNumber in seasonNumbers)
                {
                    (Season Season, int Number)? seasonItem = null;
                    if (seasonVoiceIndex.HasValue)
                    {
                        var voiceSeasonsForT = GetSeasonsWithNumbers(playerData.Voices[seasonVoiceIndex.Value]);
                        var match = voiceSeasonsForT.FirstOrDefault(s => s.Number == seasonNumber);
                        seasonItem = match.Season != null ? match : ((Season Season, int Number)?)null;
                    }
                    else
                    {
                        var match = voiceSeasons
                            .SelectMany(v => v.Seasons)
                            .FirstOrDefault(s => s.Number == seasonNumber);
                        seasonItem = match.Season != null ? match : ((Season Season, int Number)?)null;
                    }

                    string voiceParam = seasonVoiceIndex.HasValue ? $"&t={seasonVoiceIndex.Value}" : string.Empty;
                    string seasonName = seasonItem.HasValue ? seasonItem.Value.Season?.Title ?? $"Сезон {seasonNumber}" : $"Сезон {seasonNumber}";
                    string link = $"{host}/makhno?imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&season={seasonNumber}{voiceParam}";
                    season_tpl.Append(seasonName, link, seasonNumber.ToString());
                }

                return rjson ? Content(season_tpl.ToJson(), "application/json; charset=utf-8") : Content(season_tpl.ToHtml(), "text/html; charset=utf-8");
            }

            var voice_tpl = new VoiceTpl();
            var episode_tpl = new EpisodeTpl();

            int requestedSeason = seasonNumbers.Contains(season) ? season : seasonNumbers.First();

            int? seasonVoiceIndexForTpl = null;
            string selectedVoice = t;
            if (string.IsNullOrEmpty(selectedVoice) || !int.TryParse(selectedVoice, out int selectedVoiceIndex))
            {
                var voiceWithSeason = voiceSeasons.FirstOrDefault(v => v.Seasons.Any(s => s.Number == requestedSeason));
                selectedVoice = voiceWithSeason != null ? voiceWithSeason.Index.ToString() : voiceSeasons.First().Index.ToString();
            }
            else if (selectedVoiceIndex >= 0 && selectedVoiceIndex < playerData.Voices.Count)
            {
                seasonVoiceIndexForTpl = selectedVoiceIndex;
            }

            HashSet<int> selectedVoiceSeasonSet = null;
            if (seasonVoiceIndexForTpl.HasValue)
            {
                selectedVoiceSeasonSet = GetSeasonsWithNumbers(playerData.Voices[seasonVoiceIndexForTpl.Value])
                    .Select(s => s.Number)
                    .ToHashSet();
            }
            else
            {
                selectedVoiceSeasonSet = seasonNumbers.ToHashSet();
            }

            // Build season template for selected voice (if valid) to keep season list in sync when switching voices.
            var seasonTplForVoice = new SeasonTpl();
            List<int> seasonNumbersForTpl = seasonNumbers;
            if (seasonVoiceIndexForTpl.HasValue)
            {
                var seasonsForVoiceTpl = GetSeasonsWithNumbers(playerData.Voices[seasonVoiceIndexForTpl.Value])
                    .Select(s => s.Number)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                if (seasonsForVoiceTpl.Count > 0)
                    seasonNumbersForTpl = seasonsForVoiceTpl;
            }

            foreach (var seasonNumber in seasonNumbersForTpl)
            {
                (Season Season, int Number)? seasonItem = null;
                if (seasonVoiceIndexForTpl.HasValue)
                {
                    var voiceSeasonsForT = GetSeasonsWithNumbers(playerData.Voices[seasonVoiceIndexForTpl.Value]);
                    var match = voiceSeasonsForT.FirstOrDefault(s => s.Number == seasonNumber);
                    seasonItem = match.Season != null ? match : ((Season Season, int Number)?)null;
                }
                else
                {
                    var match = voiceSeasons
                        .SelectMany(v => v.Seasons)
                        .FirstOrDefault(s => s.Number == seasonNumber);
                    seasonItem = match.Season != null ? match : ((Season Season, int Number)?)null;
                }

                string voiceParam = seasonVoiceIndexForTpl.HasValue ? $"&t={seasonVoiceIndexForTpl.Value}" : string.Empty;
                string seasonName = seasonItem.HasValue ? seasonItem.Value.Season?.Title ?? $"Сезон {seasonNumber}" : $"Сезон {seasonNumber}";
                string link = $"{host}/makhno?imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&season={seasonNumber}{voiceParam}";
                seasonTplForVoice.Append(seasonName, link, seasonNumber.ToString());
            }

            for (int i = 0; i < playerData.Voices.Count; i++)
            {
                var voice = playerData.Voices[i];
                string voiceName = voice.Name ?? $"Озвучка {i + 1}";
                var seasonsForVoice = GetSeasonsWithNumbers(voice);
                if (seasonsForVoice.Count == 0)
                    continue;

                string voiceLink;
                bool hasRequestedSeason = seasonsForVoice.Any(s => s.Number == requestedSeason);
                bool sameSeasonSet = seasonsForVoice.Select(s => s.Number).ToHashSet().SetEquals(selectedVoiceSeasonSet);
                if (hasRequestedSeason && sameSeasonSet)
                {
                    voiceLink = $"{host}/makhno?imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&season={requestedSeason}&t={i}";
                }
                else
                {
                    voiceLink = $"{host}/makhno?imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&season=-1&t={i}";
                }

                bool isActive = selectedVoice == i.ToString();
                voice_tpl.Append(voiceName, isActive, voiceLink);
            }

            if (!string.IsNullOrEmpty(selectedVoice) && int.TryParse(selectedVoice, out int voiceIndex) && voiceIndex < playerData.Voices.Count)
            {
                var selectedVoiceData = playerData.Voices[voiceIndex];
                var seasonsForVoice = GetSeasonsWithNumbers(selectedVoiceData);
                if (seasonsForVoice.Count > 0)
                {
                    bool hasRequestedSeason = seasonsForVoice.Any(s => s.Number == requestedSeason);
                    if (!hasRequestedSeason)
                    {
                        string redirectUrl = $"{host}/makhno?imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&season=-1&t={voiceIndex}";
                        return UpdateService.Validate(Redirect(redirectUrl));
                    }

                    var selectedSeason = seasonsForVoice.First(s => s.Number == requestedSeason).Season;
                    var sortedEpisodes = selectedSeason.Episodes.OrderBy(e => ExtractEpisodeNumber(e.Title)).ToList();

                    for (int i = 0; i < sortedEpisodes.Count; i++)
                    {
                        var episode = sortedEpisodes[i];
                        if (!string.IsNullOrEmpty(episode.File))
                        {
                            string streamUrl = BuildStreamUrl(init, episode.File);
                            episode_tpl.Append(
                                episode.Title,
                                title ?? original_title,
                                requestedSeason.ToString(),
                                (i + 1).ToString("D2"),
                                streamUrl
                            );
                        }
                    }
                }
            }

            episode_tpl.Append(voice_tpl);
            if (rjson)
                return Content(episode_tpl.ToJson(), "application/json; charset=utf-8");

            return Content(seasonTplForVoice.ToHtml() + episode_tpl.ToHtml(), "text/html; charset=utf-8");
        }

        private int ExtractEpisodeNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return 0;

            var match = System.Text.RegularExpressions.Regex.Match(title, @"(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private int? ExtractSeasonNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return null;

            var match = System.Text.RegularExpressions.Regex.Match(title, @"(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
        }

        private List<(Season Season, int Number)> GetSeasonsWithNumbers(Voice voice)
        {
            var result = new List<(Season Season, int Number)>();
            if (voice?.Seasons == null || voice.Seasons.Count == 0)
                return result;

            for (int i = 0; i < voice.Seasons.Count; i++)
            {
                var season = voice.Seasons[i];
                int number = ExtractSeasonNumber(season?.Title) ?? (i + 1);
                result.Add((season, number));
            }

            return result;
        }

        private async Task<ResolveResult> ResolvePlaySource(string imdbId, string title, string originalTitle, int year, int serial, MakhnoInvoke invoke)
        {
            string playUrl = null;

            if (!string.IsNullOrEmpty(imdbId))
            {
                string cacheKey = $"makhno:wormhole:{imdbId}";
                playUrl = await InvokeCache<string>(cacheKey, TimeSpan.FromMinutes(5), async () =>
                {
                    return await invoke.GetWormholePlay(imdbId);
                });

                if (!string.IsNullOrEmpty(playUrl))
                {
                    return new ResolveResult
                    {
                        PlayUrl = playUrl,
                        IsSerial = IsSerialByUrl(playUrl, serial),
                        ShouldEnrich = false
                    };
                }
            }

            string searchQuery = originalTitle ?? title;
            string klonSearchCacheKey = $"makhno:klonfun:search:{imdbId ?? searchQuery}";
            var klonSearchResults = await InvokeCache<List<KlonSearchResult>>(klonSearchCacheKey, TimeSpan.FromMinutes(10), async () =>
            {
                return await invoke.SearchKlonFUN(imdbId, title, originalTitle);
            });

            if (klonSearchResults != null && klonSearchResults.Count > 0)
            {
                var klonSelected = invoke.SelectKlonFUNItem(klonSearchResults, year > 0 ? year : null, title, originalTitle);
                if (klonSelected != null && !string.IsNullOrWhiteSpace(klonSelected.Url))
                {
                    string klonPlayerCacheKey = $"makhno:klonfun:player:{klonSelected.Url}";
                    string klonPlayerUrl = await InvokeCache<string>(klonPlayerCacheKey, TimeSpan.FromMinutes(10), async () =>
                    {
                        return await invoke.GetKlonFUNPlayerUrl(klonSelected.Url);
                    });

                    if (!string.IsNullOrWhiteSpace(klonPlayerUrl))
                    {
                        string klonAshdiPath = invoke.ExtractAshdiPath(klonPlayerUrl);

                        return new ResolveResult
                        {
                            PlayUrl = klonPlayerUrl,
                            AshdiPath = klonAshdiPath,
                            Selected = new SearchResult
                            {
                                ImdbId = imdbId,
                                Title = klonSelected.Title,
                                TitleEn = originalTitle,
                                Year = klonSelected.Year > 0 ? klonSelected.Year.ToString() : null,
                                Category = invoke.IsSerialPlayerUrl(klonPlayerUrl) ? "Серіал" : "Фільм"
                            },
                            IsSerial = serial == 1 || invoke.IsSerialPlayerUrl(klonPlayerUrl) || IsSerialByUrl(klonPlayerUrl, serial),
                            ShouldEnrich = !string.IsNullOrWhiteSpace(klonAshdiPath)
                        };
                    }
                }
            }

            return null;
        }

        private bool IsSerialByUrl(string url, int serial)
        {
            if (serial == 1)
                return true;

            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("/serial/", StringComparison.OrdinalIgnoreCase);
        }

        private async Task EnrichWormhole(string imdbId, int year, ResolveResult resolved, MakhnoInvoke invoke)
        {
            if (string.IsNullOrWhiteSpace(imdbId) || resolved == null || string.IsNullOrWhiteSpace(resolved.AshdiPath))
                return;

            int? yearValue = year > 0 ? year : null;
            if (!yearValue.HasValue && int.TryParse(resolved.Selected?.Year, out int parsedYear))
                yearValue = parsedYear;

            var tmdbResult = await invoke.FetchTmdbByImdb(imdbId, yearValue, resolved.IsSerial);
            if (tmdbResult == null)
                return;

            var (item, _) = tmdbResult.Value;
            var tmdbId = item.Value<long?>("id");
            if (!tmdbId.HasValue)
                return;

            string mediaType = resolved.IsSerial ? "tv" : "movie";
            int serialValue = resolved.IsSerial ? 1 : 0;

            var payload = new
            {
                imdb_id = imdbId,
                _id = $"{mediaType}:{tmdbId.Value}",
                serial = serialValue,
                ashdi = resolved.AshdiPath
            };

            await invoke.PostWormholeAsync(payload);
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

        private string BuildStreamUrl(OnlinesSettings init, string streamLink)
        {
            string link = streamLink?.Trim();
            if (string.IsNullOrEmpty(link))
                return link;

            link = StripLampacArgs(link);

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

        private class ResolveResult
        {
            public string PlayUrl { get; set; }
            public string AshdiPath { get; set; }
            public SearchResult Selected { get; set; }
            public bool IsSerial { get; set; }
            public bool ShouldEnrich { get; set; }
        }
    }
}
