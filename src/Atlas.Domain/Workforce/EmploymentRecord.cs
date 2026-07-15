namespace Atlas.Domain.Workforce;

/// <summary>
/// One effective-dated statement of a worker's terms. Records are APPENDED,
/// never edited — history is immutable by construction.
/// </summary>
public sealed class EmploymentRecord
{
    public const int MaxTitleLength = 120;

    public Guid Id { get; }
    public Guid WorkerId { get; }

    /// <summary>The first calendar day these terms apply. There is no EffectiveTo —
    /// a record ends where the next one begins (see ADR-0004).</summary>
    public DateOnly EffectiveFrom { get; }

    public string JobTitle { get; }

    // WHY: decimal + a currency string, for exactly one more sprint. Money (the
    // value object) lands in S4 and these two fields become one. The smell is
    // intentional and scheduled — that is different from accidental.
    public decimal AnnualSalary { get; }
    public string SalaryCurrency { get; }

    public decimal WeeklyHours { get; }

    public Guid CreatedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    private EmploymentRecord(
        Guid id,
        Guid workerId,
        DateOnly effectiveFrom,
        string jobTitle,
        decimal annualSalary,
        string salaryCurrency,
        decimal weeklyHours,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        WorkerId = workerId;
        EffectiveFrom = effectiveFrom;
        JobTitle = jobTitle;
        AnnualSalary = annualSalary;
        SalaryCurrency = salaryCurrency;
        WeeklyHours = weeklyHours;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public static EmploymentRecord Create(
        Guid workerId,
        DateOnly effectiveFrom,
        string? jobTitle,
        decimal annualSalary,
        string salaryCurrency,
        decimal weeklyHours,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        string title = (jobTitle ?? string.Empty).Trim();
        if (title.Length is 0 or > MaxTitleLength)
        {
            throw new DomainRuleException(
                $"Job title is required (max {MaxTitleLength} characters).");
        }

        if (annualSalary <= 0)
        {
            throw new DomainRuleException("Annual salary must be positive.");
        }

        if (salaryCurrency is not { Length: 3 } || !salaryCurrency.All(char.IsAsciiLetterUpper))
        {
            throw new DomainRuleException($"'{salaryCurrency}' is not an ISO 4217 currency code.");
        }

        if (weeklyHours is <= 0 or > 80)
        {
            throw new DomainRuleException("Weekly hours must be between 1 and 80.");
        }

        return new EmploymentRecord(
            Guid.NewGuid(), workerId, effectiveFrom, title, annualSalary,
            salaryCurrency, weeklyHours, createdByUserId, createdAtUtc);
    }

    // WHY: EF Core materialization only.
    private EmploymentRecord()
    {
        JobTitle = null!;
        SalaryCurrency = null!;
    }
}
