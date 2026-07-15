namespace Atlas.Domain.Quotes;

/// <summary>
/// One persisted line of a <see cref="Quote"/>'s breakdown. Owned by the quote (it has
/// no life of its own) and immutable — a line is snapshotted at creation and never
/// edited, because the quote it belongs to never recalculates.
/// </summary>
// LEARN: this is the durable twin of the pure QuoteLineItem. Two types, on purpose: the
// calculator's output is a value with no identity; the stored row needs a key and an EF
// constructor. Keeping them separate stops persistence concerns leaking into the engine.
public sealed class QuoteLine
{
    public Guid Id { get; }
    public string Label { get; }
    public QuoteLineKind Kind { get; }
    public Money Amount { get; }

    private QuoteLine(Guid id, string label, QuoteLineKind kind, Money amount)
    {
        Id = id;
        Label = label;
        Kind = kind;
        Amount = amount;
    }

    internal static QuoteLine FromItem(QuoteLineItem item) =>
        new(Guid.NewGuid(), item.Label, item.Kind, item.Amount);

    // WHY: EF Core materialization only. Amount is an owned value object, rehydrated by
    // EF via the OwnsOne mapping — hence null! here, mirroring the string-field pattern.
    private QuoteLine()
    {
        Label = null!;
        Amount = null!;
    }
}
