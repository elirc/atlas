namespace Atlas.Application.Abstractions;

/// <summary>
/// How many APPROVED unpaid-leave days a worker took inside a period — the one thing
/// payroll needs to read from the leave subsystem. A PORT: payroll asks the question and
/// never learns how leave is stored.
/// </summary>
// 🔗 S06 owns leave requests and their approval lifecycle; this port is the narrow seam
// payroll reads them through. It is deliberately a QUESTION ("how many approved unpaid
// days?"), not a handle on LeaveRequest — payroll should not know leave's schema, only
// its answer. The real S06 adapter counts approved unpaid LeaveRequests overlapping the
// period; until S06's read model is wired, a zero-returning stand-in keeps the running
// system honest (it deducts nothing rather than guessing). See NOTES.md.
public interface IUnpaidLeaveCalendar
{
    Task<int> ApprovedUnpaidDaysAsync(
        Guid workerId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken);
}
