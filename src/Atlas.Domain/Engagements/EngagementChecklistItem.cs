namespace Atlas.Domain.Engagements;

/// <summary>
/// One onboarding task on an engagement's checklist. Owned by the aggregate — it
/// has no life of its own and is never loaded or mutated except through its
/// <see cref="Engagement"/>.
/// </summary>
// LEARN: this is an OWNED entity, not an aggregate root. There is no
// EngagementChecklistItem repository and no endpoint that addresses one directly;
// completion goes through the parent (`engagement.CompleteOnboardingItem(...)`),
// which is what lets the parent enforce "all items done before activate" as a
// single invariant instead of a query the caller might forget.
public sealed class EngagementChecklistItem
{
    public const int MaxNameLength = 80;

    public Guid Id { get; }

    /// <summary>Stable slug, e.g. "contract-signed" — also the URL segment that completes it.</summary>
    public string Name { get; }

    /// <summary>When the item was completed, or null while outstanding.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Convenience read; NOT stored (see EngagementConfiguration's Ignore).</summary>
    public bool IsComplete => CompletedAtUtc is not null;

    private EngagementChecklistItem(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    internal static EngagementChecklistItem Pending(string name) =>
        new(Guid.NewGuid(), name);

    // WHY: idempotent by construction. Marking an already-complete item does not
    // move its timestamp — a retried "complete" request is a no-op here, and the
    // use case reads the return value to avoid writing a second activity row.
    internal bool MarkComplete(DateTimeOffset now)
    {
        if (IsComplete)
        {
            return false;
        }

        CompletedAtUtc = now;
        return true;
    }

    // WHY: EF Core materialization only.
    private EngagementChecklistItem()
    {
        Name = null!;
    }
}
