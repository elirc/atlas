namespace Atlas.Domain.Countries;

/// <summary>
/// ISO 3166-1 alpha-3 country code (e.g. DEU, PHL, USA). Atlas's first value object.
/// </summary>
// LEARN: primitive obsession is the gateway bug. A `string country` accepts "Germany",
// "de", "🇩🇪" and ""; a CountryCode accepts exactly one shape, verified once, at the
// boundary — every function that receives one afterwards can stop checking.
// See docs/concepts/fitness-functions.md's cousin: types are fitness functions too.
public readonly record struct CountryCode
{
    public string Value { get; }

    private CountryCode(string value) => Value = value;

    public static CountryCode From(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainRuleException("Country code is required.");
        }

        string candidate = raw.Trim().ToUpperInvariant();

        // WHY: alpha-3, not alpha-2 — payroll and compliance sources overwhelmingly
        // key on alpha-3, and the extra letter removes the AT/Austria-vs-Australia
        // class of eyeball error.
        if (candidate.Length != 3 || !candidate.All(char.IsAsciiLetterUpper))
        {
            throw new DomainRuleException($"'{raw}' is not an ISO 3166-1 alpha-3 country code.");
        }

        return new CountryCode(candidate);
    }

    public override string ToString() => Value;
}
