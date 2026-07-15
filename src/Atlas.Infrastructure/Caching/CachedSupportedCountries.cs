using Atlas.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace Atlas.Infrastructure.Caching;

/// <summary>
/// Caches the supported-country reference list (ledger entry #1). Decorates <see cref="ISupportedCountries"/>
/// so the caller never knows a cache exists — the invalidation story lives entirely in here.
/// </summary>
// ⚠️ EVERY CACHE ENTRY GETS TWO THINGS WRITTEN DOWN, OR IT IS A BUG ON A TIMER:
//   • STALENESS BUDGET: this list is allowed to be out of date for at most one TTL (10 minutes). That
//     is acceptable because the data changes only when Atlas SHIPS a new country — a deploy, not a
//     request (the S11 rule-pack thesis: a country is a code file). A ten-minute-old country list is
//     never wrong in a way that hurts; a ten-minute-old worker count would be.
//   • INVALIDATION TRIGGER: adding a country. There is no such runtime path today (countries are
//     seeded), so the TTL IS the invalidation — the honest statement is "this is refreshed on a timer
//     because its source only changes on deploy". Invalidate() exists for the day a country becomes a
//     runtime write; the ledger (ADR-0017) records that hand-off so it is not forgotten.
// This is safe to cache because it is REFERENCE data — identical for every org. The cache is keyed on
// nothing org-specific for exactly that reason. Per-org data must never reach this class.
internal sealed class CachedSupportedCountries(ISupportedCountries inner, IMemoryCache cache)
    : ISupportedCountries
{
    private const string CacheKey = "reference:supported-countries";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<SupportedCountry>> AllAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<SupportedCountry>? cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlyList<SupportedCountry> fresh = await inner.AllAsync(cancellationToken);
        cache.Set(CacheKey, fresh, Ttl);
        return fresh;
    }

    /// <summary>Drop the cached list. Call this the day "add a country" becomes a runtime operation.</summary>
    public void Invalidate() => cache.Remove(CacheKey);
}
