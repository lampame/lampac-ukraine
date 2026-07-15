using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Shared.Services.Hybrid;

namespace LME.Shared.Engine
{
    /// <summary>
    /// Single-flight cache: гарантує що factory для одного ключа виконується рівно один раз
    /// при паралельних запитах. Замінює паттерн TryGetValue+Set без stampede protection.
    /// ponytail: thin wrapper (~40 рядків), без нових NuGet-залежностей.
    /// </summary>
    public static class SingleFlightCache
    {
        private sealed class Entry
        {
            public readonly SemaphoreSlim Sem = new(1, 1);
            public int Waiters;
        }

        private static readonly ConcurrentDictionary<string, Entry> _entries = new();

        public static async Task<T> GetOrCreateAsync<T>(
            string key,
            IHybridCache hybridCache,
            Func<CancellationToken, Task<T>> factory,
            CancellationToken ct = default,
            int negativeCacheSeconds = 60)
        {
            string negKey = $"neg:{key}";
            if (hybridCache != null && hybridCache.TryGetValue(negKey, out string isNeg) && isNeg == "timeout")
                return default;

            var entry = _entries.GetOrAdd(key, _ => new Entry());
            Interlocked.Increment(ref entry.Waiters);
            try
            {
                await entry.Sem.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (hybridCache != null && hybridCache.TryGetValue(negKey, out string isNegInside) && isNegInside == "timeout")
                        return default;

                    return await factory(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException || (ex.Message != null && ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)))
                {
                    if (hybridCache != null)
                        hybridCache.Set(negKey, "timeout", TimeSpan.FromSeconds(negativeCacheSeconds));
                    throw;
                }
                finally
                {
                    entry.Sem.Release();
                }
            }
            finally
            {
                if (Interlocked.Decrement(ref entry.Waiters) == 0)
                    _entries.TryRemove(key, out _);
            }
        }
    }
}
