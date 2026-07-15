using Atlas.Application.Abstractions;
using Atlas.Domain;
using Atlas.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace Atlas.Api.Tests.Reporting;

/// <summary>
/// The two cache-ledger entries, each proved to do BOTH halves of the contract: serve from cache
/// (the win) AND drop on invalidation (the safety). A cache that only proved the first half would be
/// the bug this sprint keeps warning about — fast and eventually wrong.
/// </summary>
public class ReferenceCacheTests
{
    [Fact]
    public async Task Supported_countries_are_served_from_cache_after_the_first_read()
    {
        var inner = new CountingCountries();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cached = new CachedSupportedCountries(inner, cache);

        _ = await cached.AllAsync(CancellationToken.None);
        _ = await cached.AllAsync(CancellationToken.None);

        Assert.Equal(1, inner.Calls); // the second read hit the cache, not the database
    }

    [Fact]
    public async Task Invalidating_supported_countries_forces_a_fresh_read()
    {
        var inner = new CountingCountries();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cached = new CachedSupportedCountries(inner, cache);

        _ = await cached.AllAsync(CancellationToken.None);
        cached.Invalidate();
        _ = await cached.AllAsync(CancellationToken.None);

        Assert.Equal(2, inner.Calls); // invalidation dropped the entry; the next read reloaded
    }

    [Fact]
    public void Fx_conversions_are_memoized_per_input()
    {
        var inner = new CountingFx();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cached = new CachingFxRates(inner, cache);

        Money amount = Money.Of(100m, CurrencyCode.From("USD"));
        CurrencyCode eur = CurrencyCode.From("EUR");

        _ = cached.Convert(amount, eur);
        _ = cached.Convert(amount, eur);

        Assert.Equal(1, inner.Calls); // identical inputs are a pure function — computed once
    }

    [Fact]
    public void Invalidating_fx_evicts_every_cached_rate()
    {
        var inner = new CountingFx();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cached = new CachingFxRates(inner, cache);

        Money amount = Money.Of(100m, CurrencyCode.From("USD"));
        CurrencyCode eur = CurrencyCode.From("EUR");

        _ = cached.Convert(amount, eur);
        cached.Invalidate();
        _ = cached.Convert(amount, eur);

        Assert.Equal(2, inner.Calls); // the refresh trigger forced a recompute
    }

    private sealed class CountingCountries : ISupportedCountries
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<SupportedCountry>> AllAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<SupportedCountry>>(
                [new SupportedCountry("USA", "United States", "USD")]);
        }
    }

    private sealed class CountingFx : IFxRates
    {
        public int Calls { get; private set; }

        public Money Convert(Money amount, CurrencyCode target)
        {
            Calls++;
            return Money.Of(amount.Amount, target);
        }
    }
}
