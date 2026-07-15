namespace Atlas.Domain.Quotes;

/// <summary>What a quote line represents. Persisted as text (see QuoteConfiguration).</summary>
public enum QuoteLineKind
{
    /// <summary>A statutory employer contribution from the country's rule pack.</summary>
    EmployerBurden,

    /// <summary>Atlas's own management fee.</summary>
    ManagementFee,
}

/// <summary>One priced line in a cost breakdown — a pure calculation result, not an
/// entity. The persisted twin is <c>Atlas.Domain.Quotes.QuoteLine</c>.</summary>
public sealed record QuoteLineItem(string Label, QuoteLineKind Kind, Money Amount);

/// <summary>
/// The full total-cost-of-employment breakdown for one gross salary: the gross itself,
/// every additive line (burden + fee), and the employer total. Pure output of
/// <see cref="QuoteCalculator"/> — no identity, no persistence, no org.
/// </summary>
// 🔍 Reviewer's rule for this type: "trace one euro." Gross + every line's Amount must
// equal TotalEmployerCost, in the same currency, to the minor unit. If that invariant
// isn't obvious from reading the calculator, the calculator is too clever.
public sealed record QuoteBreakdown(
    Money Gross,
    IReadOnlyList<QuoteLineItem> Lines,
    Money TotalEmployerCost);
