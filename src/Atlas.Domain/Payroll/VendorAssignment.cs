using Atlas.Domain.Countries;

namespace Atlas.Domain.Payroll;

/// <summary>
/// One effective-dated statement of which payroll-calculation vendor an org uses for one
/// country, from a given calendar day onward. Records are APPENDED, never edited — the
/// switch history is immutable by construction, exactly like <c>EmploymentRecord</c>.
/// </summary>
// 📘 EFFECTIVE DATING, SECOND APPLICATION (S03's pattern paying rent). This is the same
// move as EmploymentRecord: an append-only log carrying an EffectiveFrom (a DateOnly — a
// vendor switch is a calendar decision, "LedgerPay from 1 August", not an instant), NO
// EffectiveTo (a record ends where the next begins — ADR-0004's rejected-alternatives list
// says why a maintained closing column is a race waiting to happen), and "current vendor"
// is a RESOLUTION over history (VendorSelection.Resolve), never a stored column that can
// drift. Retroactive corrections and "which vendor governed in March?" both fall out for
// free — the third time this course proves that dating is a reusable tool, not a trick.
//
// ⚠️ Effective dating answers "which vendor is CURRENT for new work"; it deliberately does
// NOT answer "which vendor does a run already in flight use." That second question is the
// cycle-in-flight rule, and it is answered by PINNING the resolved vendor onto PayrollRun at
// creation time (see PayrollRun.SelectedVendor). The assignment log is the schedule; the run
// is the receipt. Confusing the two is how a mid-quarter switch corrupts an open cycle.
public sealed class VendorAssignment
{
    public Guid Id { get; }
    public Guid OrgId { get; }

    /// <summary>The country this assignment governs — vendor choice is per-org, PER-COUNTRY
    /// (LedgerPay was signed for DEU; an org may keep AtlasNative elsewhere).</summary>
    public CountryCode Country { get; }

    /// <summary>The first calendar day this vendor applies. There is no EffectiveTo — a
    /// record ends where the next one begins (ADR-0004).</summary>
    public DateOnly EffectiveFrom { get; }

    public PayrollVendor Vendor { get; }

    public Guid CreatedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    private VendorAssignment(
        Guid id,
        Guid orgId,
        CountryCode country,
        DateOnly effectiveFrom,
        PayrollVendor vendor,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        Country = country;
        EffectiveFrom = effectiveFrom;
        Vendor = vendor;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public static VendorAssignment Create(
        Guid orgId,
        CountryCode country,
        DateOnly effectiveFrom,
        PayrollVendor vendor,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("A vendor assignment must belong to an org.");
        }

        // WHY validate the enum: PayrollVendor is persisted as text and arrives from a string
        // contract at the edge; an out-of-range cast (a future vendor name a client invents)
        // is a well-formed request the business must refuse, not a silent bad row.
        if (!Enum.IsDefined(vendor))
        {
            throw new DomainRuleException($"'{vendor}' is not a known payroll vendor.");
        }

        return new VendorAssignment(
            Guid.NewGuid(), orgId, country, effectiveFrom, vendor, createdByUserId, createdAtUtc);
    }

    // WHY: EF Core materialization only. Country is a value-type struct (defaults automatically)
    // populated by EF via the value conversion.
    private VendorAssignment()
    {
    }
}
