using Atlas.Domain.Access;
using Atlas.Domain.Countries;

namespace Atlas.Domain.Invoicing;

/// <summary>
/// A person or entity an org engages on a contract (not employed via Atlas EOR). Deliberately
/// lightweight: identity, where they are, and the currency Atlas pays them in. Everything
/// heavier — tax forms, KYC, banking detail — is out of course scope (SPEC §8).
/// </summary>
// LEARN: a Contractor is NOT a Worker with a different Role. A Worker has effective-dated
// employment terms, a country rule pack, a payroll seat; a contractor has an invoice and a
// payout currency. Folding them into one type to save a table would force every Worker query
// to carry "…and this isn't actually an employee" — modelling distinct things distinctly is
// cheaper than the conditionals that pretending-they're-the-same demands later. (Compare the
// Role/AtlasOps split in Access/Role.cs.)
public sealed class Contractor
{
    public const int MaxNameLength = 200;

    public Guid Id { get; }
    public Guid OrgId { get; }
    public string Name { get; private set; }
    public EmailAddress Email { get; private set; }
    public CountryCode Country { get; }

    /// <summary>
    /// The currency Atlas disburses to this contractor in — and therefore the ONE currency
    /// every one of their invoices must be denominated in (see ContractorInvoice.Open).
    /// </summary>
    public CurrencyCode PayoutCurrency { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    private Contractor(
        Guid id,
        Guid orgId,
        string name,
        EmailAddress email,
        CountryCode country,
        CurrencyCode payoutCurrency,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        Name = name;
        Email = email;
        Country = country;
        PayoutCurrency = payoutCurrency;
        CreatedAtUtc = createdAtUtc;
    }

    public static Contractor Create(
        Guid orgId,
        string? name,
        EmailAddress email,
        CountryCode country,
        CurrencyCode payoutCurrency,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("A contractor must belong to an org.");
        }

        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw new DomainRuleException(
                $"Contractor name is required (max {MaxNameLength} characters).");
        }

        return new Contractor(Guid.NewGuid(), orgId, trimmed, email, country, payoutCurrency, createdAtUtc);
    }

    // WHY: EF Core materialization only.
    private Contractor()
    {
        Name = null!;
    }
}
