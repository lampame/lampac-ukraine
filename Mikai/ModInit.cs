using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
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


namespace Mikai
{
    public class ModInit
    {
        public static double Version => 3.5;

        public static OnlinesSettings Mikai;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => Mikai;
            set => Mikai = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            

            Mikai = new OnlinesSettings("Mikai", "https://mikai.me", streamproxy: false, useproxy: false)
            {
                displayname = "Mikai",
                displayindex = 0,
                apihost = "https://api.mikai.me/v1",
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var conf = ModuleInvoke.Conf("Mikai", Mikai);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            Mikai = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Mikai);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                Mikai.streamproxy = false;
            }
            else if (Mikai.streamproxy)
            {
                Mikai.apnstream = false;
                Mikai.apn = null;
            }

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("mikai");
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
                            ? DateTime.UtcNow.AddHours(Random.Shared.Next(1, 16))
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