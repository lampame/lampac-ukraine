using JackTor.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Services.Utilities;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace JackTor.Controllers
{
    public class Controller : BaseOnlineController<JackTorSettings>
    {
        ProxyManager proxyManager;

        public Controller() : base(ModInit.Settings)
        {
            proxyManager = new ProxyManager(ModInit.Settings);
        }

        [HttpGet]
        [Route("lite/jacktor")]
        async public Task<ActionResult> Index(
            long id,
            string imdb_id,
            long kinopoisk_id,
            string title,
            string original_title,
            string original_language,
            int year,
            string source,
            int serial,
            string account_email,
            string t = null,
            int s = -1,
            bool rjson = false,
            bool checksearch = false)
        {
            await UpdateService.ConnectAsync(host);

            var init = loadKit(ModInit.Settings);
            if (!init.enable)
                return Forbid();

            if (NoAccessGroup(init, out string error_msg))
                return Json(new { accsdb = true, error_msg });

            var invoke = new JackTorInvoke(init, hybridCache, OnLog, proxyManager);

            if (checksearch)
            {
                if (!IsCheckOnlineSearchEnabled())
                    return OnError("jacktor", refresh_proxy: true);

                var check = await invoke.Search(title, original_title, year, serial, original_language);
                if (check.Count > 0)
                    return Content("data-json=", "text/plain; charset=utf-8");

                return OnError("jacktor", refresh_proxy: true);
            }

            var torrents = await invoke.Search(title, original_title, year, serial, original_language);
            if (torrents == null || torrents.Count == 0)
            {
                string debugInfo = $"title={title}\noriginal_title={original_title}\nyear={year}\nserial={serial}\njackett={MaskSensitiveUrl(init.jackett)}\nmin_sid={init.min_sid}\nmin_peers={init.min_peers}";
                return OnError("jacktor", refresh_proxy: true, weblog: debugInfo);
            }

            if (serial == 1)
            {
                var seasons = torrents
                    .Where(i => i.Seasons != null && i.Seasons.Length > 0)
                    .SelectMany(i => i.Seasons)
                    .Distinct()
                    .OrderBy(i => i)
                    .ToList();

                string enTitle = HttpUtility.UrlEncode(title);
                string enOriginal = HttpUtility.UrlEncode(original_title);

                if (s == -1 && seasons.Count > 0)
                {
                    string quality = torrents.FirstOrDefault(i => i.Quality >= 2160)?.QualityLabel
                                     ?? torrents.FirstOrDefault(i => i.Quality >= 1080)?.QualityLabel
                                     ?? torrents.FirstOrDefault(i => i.Quality >= 720)?.QualityLabel
                                     ?? "720p";

                    var seasonTpl = new SeasonTpl(quality: quality);
                    foreach (int season in seasons)
                    {
                        seasonTpl.Append(
                            $"{season} сезон",
                            $"{host}/lite/jacktor?rjson={rjson}&title={enTitle}&original_title={enOriginal}&year={year}&original_language={original_language}&serial=1&s={season}",
                            season);
                    }

                    return rjson
                        ? Content(seasonTpl.ToJson(), "application/json; charset=utf-8")
                        : Content(seasonTpl.ToHtml(), "text/html; charset=utf-8");
                }

                int targetSeason = s == -1 ? 1 : s;
                var releases = torrents
                    .Where(i => i.Seasons == null || i.Seasons.Length == 0 || i.Seasons.Contains(targetSeason))
                    .ToList();

                if (releases.Count == 0)
                    releases = torrents;

                var similarTpl = new SimilarTpl();
                foreach (var torrent in releases)
                {
                    string seasonLabel = (torrent.Seasons != null && torrent.Seasons.Length > 0)
                        ? $"S{string.Join(",", torrent.Seasons.OrderBy(i => i))}"
                        : $"S{targetSeason}";

                    string releaseName = string.IsNullOrWhiteSpace(torrent.Voice)
                        ? $"{seasonLabel} • {(torrent.Tracker ?? "Без назви")}"
                        : $"{seasonLabel} • {torrent.Voice}";

                    string qualityInfo = $"{torrent.Tracker} / {torrent.QualityLabel} / {torrent.MediaInfo} / ↑{torrent.Seeders}";
                    string releaseLink = accsArgs($"{host}/lite/jacktor/serial/{torrent.Rid}?rjson={rjson}&title={enTitle}&original_title={enOriginal}&s={targetSeason}");

                    similarTpl.Append(releaseName, null, qualityInfo, releaseLink);
                }

                return rjson
                    ? Content(similarTpl.ToJson(), "application/json; charset=utf-8")
                    : Content(similarTpl.ToHtml(), "text/html; charset=utf-8");
            }
            else
            {
                var movieTpl = new MovieTpl(title, original_title);

                foreach (var torrent in torrents)
                {
                    string voice = string.IsNullOrWhiteSpace(torrent.Voice)
                        ? (torrent.Tracker ?? "Торрент")
                        : torrent.Voice;

                    string voiceName = $"{torrent.QualityLabel} / {torrent.MediaInfo} / ↑{torrent.Seeders}";
                    string streamLink = accsArgs($"{host}/lite/jacktor/s{torrent.Rid}");

                    movieTpl.Append(
                        voice,
                        streamLink,
                        voice_name: voiceName,
                        quality: torrent.Quality > 0 ? torrent.Quality.ToString() : null);
                }

                return rjson
                    ? Content(movieTpl.ToJson(), "application/json; charset=utf-8")
                    : Content(movieTpl.ToHtml(), "text/html; charset=utf-8");
            }
        }

        [HttpGet]
        [Route("lite/jacktor/serial/{rid}")]
        async public ValueTask<ActionResult> Serial(string rid, string account_email, string title, string original_title, int s = 1, bool rjson = false)
        {
            var init = loadKit(ModInit.Settings);
            if (!init.enable)
                return Forbid();

            if (NoAccessGroup(init, out string error_msg))
                return Json(new { accsdb = true, error_msg });

            var invoke = new JackTorInvoke(init, hybridCache, OnLog, proxyManager);
            if (!invoke.TryGetSource(rid, out JackTorSourceCache source))
                return OnError("jacktor", refresh_proxy: true);

            string memKey = $"jacktor:serial:{rid}";

            return await InvkSemaphore(memKey, null, async () =>
            {
                if (!hybridCache.TryGetValue(memKey, out FileStat[] fileStats))
                {
                    var ts = ResolveProbeTorrentServer(init, account_email);
                    if (string.IsNullOrWhiteSpace(ts.host))
                        return OnError("jacktor", refresh_proxy: true);

                    string hashResponse = await httpHydra.Post(
                        $"{ts.host}/torrents",
                        BuildAddPayload(source.SourceUri),
                        statusCodeOK: false,
                        newheaders: ts.headers);

                    string hash = ExtractHash(hashResponse);
                    if (string.IsNullOrWhiteSpace(hash))
                        return OnError("jacktor", refresh_proxy: true);

                    Stat stat = null;
                    DateTime deadline = DateTime.Now.AddSeconds(20);

                    while (true)
                    {
                        stat = await httpHydra.Post<Stat>(
                            $"{ts.host}/torrents",
                            BuildGetPayload(hash),
                            statusCodeOK: false,
                            newheaders: ts.headers);

                        if (stat?.file_stats != null && stat.file_stats.Length > 0)
                            break;

                        if (DateTime.Now > deadline)
                        {
                            _ = httpHydra.Post($"{ts.host}/torrents", BuildRemovePayload(hash), statusCodeOK: false, newheaders: ts.headers);
                            return OnError("jacktor", refresh_proxy: true);
                        }

                        await Task.Delay(250);
                    }

                    _ = httpHydra.Post($"{ts.host}/torrents", BuildRemovePayload(hash), statusCodeOK: false, newheaders: ts.headers);

                    fileStats = stat.file_stats;
                    hybridCache.Set(memKey, fileStats, DateTime.Now.AddHours(36));
                }

                if (fileStats == null || fileStats.Length == 0)
                    return OnError("jacktor", refresh_proxy: true);

                var episodeTpl = new EpisodeTpl();
                int appended = 0;

                foreach (var file in fileStats.OrderBy(i => i.Id))
                {
                    if (!IsVideoFile(file.Path))
                        continue;

                    episodeTpl.Append(
                        Path.GetFileName(file.Path),
                        title ?? original_title,
                        s.ToString(),
                        file.Id.ToString(),
                        accsArgs($"{host}/lite/jacktor/s{rid}?tsid={file.Id}"));

                    appended++;
                }

                if (appended == 0)
                    return OnError("jacktor", refresh_proxy: true);

                return rjson
                    ? Content(episodeTpl.ToJson(), "application/json; charset=utf-8")
                    : Content(episodeTpl.ToHtml(), "text/html; charset=utf-8");
            });
        }

        [HttpGet]
        [Route("lite/jacktor/s{rid}")]
        async public ValueTask<ActionResult> Stream(string rid, int tsid = -1, string account_email = null)
        {
            var init = loadKit(ModInit.Settings);
            if (!init.enable)
                return Forbid();

            if (NoAccessGroup(init, out string error_msg))
                return Json(new { accsdb = true, error_msg });

            var invoke = new JackTorInvoke(init, hybridCache, OnLog, proxyManager);
            if (!invoke.TryGetSource(rid, out JackTorSourceCache source))
                return OnError("jacktor", refresh_proxy: true);

            int index = tsid != -1 ? tsid : 1;
            string country = requestInfo.Country;

            async ValueTask<ActionResult> AuthStream(string tsHost, string login, string passwd, string uhost = null, Dictionary<string, string> addheaders = null)
            {
                string memKey = $"jacktor:auth_stream:{rid}:{uhost ?? tsHost}";
                if (!hybridCache.TryGetValue(memKey, out string hash))
                {
                    login = (login ?? string.Empty).Replace("{account_email}", account_email ?? string.Empty);

                    var headers = HeadersModel.Init("Authorization", $"Basic {CrypTo.Base64($"{login}:{passwd}")}");
                    headers = HeadersModel.Join(headers, addheaders);

                    string response = await httpHydra.Post(
                        $"{tsHost}/torrents",
                        BuildAddPayload(source.SourceUri),
                        statusCodeOK: false,
                        newheaders: headers);

                    hash = ExtractHash(response);
                    if (string.IsNullOrWhiteSpace(hash))
                        return OnError("jacktor", refresh_proxy: true);

                    hybridCache.Set(memKey, hash, DateTime.Now.AddMinutes(1));
                }

                return Redirect($"{uhost ?? tsHost}/stream?link={hash}&index={index}&play");
            }

            if ((init.torrs == null || init.torrs.Length == 0) && (init.auth_torrs == null || init.auth_torrs.Count == 0))
            {
                if (TryReadLocalTorrServerPassword(out string localPassword))
                {
                    return await AuthStream(
                        $"http://{AppInit.conf.listen.localhost}:9080",
                        "ts",
                        localPassword,
                        uhost: $"{host}/ts");
                }

                return Redirect($"{host}/ts/stream?link={HttpUtility.UrlEncode(source.SourceUri)}&index={index}&play");
            }

            if (init.auth_torrs != null && init.auth_torrs.Count > 0)
            {
                string tsKey = $"jacktor:ts2:{rid}:{requestInfo.IP}";
                if (!hybridCache.TryGetValue(tsKey, out PidTorAuthTS ts))
                {
                    var servers = init.auth_torrs.Where(i => i.enable).ToList();
                    if (country != null)
                    {
                        servers = servers
                            .Where(i => i.country == null || i.country.Contains(country))
                            .Where(i => i.no_country == null || !i.no_country.Contains(country))
                            .ToList();
                    }

                    if (servers.Count == 0)
                        return OnError("jacktor", refresh_proxy: true);

                    ts = servers[Random.Shared.Next(0, servers.Count)];
                    hybridCache.Set(tsKey, ts, DateTime.Now.AddHours(4));
                }

                return await AuthStream(ts.host, ts.login, ts.passwd, addheaders: ts.headers);
            }
            else
            {
                if (init.base_auth != null && init.base_auth.enable)
                {
                    if (init.torrs == null || init.torrs.Length == 0)
                        return OnError("jacktor", refresh_proxy: true);

                    string tsKey = $"jacktor:ts3:{rid}:{requestInfo.IP}";
                    if (!hybridCache.TryGetValue(tsKey, out string tsHost))
                    {
                        tsHost = init.torrs[Random.Shared.Next(0, init.torrs.Length)];
                        hybridCache.Set(tsKey, tsHost, DateTime.Now.AddHours(4));
                    }

                    return await AuthStream(tsHost, init.base_auth.login, init.base_auth.passwd, addheaders: init.base_auth.headers);
                }

                if (init.torrs == null || init.torrs.Length == 0)
                    return OnError("jacktor", refresh_proxy: true);

                string key = $"jacktor:ts4:{rid}:{requestInfo.IP}";
                if (!hybridCache.TryGetValue(key, out string torrentHost))
                {
                    torrentHost = init.torrs[Random.Shared.Next(0, init.torrs.Length)];
                    hybridCache.Set(key, torrentHost, DateTime.Now.AddHours(4));
                }

                return Redirect($"{torrentHost}/stream?link={HttpUtility.UrlEncode(source.SourceUri)}&index={index}&play");
            }
        }

        private (List<HeadersModel> headers, string host) ResolveProbeTorrentServer(JackTorSettings init, string account_email)
        {
            if ((init.torrs == null || init.torrs.Length == 0) && (init.auth_torrs == null || init.auth_torrs.Count == 0))
            {
                if (TryReadLocalTorrServerPassword(out string localPassword))
                {
                    var headers = HeadersModel.Init("Authorization", $"Basic {CrypTo.Base64($"ts:{localPassword}")}");
                    return (headers, $"http://{AppInit.conf.listen.localhost}:9080");
                }

                return (null, $"http://{AppInit.conf.listen.localhost}:9080");
            }

            if (init.auth_torrs != null && init.auth_torrs.Count > 0)
            {
                var ts = init.auth_torrs.FirstOrDefault(i => i.enable) ?? init.auth_torrs.First();
                string login = (ts.login ?? string.Empty).Replace("{account_email}", account_email ?? string.Empty);
                var auth = HeadersModel.Init("Authorization", $"Basic {CrypTo.Base64($"{login}:{ts.passwd}")}");

                return (httpHeaders(ts.host, HeadersModel.Join(auth, ts.headers)), ts.host);
            }

            if (init.base_auth != null && init.base_auth.enable)
            {
                string tsHost = init.torrs?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(tsHost))
                    return (null, null);

                string login = (init.base_auth.login ?? string.Empty).Replace("{account_email}", account_email ?? string.Empty);
                var auth = HeadersModel.Init("Authorization", $"Basic {CrypTo.Base64($"{login}:{init.base_auth.passwd}")}");

                return (httpHeaders(tsHost, HeadersModel.Join(auth, init.base_auth.headers)), tsHost);
            }

            return (null, init.torrs?.FirstOrDefault());
        }

        private bool TryReadLocalTorrServerPassword(out string password)
        {
            password = null;

            if (!System.IO.File.Exists("torrserver/accs.db"))
                return false;

            string accs = System.IO.File.ReadAllText("torrserver/accs.db");
            password = Regex.Match(accs, "\"ts\":\"([^\"]+)\"").Groups[1].Value;
            return !string.IsNullOrWhiteSpace(password);
        }

        private static string BuildAddPayload(string sourceUri)
        {
            return JsonSerializer.Serialize(new
            {
                action = "add",
                link = sourceUri,
                title = string.Empty,
                poster = string.Empty,
                save_to_db = false
            });
        }

        private static string BuildGetPayload(string hash)
        {
            return JsonSerializer.Serialize(new
            {
                action = "get",
                hash
            });
        }

        private static string BuildRemovePayload(string hash)
        {
            return JsonSerializer.Serialize(new
            {
                action = "rem",
                hash
            });
        }

        private static string ExtractHash(string response)
        {
            return Regex.Match(response ?? string.Empty, "\"hash\":\"([^\"]+)\"").Groups[1].Value;
        }

        private static bool IsVideoFile(string path)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            return ext switch
            {
                ".srt" => false,
                ".txt" => false,
                ".jpg" => false,
                ".jpeg" => false,
                ".png" => false,
                ".nfo" => false,
                _ => true,
            };
        }

        private static string MaskSensitiveUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            return Regex.Replace(url, "(apikey=)[^&]+", "$1***", RegexOptions.IgnoreCase);
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
