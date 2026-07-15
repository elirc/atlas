using Atlas.Domain.Countries;

namespace Atlas.Domain.Workforce;

/// <summary>
/// Country employment rules, v1 → v1.1: probation windows. Sprint 03 shipped this as a
/// standalone data table; Sprint 04 moved the data INTO <see cref="CountryRulePack"/>
/// and this class became a thin shim over the pack registry.
/// </summary>
// LEARN: this is what "promote data without breaking callers" looks like in practice.
// Worker.ProbationEndsOn still calls ProbationMonthsFor(country) exactly as before —
// the method kept its signature and its "unknown country → 3 months" fallback. Only the
// source of truth moved. The next promotion (S11, packs → plugins) can do the same trick.
public static class CountryEmploymentRules
{
    // WHY: 3 months is the neutral default for a country we don't have a pack for yet.
    // It preserves the S03 behaviour (Worker can be hired into any well-formed country
    // code, priced later) rather than throwing here.
    private const int DefaultProbationMonths = 3;

    public static int ProbationMonthsFor(CountryCode country) =>
        CountryRulePacks.TryFor(country, out ICountryRulePack pack)
            ? pack.ProbationMonths
            : DefaultProbationMonths;
}
