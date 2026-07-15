namespace Atlas.Domain.Approvals;

/// <summary>
/// Who decided an approval, when, and (optionally) why. The decision RECORD the S06 leave flow
/// carried as three inline fields (decider + timestamp) and the expense flow reuses — plus the
/// <see cref="Reason"/> a denial can attach. Application-owned facts snapshotted onto the
/// aggregate; this record is the shared shape they read back through.
/// </summary>
// WHY a reason on the record but not a reason on Approve/Deny in leave: leave never captured
// one (S06), so leave's public surface is unchanged — it simply produces a decision whose
// Reason is null. Expenses DO capture a denial reason. Extracting the record now means the day
// leave wants reasons, the shape already exists; today's duplication became tomorrow's spec.
public sealed record ApprovalDecision(Guid DecidedByUserId, DateTimeOffset DecidedAtUtc, string? Reason);
