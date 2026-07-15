namespace Atlas.Domain.Workforce;

/// <summary>Pure resolution: which record governs a given calendar day?</summary>
public static class EmploymentTerms
{
    // LEARN: "terms as of" is a FOLD over history, not a column. The record with
    // the latest EffectiveFrom that is <= asOf wins; order of arrival is
    // irrelevant (the tests shuffle input to prove it). This one function is why
    // retroactive raises, backdated corrections, and "what did we pay in March?"
    // all fall out for free.
    public static EmploymentRecord? Resolve(
        IEnumerable<EmploymentRecord> history,
        DateOnly asOf)
    {
        return history
            .Where(r => r.EffectiveFrom <= asOf)
            .OrderByDescending(r => r.EffectiveFrom)
            // WHY: the tiebreak matters even though the DB forbids duplicate
            // EffectiveFrom per worker — Resolve is pure and must be total over
            // ANY input, including in-memory lists tests construct.
            .ThenByDescending(r => r.CreatedAtUtc)
            .FirstOrDefault();
    }
}
