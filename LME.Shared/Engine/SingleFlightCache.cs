using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

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
            Func<CancellationToken, Task<T>> factory,
            CancellationToken ct = default)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());
            Interlocked.Increment(ref entry.Waiters);
            try
            {
                await entry.Sem.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return await factory(ct).ConfigureAwait(false);
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
