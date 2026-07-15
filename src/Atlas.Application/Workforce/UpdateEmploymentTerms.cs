using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Workforce;

public sealed class UpdateEmploymentTerms(
    IWorkerRepository workers,
    IEmploymentRecordRepository records,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<EmploymentRecord> ExecuteAsync(
        Guid workerId,
        DateOnly effectiveFrom,
        string? jobTitle,
        decimal annualSalary,
        decimal weeklyHours,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);

        Worker worker = await workers.FindInOrgAsync(
                workerId, context.RequireOrg(), cancellationToken)
            ?? throw new NotFoundException("worker", workerId);

        if (effectiveFrom < worker.HiredOn)
        {
            throw new DomainRuleException(
                "Terms cannot take effect before the worker's hire date.");
        }

        IReadOnlyList<EmploymentRecord> history =
            await records.ListForWorkerAsync(worker.Id, cancellationToken);

        if (history.Any(r => r.EffectiveFrom == effectiveFrom))
        {
            // WHY: one record per effective day. A same-day correction should be a
            // deliberate act with its own semantics (S15 runbook material), not an
            // accidental double-submit — which is what this usually is.
            throw new DomainRuleException(
                $"Terms effective {effectiveFrom:yyyy-MM-dd} already exist for this worker.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        EmploymentRecord? previous = EmploymentTerms.Resolve(history, effectiveFrom);

        // LEARN: this is the whole trick of effective dating — the "update" is an
        // APPEND. Nothing is edited; history cannot be lost, because losing it
        // would require a statement we never wrote.
        var record = EmploymentRecord.Create(
            worker.Id, effectiveFrom, jobTitle, annualSalary,
            previous?.SalaryCurrency ?? "USD", weeklyHours, context.UserId, now);

        audit.Append(AuditEntry.Record(
            worker.OrgId, context.UserId, "worker.terms_changed", "worker", worker.Id,
            JsonSerializer.Serialize(new
            {
                EffectiveFrom = effectiveFrom,
                Before = previous is null
                    ? null
                    : new { previous.JobTitle, previous.AnnualSalary, previous.WeeklyHours },
                After = new { record.JobTitle, record.AnnualSalary, record.WeeklyHours },
            }),
            now));

        await records.AddAsync(record, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return record;
    }
}
