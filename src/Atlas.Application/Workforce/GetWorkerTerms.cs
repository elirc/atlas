using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Workforce;

public sealed record WorkerTerms(
    Worker Worker,
    DateOnly AsOf,
    EmploymentRecord? Effective,
    IReadOnlyList<EmploymentRecord> History);

public sealed class GetWorkerTerms(
    IWorkerRepository workers,
    IEmploymentRecordRepository records,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<WorkerTerms> ExecuteAsync(
        Guid workerId,
        DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        context.RequireUser();

        Worker worker = await workers.FindInOrgAsync(
                workerId, context.RequireOrg(), cancellationToken)
            ?? throw new NotFoundException("worker", workerId);

        IReadOnlyList<EmploymentRecord> history =
            await records.ListForWorkerAsync(worker.Id, cancellationToken);

        // WHY: "today" is a calendar question and UTC is the least-wrong default
        // for an org-facing view; the worker's local calendar matters for
        // STATUTORY dates (probation, notice), which is a different code path.
        DateOnly effectiveDate = asOf
            ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        EmploymentRecord? effective = EmploymentTerms.Resolve(history, effectiveDate);

        return new WorkerTerms(
            worker,
            effectiveDate,
            effective,
            [.. history.OrderByDescending(r => r.EffectiveFrom)]);
    }
}
