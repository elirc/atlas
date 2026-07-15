// WHY: Money and CurrencyCode live in the ROOT Atlas.Domain namespace, not in an
// Atlas.Domain.Money namespace. A type named `Money` inside a namespace also named
// `Money` collides for every sibling Atlas.Domain.* file (the simple name `Money`
// resolves to the namespace, not the type). Rooting them here — beside
// DomainRuleException — makes both visible to every aggregate with no `using` and
// no collision. See docs/concepts/value-objects.md.
namespace Atlas.Domain;

/// <summary>
/// ISO 4217 alpha-3 currency code (e.g. EUR, GBP, PHP, USD). The sibling of
/// CountryCode, and the type that finally replaces the plain `string Currency`
/// promised in Sprint 01 (see Country.Currency's comment).
/// </summary>
// LEARN: this is CountryCode's twin on purpose. Same shape (3 upper ASCII letters),
// same "verify once at the boundary" stance, same value-equality. A currency is part
// of a Money's IDENTITY, not a label on it — which is why it earns a real type and
// why "€10 == $10" never compiles.
public readonly record struct CurrencyCode
{
    public string Value { get; }

    private CurrencyCode(string value) => Value = value;

    public static CurrencyCode From(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainRuleException("Currency code is required.");
        }

        string candidate = raw.Trim().ToUpperInvariant();

        // WHY: shape-only validation, exactly like CountryCode. We do NOT check the
        // code against a table of "real" currencies here — that is a different kind of
        // truth (is this a currency Atlas SUPPORTS?), and it belongs to the rule packs
        // and the FX table, not to the primitive. A value object guards SHAPE.
        if (candidate.Length != 3 || !candidate.All(char.IsAsciiLetterUpper))
        {
            throw new DomainRuleException($"'{raw}' is not an ISO 4217 currency code.");
        }

        return new CurrencyCode(candidate);
    }

    public override string ToString() => Value;
}
