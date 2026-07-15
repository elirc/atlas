namespace Atlas.Domain.Payroll;

/// <summary>
/// One persisted line of a <see cref="Payslip"/> — a deduction, an unpaid-leave charge,
/// or an employer-burden cost. Owned by the payslip (no life of its own) and immutable, a
/// snapshot at creation, because the payslip it belongs to never recomputes.
/// </summary>
// LEARN: the durable twin of the pure PayslipLineItem — same two-type split as S04's
// QuoteLine / QuoteLineItem. The calculator's output is a value with no identity; the
// stored row needs a key and an EF constructor. Keeping them separate stops persistence
// concerns leaking into the engine.
public sealed class PayslipLine
{
    public Guid Id { get; }
    public string Label { get; }
    public PayslipLineKind Kind { get; }
    public Money Amount { get; }

    private PayslipLine(Guid id, string label, PayslipLineKind kind, Money amount)
    {
        Id = id;
        Label = label;
        Kind = kind;
        Amount = amount;
    }

    internal static PayslipLine FromItem(PayslipLineItem item) =>
        new(Guid.NewGuid(), item.Label, item.Kind, item.Amount);

    // WHY: EF Core materialization only. Amount is an owned value object, rehydrated by
    // EF via the OwnsOne mapping — hence null! here, mirroring the string-field pattern.
    private PayslipLine()
    {
        Label = null!;
        Amount = null!;
    }
}
