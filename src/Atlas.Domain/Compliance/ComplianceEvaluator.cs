using Atlas.Domain.Countries;

namespace Atlas.Domain.Compliance;

public enum DocumentComplianceState
{
    Missing = 1,
    Provided = 2,
    ExpiringSoon = 3,
    Expired = 4,
}

/// <summary>One required document's status for a worker.</summary>
public sealed record DocumentComplianceLine(
    DocumentKind Kind,
    string Description,
    bool ExpiryTracked,
    DocumentComplianceState State,
    DateOnly? ExpiresOn);

/// <summary>A worker's compliance rollup: per-requirement lines and an overall verdict.</summary>
public sealed record WorkerComplianceReport(bool IsCompliant, IReadOnlyList<DocumentComplianceLine> Lines);

/// <summary>
/// The compliance rollup as a PURE fold: given a country's required documents, the documents a worker
/// actually holds, and today's date, decide each requirement's state and whether the worker is
/// compliant overall. No I/O, no clock — the caller passes <paramref name="today"/> in, which is why
/// this is a static function and its tests can pin exact verdicts.
/// </summary>
// 🔗 The same stance as QuoteCalculator and PayrollCalculator: statutory/compliance logic is a pure
// function over data. "Which documents does this country require" is the rule pack's job (data + the
// occasional code hook); "does this worker satisfy them today" is this fold's job. Neither touches a
// database — the exercise "rewrite the rollup as SQL" exists precisely to feel why in-memory wins
// here (the required set is tiny and per-country; the join would be N+1 across workers).
public static class ComplianceEvaluator
{
    /// <summary>An expiry within this many days is still compliant, but flagged for renewal.</summary>
    public const int ExpiringSoonWindowDays = 30;

    public static WorkerComplianceReport Evaluate(
        IReadOnlyList<RequiredDocument> required,
        IReadOnlyList<WorkerDocument> held,
        DateOnly today)
    {
        var lines = new List<DocumentComplianceLine>(required.Count);

        foreach (RequiredDocument requirement in required)
        {
            // The latest-expiring held document of this kind, if any — a renewed passport supersedes
            // the lapsed one.
            WorkerDocument? document = held
                .Where(d => d.Kind == requirement.Kind)
                .OrderByDescending(d => d.ExpiresOn ?? DateOnly.MaxValue)
                .FirstOrDefault();

            DocumentComplianceState state = Classify(requirement, document, today);
            lines.Add(new DocumentComplianceLine(
                requirement.Kind, requirement.Description, requirement.ExpiryTracked, state, document?.ExpiresOn));
        }

        // Missing or Expired breaks compliance; ExpiringSoon is a warning, not a failure.
        bool compliant = lines.All(l =>
            l.State is DocumentComplianceState.Provided or DocumentComplianceState.ExpiringSoon);

        return new WorkerComplianceReport(compliant, lines);
    }

    private static DocumentComplianceState Classify(RequiredDocument requirement, WorkerDocument? document, DateOnly today)
    {
        if (document is null)
        {
            return DocumentComplianceState.Missing;
        }

        if (!requirement.ExpiryTracked || document.ExpiresOn is not { } expiry)
        {
            return DocumentComplianceState.Provided;
        }

        if (expiry < today)
        {
            return DocumentComplianceState.Expired;
        }

        return expiry <= today.AddDays(ExpiringSoonWindowDays)
            ? DocumentComplianceState.ExpiringSoon
            : DocumentComplianceState.Provided;
    }
}
