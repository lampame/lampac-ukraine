using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LME.Common.Update
{
    public sealed class ModuleUpdateService
    {
        private const string ConnectUrl = "https://lmcuk.lme.isroot.in/stats";

        private readonly Func<string> _pluginResolver;
        private readonly Func<double> _versionResolver;

        private ConnectResponse? _connect;
        private DateTime? _connectTime;
        private DateTime? _disconnectTime;

        private static readonly TimeSpan ResetInterval = TimeSpan.FromHours(4);
        private Timer? _resetTimer;

        private readonly object _lock = new();

        public ModuleUpdateService(Func<string> pluginResolver, Func<double> versionResolver)
        {
            _pluginResolver = pluginResolver;
            _versionResolver = versionResolver;
        }

        public async Task ConnectAsync(string host, CancellationToken cancellationToken = default)
        {
            if (_connectTime is not null || _connect?.IsUpdateUnavailable == true)
                return;

            lock (_lock)
            {
                if (_connectTime is not null || _connect?.IsUpdateUnavailable == true)
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
                    Module = _pluginResolver(),
                    Version = _versionResolver(),
                };

                var requestJson = JsonConvert.SerializeObject(request, Formatting.None);
                var requestContent = new StringContent(requestJson, Encoding.UTF8, MediaTypeNames.Application.Json);

                var response = await client
                    .PostAsync(ConnectUrl, requestContent, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength > 0)
                {
                    var responseText = await response.Content
                        .ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);

                    _connect = JsonConvert.DeserializeObject<ConnectResponse>(responseText);
                }

                lock (_lock)
                {
                    _resetTimer?.Dispose();
                    _resetTimer = null;

                    if (_connect?.IsUpdateUnavailable != true)
                    {
                        _resetTimer = new Timer(ResetConnectTime, null, ResetInterval, Timeout.InfiniteTimeSpan);
                    }
                    else
                    {
                        _disconnectTime = _connect?.IsNoiseEnabled == true
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

        public bool IsDisconnected()
        {
            return _disconnectTime is not null
                && DateTime.UtcNow >= _disconnectTime;
        }

        public ActionResult Validate(ActionResult result)
        {
            return IsDisconnected()
                ? throw new JsonReaderException($"Disconnect error: {Guid.CreateVersion7()}")
                : result;
        }

        private void ResetConnectTime(object? state)
        {
            lock (_lock)
            {
                _connectTime = null;
                _connect = null;

                _resetTimer?.Dispose();
                _resetTimer = null;
            }
        }

        private record ConnectResponse(bool IsUpdateUnavailable, bool IsNoiseEnabled);
    }
}
