namespace Atlas.Domain.Countries;

/// <summary>
/// A country Atlas can employ in. Reference data: seeded, read-only through the API.
/// </summary>
public sealed class Country
{
    public CountryCode Code { get; }
    public string Name { get; }

    // WHY: currency stays a plain ISO 4217 string for now. It becomes load-bearing in
    // Sprint 04 when Money arrives — at which point it gets a real type. Watch that
    // promotion: it's the value-object lifecycle happening in slow motion.
    public string Currency { get; }

    public Country(CountryCode code, string name, string currency)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("Country name is required.");
        }

        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new DomainRuleException($"'{currency}' is not an ISO 4217 currency code.");
        }

        Code = code;
        Name = name.Trim();
        Currency = currency;
    }

    // WHY: EF Core materialization only. Private, parameterless, and invisible to
    // every caller that matters — the public constructor remains the single door
    // through which a valid Country can exist.
    private Country()
    {
        Name = null!;
        Currency = null!;
    }
}
