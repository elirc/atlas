namespace Atlas.Domain.Leave;

/// <summary>
/// Pure leave-balance math: how many days a worker has AVAILABLE on a given calendar day,
/// given a policy, a hire date, and days already spent. No clock, no storage, no I/O — a
/// fold over the calendar, exactly like <c>EmploymentTerms.Resolve</c> is a fold over
/// history. The balance is DERIVED, never stored (ADR-0007): there is no column to drift.
/// </summary>
// 📘 The policy CHOICES this engine encodes are written down, because none of them is a
// default (see docs/concepts/date-math.md):
//   • Accrual posts at the START of each month; the hire month is prorated.
//   • Proration day-count is ACTUAL days in that month (no 30-day fiction).
//   • Carryover cap is applied cap-then-accrue at each leave-year boundary.
//   • Spend is subtracted after the accrual fold (a documented simplification — see
//     BalanceOn — deferred until a customer's policy makes per-year spend material).
public static class LeaveAccrual
{
    /// <summary>
    /// Days accrued from <paramref name="hiredOn"/> through <paramref name="asOf"/>
    /// (inclusive), with the carryover cap applied at every leave-year boundary. Ignores
    /// spending — <see cref="BalanceOn"/> subtracts that.
    /// </summary>
    public static decimal AccruedThrough(LeavePolicy policy, DateOnly hiredOn, DateOnly asOf)
    {
        if (asOf < hiredOn)
        {
            return 0m;
        }

        decimal monthlyRate = policy.AnnualEntitlementDays / 12m;
        decimal carried = 0m;

        // LEARN: the fold walks leave-year by leave-year. At every boundary EXCEPT the
        // hire year's own start we CAP the carried balance BEFORE adding the new year's
        // accrual — "cap-then-accrue".
        // ⚠️ The order is a real fork. Cap-then-accrue forfeits stale days first, so a
        // fresh year always has room to earn its full entitlement. Accrue-then-cap would
        // let this year's fresh earnings be clipped by last year's surplus — a different,
        // stingier policy. Both are tested (LeaveAccrualTests); we chose the first and
        // said so, which is the whole point of docs/concepts/date-math.md.
        for (int year = hiredOn.Year; year <= asOf.Year; year++)
        {
            if (year != hiredOn.Year)
            {
                carried = Math.Min(carried, policy.CarryoverCapDays);
            }

            carried += AccruedWithinYear(monthlyRate, hiredOn, year, asOf);
        }

        return carried;
    }

    /// <summary>Available balance: accrued-with-carryover minus days already spent.</summary>
    // WHY: spentDays is the sum of the worker's APPROVED leave — the caller aggregates it
    // from the request ledger (there is no stored balance to reconcile). This subtraction
    // assumes spend draws down the CURRENT leave year; historical per-year spend
    // attribution is a documented deferral (ADR-0007, and an exercise in the sprint).
    public static decimal BalanceOn(
        LeavePolicy policy, DateOnly hiredOn, DateOnly asOf, decimal spentDays) =>
        AccruedThrough(policy, hiredOn, asOf) - spentDays;

    private static decimal AccruedWithinYear(
        decimal monthlyRate, DateOnly hiredOn, int year, DateOnly asOf)
    {
        int firstMonth = year == hiredOn.Year ? hiredOn.Month : 1;
        int lastMonth = year == asOf.Year ? asOf.Month : 12;

        decimal accrued = 0m;
        for (int month = firstMonth; month <= lastMonth; month++)
        {
            // WHY: only the HIRE month is prorated. Someone who starts mid-March earns a
            // slice of March and full months thereafter. The day-count convention is
            // ACTUAL days in the month: starting on the 16th of a 31-day month earns
            // 16/31; of a 30-day month, 15/30. There is no obvious default here — which is
            // exactly why it is written down (concepts/date-math.md), not assumed.
            bool isHireMonth = year == hiredOn.Year && month == hiredOn.Month;
            decimal fraction = isHireMonth ? HireMonthFraction(hiredOn) : 1m;
            accrued += monthlyRate * fraction;
        }

        return accrued;
    }

    private static decimal HireMonthFraction(DateOnly hiredOn)
    {
        // GOTCHA: DateTime.DaysInMonth is a PURE calendar function (no clock read) — it is
        // the correct way to ask "how many days in this month?" and it is NOT the banned
        // ambient-time API. Actual days, not a 30-day approximation, is the convention.
        int daysInMonth = DateTime.DaysInMonth(hiredOn.Year, hiredOn.Month);
        int daysWorked = daysInMonth - hiredOn.Day + 1;
        return (decimal)daysWorked / daysInMonth;
    }
}
