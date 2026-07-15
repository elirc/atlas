using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Leave;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Leave;

/// <summary>
/// Record a worker's leave request. v1: submitted BY HR/admin ON BEHALF of a worker.
/// </summary>
public sealed class SubmitLeaveRequest(
    IWorkerRepository workers,
    ILeaveRequestRepository requests,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<LeaveRequest> ExecuteAsync(
        Guid workerId,
        DateOnly from,
        DateOnly to,
        string? note,
        CancellationToken cancellationToken)
    {
        // WHY (v1): leave is submitted BY an HR manager or admin on behalf of a worker.
        // Workers are not Atlas users yet — there is no worker→user link to authorize
        // self-service against, and inventing one "sort of" now would bake in an identity
        // mapping we haven't designed. HR-on-behalf is the honest v1; worker self-service
        // is a deliberate deferral (ADR-0007) that needs the worker↔user link first.
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Worker worker = await workers.FindInOrgAsync(workerId, orgId, cancellationToken)
            ?? throw new NotFoundException("worker", workerId);

        DateTimeOffset now = clock.GetUtcNow();

        // LEARN: the persisted row is Submitted, but it travels through Draft first — the
        // Draft → Submit transition is REAL and exercised here, it just happens inside one
        // use case for v1. When worker self-service lands, Draft becomes a persisted,
        // editable state the worker owns before an HR manager ever sees it.
        var request = LeaveRequest.Draft(worker.Id, orgId, from, to, note, context.UserId, now);
        request.Submit(now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "leave.submitted", "leave_request", request.Id,
            JsonSerializer.Serialize(new
            {
                WorkerId = worker.Id,
                From = from,
                To = to,
                request.WorkingDays,
            }),
            now));

        await requests.AddAsync(request, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return request;
    }
}
