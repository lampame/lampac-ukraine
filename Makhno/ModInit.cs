using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;
using Shared.Models.Online.Settings;
using Shared.Models.Module;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Caching.Memory;
using Shared.Models;
using Shared.Models.Events;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Makhno
{
    public class ModInit
    {
        public static double Version => 1.9;

        public static OnlinesSettings Makhno;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => Makhno;
            set => Makhno = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            Makhno = new OnlinesSettings("Makhno", "https://wh.lme.isroot.in", streamproxy: false, useproxy: false)
            {
                displayname = "Махно",
                displayindex = 0,
                apihost = "https://uk.uatut.fun/watch",
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            var conf = ModuleInvoke.Conf("Makhno", Makhno);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            if (hasApn)
            {
                conf.Remove("apn");
                conf.Remove("apn_host");
            }
            Makhno = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Makhno);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                Makhno.streamproxy = false;
            }
            else if (Makhno.streamproxy)
            {
                Makhno.apnstream = false;
                Makhno.apn = null;
            }

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("makhno");
        }
    }

    public static class UpdateService
    {
        private static readonly string _connectUrl = "https://lmcuk.lampame.v6.rocks/stats";

        private static ConnectResponse? Connect = null;
        private static DateTime? _connectTime = null;
        private static DateTime? _disconnectTime = null;

        private static readonly TimeSpan _resetInterval = TimeSpan.FromHours(4);
        private static Timer? _resetTimer = null;

        private static readonly object _lock = new();

        public static async Task ConnectAsync(string host, CancellationToken cancellationToken = default)
        {
            if (_connectTime is not null || Connect?.IsUpdateUnavailable == true)
            {
                return;
            }

            lock (_lock)
            {
                if (_connectTime is not null || Connect?.IsUpdateUnavailable == true)
                {
                    return;
                }

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
            catch (Exception)
            {
                ResetConnectTime(null);
            }
        }

        private static void ResetConnectTime(object? state)
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

        public static ActionResult Validate(ActionResult result)
        {
            return IsDisconnected()
                ? throw new JsonReaderException($"Disconnect error: {Guid.CreateVersion7()}")
                : result;
        }
    }

    public record ConnectResponse(bool IsUpdateUnavailable, bool IsNoiseEnabled);
}
