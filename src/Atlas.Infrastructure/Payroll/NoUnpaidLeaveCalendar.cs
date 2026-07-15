using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure.Payroll;

/// <summary>
/// Stand-in <see cref="IUnpaidLeaveCalendar"/> until S06's leave read model is integrated:
/// reports zero approved unpaid days for everyone.
/// </summary>
// WHY zero and not "throw / not implemented": payroll must be able to RUN today. Reporting
// zero unpaid days deducts NOTHING — the conservative, honest default (never over-deduct on
// a guess). When S06's approved-unpaid-leave query lands, this one file is swapped for the
// real adapter and every payslip picks up the deduction with no calculator change. See
// NOTES.md.
internal sealed class NoUnpaidLeaveCalendar : IUnpaidLeaveCalendar
{
    public Task<int> ApprovedUnpaidDaysAsync(
        Guid workerId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken) => Task.FromResult(0);
}
