using Atlas.Application.Abstractions;
using Atlas.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Atlas.Infrastructure.Caching;

/// <summary>
/// Caches FX conversions (ledger entry #2). Decorates <see cref="IFxRates"/>. Memoizing is trivially
/// correct because <c>Convert</c> is a PURE function of (amount, source, target) for a given rate
/// table — the same inputs always yield the same <see cref="Money"/>.
/// </summary>
// 🔗 SHIPPED NOW, LOAD-BEARING LATER. Today's IFxRates is a static in-code table (S7's FxRateTable),
// so this cache saves a dictionary lookup — nothing. It ships anyway because S12 replaces that table
// with a LIVE vendor: an HTTP call, a latency budget, a rate limit. The moment that lands, memoizing
// repeated conversions (a cost report converting a hundred identical salaries) is real money and real
// milliseconds — and the invalidation contract has to already exist, because a cache bolted on under
// load without one is how stale rates quietly misprice a month of quotes. We write the contract
// before the cache matters, not after it burns us. Wiring is handed to the S12 FX swap (NOTES.md);
// ADR-0017 records both ledger entries and this deferral.
internal sealed class CachingFxRates(IFxRates inner, IMemoryCache cache) : IFxRates
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    // A single change token behind every cached rate — cancelling it evicts them ALL at once, which
    // is what "the rate table refreshed" means. This is the idiomatic group-invalidation over
    // IMemoryCache (no reliance on a concrete-type Clear()).
    private volatile CancellationTokenSource _generation = new();

    public Money Convert(Money amount, CurrencyCode target)
    {
        string key = $"fx:{amount.Currency.Value}:{target.Value}:{amount.Amount}";
        if (cache.TryGetValue(key, out Money? hit) && hit is not null)
        {
            return hit;
        }

        Money converted = inner.Convert(amount, target);

        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl };
        options.AddExpirationToken(new CancellationChangeToken(_generation.Token));
        cache.Set(key, converted, options);
        return converted;
    }

    /// <summary>Evict every cached rate. The invalidation trigger is a rate refresh from the source.</summary>
    // ⚠️ DELETING THIS METHOD'S CALL SITE DOES NOT FAIL A TEST — IT FAILS A MONTH OF QUOTES. A stale
    // FX rate misprices silently: no exception, no red build, just books that don't reconcile weeks
    // later. That is the whole danger of caching, and the reason the trigger is mandatory, not
    // optional. The S14 exercise has you delete the refresh's call to this and trace the blast radius.
    public void Invalidate()
    {
        CancellationTokenSource previous = Interlocked.Exchange(ref _generation, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }
}
