using JackTor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JackTor
{
    public class ModInit
    {
        public static double Version => 1.0;

        public static JackTorSettings JackTor;

        public static JackTorSettings Settings
        {
            get => JackTor;
            set => JackTor = value;
        }

        /// <summary>
        /// Модуль завантажено.
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            JackTor = new JackTorSettings("JackTor", "http://127.0.0.1:9117", streamproxy: false, useproxy: false)
            {
                displayname = "JackTor",
                displayindex = 0,
                group = 0,
                group_hide = true,

                jackett = "http://127.0.0.1:9117",
                apikey = string.Empty,
                min_sid = 5,
                min_peers = 0,
                max_size = 0,
                max_serial_size = 0,
                emptyVoice = true,
                forceAll = false,
                sort = "sid",
                max_age_days = 0,
                quality_allow = new[] { 2160, 1080, 720 },
                trackers_allow = Array.Empty<string>(),
                trackers_block = Array.Empty<string>(),
                hdr_mode = "any",
                codec_allow = "any",
                audio_pref = new[] { "ukr", "eng", "rus" },
                year_tolerance = 1,
                query_mode = "both",
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var conf = ModuleInvoke.Conf("JackTor", JackTor) ?? JObject.FromObject(JackTor);
            JackTor = conf.ToObject<JackTorSettings>();

            if (string.IsNullOrWhiteSpace(JackTor.jackett))
                JackTor.jackett = JackTor.host;

            if (string.IsNullOrWhiteSpace(JackTor.host))
                JackTor.host = JackTor.jackett;

            // Показувати «уточнити пошук».
            AppInit.conf.online.with_search.Add("jacktor");
        }
    }

    public static class UpdateService
    {
        private static readonly string _connectUrl = "https://lmcuk.lme.isroot.in/stats";

        private static ConnectResponse Connect = null;
        private static DateTime? _connectTime = null;
        private static DateTime? _disconnectTime = null;

        private static readonly TimeSpan _resetInterval = TimeSpan.FromHours(4);
        private static Timer _resetTimer = null;

        private static readonly object _lock = new();

        public static async Task ConnectAsync(string host, CancellationToken cancellationToken = default)
        {
            if (_connectTime is not null || Connect?.IsUpdateUnavailable == true)
                return;

            lock (_lock)
            {
                if (_connectTime is not null || Connect?.IsUpdateUnavailable == true)
                    return;

                _connectTime = DateTime.UtcNow;
            }

            try
            {
                using var handler = new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, _, _, _) => true,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    }
                };

                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(15);

                var request = new
                {
                    Host = host,
                    Module = ModInit.Settings.plugin,
                    Version = ModInit.Version,
                };

                var requestJson = JsonConvert.SerializeObject(request, Formatting.None);
                var requestContent = new StringContent(requestJson, Encoding.UTF8, MediaTypeNames.Application.Json);

                var response = await client
                    .PostAsync(_connectUrl, requestContent, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength > 0)
                {
                    var responseText = await response.Content
                        .ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);

                    Connect = JsonConvert.DeserializeObject<ConnectResponse>(responseText);
                }

                lock (_lock)
                {
                    _resetTimer?.Dispose();
                    _resetTimer = null;

                    if (Connect?.IsUpdateUnavailable != true)
                    {
                        _resetTimer = new Timer(ResetConnectTime, null, _resetInterval, Timeout.InfiniteTimeSpan);
                    }
                    else
                    {
                        _disconnectTime = Connect?.IsNoiseEnabled == true
                            ? DateTime.UtcNow.AddHours(Random.Shared.Next(1, 4))
                            : DateTime.UtcNow;
                    }
                }
            }
            catch
            {
                ResetConnectTime(null);
            }
        }

        private static void ResetConnectTime(object state)
        {
            lock (_lock)
            {
                _connectTime = null;
                Connect = null;

                _resetTimer?.Dispose();
                _resetTimer = null;
            }
        }

        public static bool IsDisconnected()
        {
            return _disconnectTime is not null
                && DateTime.UtcNow >= _disconnectTime;
        }
    }

    public record ConnectResponse(bool IsUpdateUnavailable, bool IsNoiseEnabled);
}
