using Atlas.Domain.Countries;

namespace Atlas.Domain.Workforce;

/// <summary>
/// Country employment rules, v1: a data table. Sprint 04 grows this into rule
/// packs (burden components, leave minimums); Sprint 11 promotes packs to plugins.
/// Watch the lifecycle — this static table is deliberately the naive first step.
/// </summary>
public static class CountryEmploymentRules
{
    // WHY: months, not days — statutory probation periods are defined in months
    // in every jurisdiction we model, and "6 months from March 30" has a defined
    // calendar answer (Sept 30) where "182 days" does not.
    private static readonly Dictionary<string, int> ProbationMonths = new()
    {
        ["DEU"] = 6,
        ["GBR"] = 3,
        ["PHL"] = 6,
        ["USA"] = 3,
    };

    public static int ProbationMonthsFor(CountryCode country) =>
        ProbationMonths.TryGetValue(country.Value, out int months) ? months : 3;
}
