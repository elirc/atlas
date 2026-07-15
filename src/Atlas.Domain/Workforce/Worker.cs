using Atlas.Domain.Access;
using Atlas.Domain.Countries;

namespace Atlas.Domain.Workforce;

/// <summary>
/// A person Atlas employs on behalf of a client org. The profile is deliberately
/// thin — employment TERMS live in effective-dated records, not here.
/// </summary>
public sealed class Worker
{
    public const int MaxNameLength = 200;

    public Guid Id { get; }
    public Guid OrgId { get; }
    public string FullName { get; private set; }
    public EmailAddress ContactEmail { get; private set; }
    public CountryCode Country { get; }

    // LEARN: HiredOn is a DateOnly — a calendar date in the worker's employment
    // context, not an instant. "Hired on 2026-03-01" is true in every timezone;
    // a DateTimeOffset would force a fictitious time-of-day on it.
    public DateOnly HiredOn { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    private Worker(
        Guid id,
        Guid orgId,
        string fullName,
        EmailAddress contactEmail,
        CountryCode country,
        DateOnly hiredOn,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        FullName = fullName;
        ContactEmail = contactEmail;
        Country = country;
        HiredOn = hiredOn;
        CreatedAtUtc = createdAtUtc;
    }

    public static Worker Hire(
        Guid orgId,
        string? fullName,
        EmailAddress contactEmail,
        CountryCode country,
        DateOnly hiredOn,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("A worker must belong to an org.");
        }

        string name = (fullName ?? string.Empty).Trim();
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new DomainRuleException(
                $"Worker name is required (max {MaxNameLength} characters).");
        }

        return new Worker(Guid.NewGuid(), orgId, name, contactEmail, country, hiredOn, createdAtUtc);
    }

    /// <summary>Last day of the statutory probation window for this worker's country.</summary>
    public DateOnly ProbationEndsOn =>
        HiredOn.AddMonths(CountryEmploymentRules.ProbationMonthsFor(Country));

    /// <summary>Is the worker still in probation on the given calendar date?</summary>
    // WHY: the date is a PARAMETER, not read from an ambient clock. "In probation?"
    // is a question ABOUT a date, so the date is an input — the caller supplies it
    // (the Api derives it once, at the edge, from the injected TimeProvider). This
    // method is now pure and total, and the ambient-time ban keeps it that way.
    // GOTCHA: the edge currently derives the date from UTC, which approximates the
    // worker's local calendar — good enough for a probation flag, but statutory
    // dates that must land in the worker's timezone (notice periods) will take the
    // country's calendar explicitly. Noted in ADR-0007; not this sprint's job.
    public bool IsInProbationOn(DateOnly date) => date < ProbationEndsOn;

    // WHY: EF Core materialization only.
    private Worker()
    {
        FullName = null!;
    }
}
