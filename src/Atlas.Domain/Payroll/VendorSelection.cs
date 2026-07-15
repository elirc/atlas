namespace Atlas.Domain.Payroll;

/// <summary>Pure resolution: which vendor governs a given calendar day?</summary>
// LEARN: this is EmploymentTerms.Resolve wearing a different noun — deliberately, line for
// line. "Current vendor" is a FOLD over the assignment history, not a column: the record with
// the latest EffectiveFrom that is <= asOf wins; order of arrival is irrelevant (the tests
// shuffle input to prove it). When history is empty, or no record is yet effective, the
// fallback governs — every org starts on AtlasNative and STAYS there until an assignment says
// otherwise, so an org that never opts into LedgerPay never even queries the vendor directory
// for anything but its default.
public static class VendorSelection
{
    /// <summary>
    /// The vendor in effect on <paramref name="asOf"/>, or <paramref name="fallback"/> when
    /// no assignment is yet effective. Total over ANY input, including the empty history.
    /// </summary>
    public static PayrollVendor Resolve(
        IEnumerable<VendorAssignment> history,
        DateOnly asOf,
        PayrollVendor fallback)
    {
        VendorAssignment? governing = history
            .Where(a => a.EffectiveFrom <= asOf)
            .OrderByDescending(a => a.EffectiveFrom)
            // WHY the tiebreak: Resolve is pure and must be total over ANY input, including
            // in-memory lists a test builds with two records on the same day. The DB forbids
            // that (unique index on (OrgId, Country, EffectiveFrom)), but the domain function
            // cannot lean on the DB to stay total — latest-created wins, deterministically.
            .ThenByDescending(a => a.CreatedAtUtc)
            .FirstOrDefault();

        return governing?.Vendor ?? fallback;
    }

    /// <summary>
    /// The earliest assignment whose EffectiveFrom is strictly AFTER <paramref name="asOf"/>
    /// — the "pending switch" a shadow run compares against, or null when nothing is queued.
    /// </summary>
    // 🔗 This is the hook the shadow-run policy hangs auto-expiry on (ADR-0014): shadow the
    // vendor we are ABOUT to flip to, right up to the flip date; once the switch is effective
    // there is no pending record, so shadowing stops on its own. "Run both, compare, then
    // flip" is encoded as "there is a pending assignment to compare against."
    public static VendorAssignment? PendingSwitch(
        IEnumerable<VendorAssignment> history,
        DateOnly asOf)
    {
        return history
            .Where(a => a.EffectiveFrom > asOf)
            .OrderBy(a => a.EffectiveFrom)
            .ThenBy(a => a.CreatedAtUtc)
            .FirstOrDefault();
    }
}
