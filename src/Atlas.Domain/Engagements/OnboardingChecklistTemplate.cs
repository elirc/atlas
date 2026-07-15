using Atlas.Domain.Countries;

namespace Atlas.Domain.Engagements;

/// <summary>
/// The onboarding checklist for a new hire, v1: universal items plus a few
/// country-specific ones, as data. Item names are stable slugs — they double as
/// the identifier in <c>POST /engagements/{id}/checklist/{item}/complete</c>.
/// </summary>
// LEARN: this is the same "policy-as-data" call the rule packs make in S4 — the
// country-specific rows (Germany's works-council notice, the Philippines' SSS
// registration) are configuration, not code, so a new country adds a row, never a
// branch. It stays a pure Domain table here because the checklist is BUSINESS
// truth an engagement needs even before persistence exists.
// GOTCHA (integration): S4 grows the country rule packs with an `OnboardingItems`
// extension point. The country-specific half below MIRRORS that point deliberately
// so this file has one obvious home to delegate to once S4 lands — see NOTES.md.
public static class OnboardingChecklistTemplate
{
    /// <summary>Every hire, every country — in the order they are presented.</summary>
    public static readonly IReadOnlyList<string> UniversalItems =
    [
        "contract-signed",
        "id-verified",
        "bank-details",
    ];

    // WHY: keyed by the SAME alpha-3 code CountryCode normalizes to, so lookups are
    // exact and case-proof. Absence means "no extra items" — a country is onboarded
    // with just the universal set until someone adds a row here.
    private static readonly Dictionary<string, string[]> CountrySpecificItems = new()
    {
        ["DEU"] = ["works-council-notification"],
        ["PHL"] = ["sss-registration"],
    };

    /// <summary>The full ordered item list for a country: universal, then country-specific.</summary>
    public static IReadOnlyList<string> For(CountryCode country)
    {
        string[] extra = CountrySpecificItems.TryGetValue(country.Value, out string[]? items)
            ? items
            : [];

        return [.. UniversalItems, .. extra];
    }
}
