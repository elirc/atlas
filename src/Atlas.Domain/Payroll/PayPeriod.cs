namespace Atlas.Domain.Payroll;

/// <summary>
/// A closed calendar interval a payroll run covers: the first and last day of the
/// period, inclusive on both ends. Business time, so <see cref="DateOnly"/> — a period
/// is "March 2026", true in every timezone, not an instant with a fictitious clock time.
/// </summary>
// LEARN: PeriodEnd is INCLUSIVE (the last paid day), not exclusive (the first unpaid
// day). Half-open intervals are the safer default for durations in general, but a
// payslip says "1–31 March", and matching the domain's own language beats matching a
// convention nobody in payroll uses. The choice is written down here so nobody has to
// re-derive it from the arithmetic. PeriodStart is the anchor the natural key
// (PayCycleId, PeriodStart) is built on — see PayrollRun.
public readonly record struct PayPeriod(DateOnly Start, DateOnly End)
{
    /// <summary>
    /// The calendar-month period for <paramref name="year"/>/<paramref name="month"/>:
    /// the 1st through the last day, handling leap Februarys and 30/31-day months.
    /// </summary>
    public static PayPeriod ForMonth(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new DomainRuleException($"Month must be between 1 and 12 (was {month}).");
        }

        var start = new DateOnly(year, month, 1);

        // WHY: last day = first-of-next-month minus one day. Never hard-code 28/30/31 —
        // February and the calendar's own edge cases are exactly where hand-rolled
        // day-count tables go wrong. Let the date type own the calendar.
        DateOnly end = start.AddMonths(1).AddDays(-1);

        return new PayPeriod(start, end);
    }

    /// <summary>Zero-based index of this period within its calendar year (Jan = 0 … Dec = 11).</summary>
    // WHY: the calculator allocates annual statutory figures across the year's periods so
    // they sum EXACTLY (largest-remainder). "Which period is this?" is that allocation's
    // index — for Monthly it is the calendar month, zero-based.
    public int IndexInYear => Start.Month - 1;
}
