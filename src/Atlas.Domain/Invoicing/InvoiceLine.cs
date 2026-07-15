namespace Atlas.Domain.Invoicing;

/// <summary>
/// One billed item on a contractor invoice: what it was for, and how much. Owned by the
/// <see cref="ContractorInvoice"/> aggregate — it has no life of its own.
/// </summary>
// WHY the amount is a Money, not a decimal: a line without a currency is a number that means
// nothing until someone guesses. Attaching the currency here lets the aggregate reject a
// mixed-currency invoice by comparison, not by convention (see ContractorInvoice.Open).
public sealed class InvoiceLine
{
    public const int MaxDescriptionLength = 200;

    public Guid Id { get; }
    public string Description { get; private set; }
    public Money Amount { get; private set; }

    private InvoiceLine(Guid id, string description, Money amount)
    {
        Id = id;
        Description = description;
        Amount = amount;
    }

    public static InvoiceLine For(string? description, Money amount)
    {
        string trimmed = (description ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxDescriptionLength)
        {
            throw new DomainRuleException(
                $"An invoice line needs a description (max {MaxDescriptionLength} characters).");
        }

        // WHY: a zero or negative line is not a billing item — it is either a mistake or a
        // credit note, and credit notes are a different document with different rules. Fail
        // fast rather than silently disburse a negative "payment".
        if (amount.Amount <= 0m)
        {
            throw new DomainRuleException("An invoice line amount must be positive.");
        }

        return new InvoiceLine(Guid.NewGuid(), trimmed, amount);
    }

    // WHY: EF Core materialization only. Amount (a reference-type value object) is owned by the
    // configuration and populated after construction; null! silences the nullable warning.
    private InvoiceLine()
    {
        Description = null!;
        Amount = null!;
    }
}
