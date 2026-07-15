using System.Reflection;

namespace Atlas.Domain.Countries;

/// <summary>
/// The registry of country rule packs. It DISCOVERS every <see cref="ICountryRulePack"/> in this
/// assembly by reflection — so adding a country is adding a pack file, and this class never changes.
/// That is the open-closed principle with a receipt: the "add two countries" PR touches only new
/// files under Countries/Packs, and this registry is not one of them.
/// </summary>
// ⚠️ Assembly scanning is DISCOVERY MAGIC, and magic has a cost worth naming (ADR-0013). The trade
// we accepted: a new pack "just works" with no registration edit (great for the thesis), but the
// failure modes are quieter than explicit registration — a pack with no public parameterless ctor is
// silently skipped, and two packs claiming the same country would collide at startup. We defend the
// second with an explicit duplicate check below (fail loud at first touch, not mysteriously later);
// the first is covered by a fitness test that asserts every ICountryRulePack type is discoverable.
public static class CountryRulePacks
{
    private static readonly IReadOnlyDictionary<string, ICountryRulePack> Packs = Discover();

    /// <summary>The pack for a country, or a DomainRuleException (422) if unsupported.</summary>
    public static ICountryRulePack For(CountryCode country) =>
        Packs.TryGetValue(country.Value, out ICountryRulePack? pack)
            ? pack
            : throw new DomainRuleException($"Atlas has no rule pack for '{country}' yet.");

    /// <summary>Non-throwing lookup — used by the legacy probation shim.</summary>
    public static bool TryFor(CountryCode country, out ICountryRulePack pack)
    {
        bool found = Packs.TryGetValue(country.Value, out ICountryRulePack? value);
        pack = value!;
        return found;
    }

    public static IReadOnlyCollection<CountryCode> SupportedCountries() =>
        [.. Packs.Values.Select(p => p.Country).OrderBy(c => c.Value)];

    private static IReadOnlyDictionary<string, ICountryRulePack> Discover()
    {
        var packs = new Dictionary<string, ICountryRulePack>();

        foreach (Type type in typeof(ICountryRulePack).Assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false }
                || !typeof(ICountryRulePack).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            var pack = (ICountryRulePack)Activator.CreateInstance(type)!;
            if (!packs.TryAdd(pack.Country.Value, pack))
            {
                // Two packs for one country is a build-time authoring bug — fail loud at first touch.
                throw new InvalidOperationException(
                    $"Duplicate country rule pack for '{pack.Country}': " +
                    $"{packs[pack.Country.Value].GetType().Name} and {type.Name}.");
            }
        }

        return packs;
    }

    /// <summary>Every discoverable pack — for the fitness test that asserts discovery works.</summary>
    public static IReadOnlyCollection<ICountryRulePack> All() => [.. Packs.Values];
}
