using System.Reflection;
using Atlas.Domain.Countries;
using Xunit;

namespace Atlas.Domain.Tests.Countries;

/// <summary>
/// The fitness function that guards assembly-scan discovery — the quiet failure mode of "discovery
/// magic". If a pack ever ships without a public parameterless constructor, or the registry stops
/// finding it, THIS test goes red instead of the country silently vanishing at runtime.
/// </summary>
public class CountryRulePackDiscoveryTests
{
    [Theory]
    [InlineData("DEU")]
    [InlineData("GBR")]
    [InlineData("PHL")]
    [InlineData("USA")]
    [InlineData("JPN")] // added in S11 as a pack file only
    [InlineData("CAN")] // added in S11 as a pack file only
    public void Each_country_is_discovered(string code)
    {
        Assert.True(CountryRulePacks.TryFor(CountryCode.From(code), out _));
    }

    [Fact]
    public void All_six_countries_are_supported()
    {
        var supported = CountryRulePacks.SupportedCountries().Select(c => c.Value).ToHashSet();

        Assert.Superset(new HashSet<string> { "DEU", "GBR", "PHL", "USA", "JPN", "CAN" }, supported);
    }

    [Fact]
    public void Every_concrete_rule_pack_type_is_discoverable()
    {
        // Reflection over the domain assembly: every non-abstract ICountryRulePack must be reachable
        // through the registry. A pack that compiles but can't be constructed (no parameterless ctor)
        // fails HERE, loudly, not as a mysterious "unsupported country" in production.
        var concretePackTypes = typeof(ICountryRulePack).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICountryRulePack).IsAssignableFrom(t))
            .ToList();

        var discoveredTypes = CountryRulePacks.All().Select(p => p.GetType()).ToHashSet();

        foreach (Type type in concretePackTypes)
        {
            Assert.True(
                type.GetConstructor(Type.EmptyTypes) is not null,
                $"{type.Name} has no public parameterless constructor and will be silently skipped by discovery.");
            Assert.Contains(type, discoveredTypes);
        }
    }
}
