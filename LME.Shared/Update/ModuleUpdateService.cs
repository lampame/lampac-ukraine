using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LME.Common.Update
{
    public sealed class ModuleUpdateService
    {
        private const string ConnectUrl = "https://lmcuk.lme.isroot.in/stats";

        // Перевірка не блокує запит користувача: пінг іде у фоні, тому таймаут короткий.
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

        // Після успішної перевірки повторюємо її не частіше ніж раз на 4 години.
        private static readonly TimeSpan SuccessInterval = TimeSpan.FromHours(4);

        // Якщо сервер не відповів — не достукуємось на кожному запиті, а чекаємо паузу.
        private static readonly TimeSpan FailureInterval = TimeSpan.FromMinutes(15);

        // Один HttpClient на всі перевірки замість створення нового на кожен виклик.
        private static readonly HttpClient _client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }
            };

            return new HttpClient(handler) { Timeout = RequestTimeout };
        }

        private readonly Func<string> _pluginResolver;
        private readonly Func<double> _versionResolver;

        private readonly object _lock = new();

        private ConnectResponse? _connect;
        private DateTime? _nextAttemptTime;
        private DateTime? _disconnectTime;
        private int _inFlight;

        public ModuleUpdateService(Func<string> pluginResolver, Func<double> versionResolver)
        {
            _pluginResolver = pluginResolver;
            _versionResolver = versionResolver;
        }

        public Task ConnectAsync(string host, CancellationToken cancellationToken = default)
        {
            if (!ShouldAttempt())
                return Task.CompletedTask;

            lock (_lock)
            {
                if (!ShouldAttempt() || Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
                    return Task.CompletedTask;
            }

            // Навмисно не чекаємо: результат перевірки не потрібен поточному запиту,
            // а його таймаут не має гальмувати відповідь користувачу.
            _ = CheckAsync(host);
            return Task.CompletedTask;
        }

        private bool ShouldAttempt()
        {
            if (_disconnectTime is not null)
                return false;

            return _nextAttemptTime is null || DateTime.UtcNow >= _nextAttemptTime;
        }

        private async Task CheckAsync(string host)
        {
            try
            {
                var request = new
                {
                    Host = host,
                    Module = _pluginResolver(),
                    Version = _versionResolver(),
                };

                var requestJson = JsonSerializer.Serialize(request);
                using var requestContent = new StringContent(requestJson, Encoding.UTF8, MediaTypeNames.Application.Json);

                // CancellationToken.None: запит користувача вже завершився, його токен тут недійсний.
                using var response = await _client
                    .PostAsync(ConnectUrl, requestContent, CancellationToken.None)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                ConnectResponse? parsed = _connect;

                var responseText = await response.Content
                    .ReadAsStringAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(responseText))
                    parsed = JsonSerializer.Deserialize<ConnectResponse>(responseText);

                lock (_lock)
                {
                    _connect = parsed;

                    if (parsed?.IsUpdateUnavailable == true)
                    {
                        _disconnectTime = parsed.IsNoiseEnabled
                            ? DateTime.UtcNow.AddHours(Random.Shared.Next(1, 4))
                            : DateTime.UtcNow;
                    }
                    else
                    {
                        _nextAttemptTime = DateTime.UtcNow.Add(SuccessInterval);
                    }
                }
            }
            catch
            {
                lock (_lock)
                {
                    _connect = null;
                    _nextAttemptTime = DateTime.UtcNow.Add(FailureInterval);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
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
                ? throw new JsonException($"Disconnect error: {Guid.CreateVersion7()}")
                : result;
        }

        private record ConnectResponse(bool IsUpdateUnavailable, bool IsNoiseEnabled);
    }
}
