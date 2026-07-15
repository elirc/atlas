using Atlas.Domain.Countries;

namespace Atlas.Domain.Quotes;

/// <summary>
/// A frozen total-cost-of-employment estimate for one country and gross salary, owned by
/// one client org. A quote is a SNAPSHOT: it stores the numbers that were true when it
/// was created and never recomputes them — rates change, packs get promoted, FX moves,
/// and last week's quote must still say exactly what it said last week.
/// </summary>
// 🔗 "Quotes estimate; payroll executes" (S7). This immutability is why the two can
// diverge safely: a quote is a promise made on a date, not a live calculation. If you
// ever feel tempted to add a Recalculate() method here, you are turning a contract into
// a spreadsheet — write a NEW quote instead.
public sealed class Quote
{
    private readonly List<QuoteLine> _lines = [];

    public Guid Id { get; }
    public Guid OrgId { get; }
    public CountryCode Country { get; }

    /// <summary>Gross annual salary, in the country's local currency.</summary>
    public Money Gross { get; }

    /// <summary>Employer total (gross + burden + fee), in local currency.</summary>
    public Money TotalEmployerCost { get; }

    /// <summary>The employer total converted to the org's display currency, at the FX
    /// rate in effect when the quote was created. Part of the snapshot, on purpose.</summary>
    public Money DisplayTotal { get; }

    public Guid CreatedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<QuoteLine> Lines => _lines;

    private Quote(
        Guid id,
        Guid orgId,
        CountryCode country,
        Money gross,
        Money totalEmployerCost,
        Money displayTotal,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        Country = country;
        Gross = gross;
        TotalEmployerCost = totalEmployerCost;
        DisplayTotal = displayTotal;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Freeze a computed breakdown into a durable quote. The lines are copied out of the
    /// pure breakdown into owned <see cref="QuoteLine"/> rows; nothing shares a reference
    /// with the calculator's transient output.
    /// </summary>
    public static Quote FromBreakdown(
        Guid orgId,
        CountryCode country,
        QuoteBreakdown breakdown,
        Money displayTotal,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("A quote must belong to an org.");
        }

        var quote = new Quote(
            Guid.NewGuid(), orgId, country, breakdown.Gross, breakdown.TotalEmployerCost,
            displayTotal, createdByUserId, createdAtUtc);

        foreach (QuoteLineItem item in breakdown.Lines)
        {
            quote._lines.Add(QuoteLine.FromItem(item));
        }

        return quote;
    }

    // WHY: EF Core materialization only. Owned Money values and the _lines collection are
    // populated by EF; the public factory remains the only door for new quotes.
    private Quote()
    {
        Gross = null!;
        TotalEmployerCost = null!;
        DisplayTotal = null!;
    }
}
