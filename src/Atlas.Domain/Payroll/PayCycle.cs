using Atlas.Domain.Countries;

namespace Atlas.Domain.Payroll;

/// <summary>
/// A recurring payroll schedule for one org in one country at one cadence. The cycle is
/// the durable schedule; each <see cref="PayrollRun"/> is one occurrence of it for a
/// specific period. "Run March against the German monthly cycle" is the shape of the
/// product.
/// </summary>
// 📘 Why a cycle AND a run, not just a run: the cycle is the identity that makes runs
// idempotent. The natural key of a run is (PayCycleId, PeriodStart) — so "prepare March"
// twice against the same cycle is the same run, by construction. Without a stable cycle
// to hang the period on, idempotency would key on (org, country, frequency, period),
// four columns that can drift; one foreign key can't.
public sealed class PayCycle
{
    public Guid Id { get; }
    public Guid OrgId { get; }
    public CountryCode Country { get; }
    public PayFrequency Frequency { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    private PayCycle(
        Guid id,
        Guid orgId,
        CountryCode country,
        PayFrequency frequency,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        Country = country;
        Frequency = frequency;
        CreatedAtUtc = createdAtUtc;
    }

    public static PayCycle Create(
        Guid orgId,
        CountryCode country,
        PayFrequency frequency,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("A pay cycle must belong to an org.");
        }

        return new PayCycle(Guid.NewGuid(), orgId, country, frequency, createdAtUtc);
    }

    /// <summary>The period this cycle covers for a given calendar year and month.</summary>
    // WHY: the cycle OWNS period generation because the period shape is a function of the
    // frequency. Monthly → the calendar month. When Biweekly arrives, this method grows a
    // switch on Frequency and every caller keeps asking the same question ("what period is
    // 2026/3?") without caring how it's answered.
    public PayPeriod PeriodFor(int year, int month) => Frequency switch
    {
        PayFrequency.Monthly => PayPeriod.ForMonth(year, month),
        _ => throw new DomainRuleException($"Unsupported pay frequency '{Frequency}'."),
    };

    // WHY: EF Core materialization only. Country/Frequency are value types rehydrated by
    // the configured conversions; no null! needed (no reference fields here).
    private PayCycle()
    {
    }
}
