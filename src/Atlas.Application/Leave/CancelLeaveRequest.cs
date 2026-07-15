using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Leave;

namespace Atlas.Application.Leave;

public sealed class CancelLeaveRequest(
    ILeaveRequestRepository requests,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<LeaveRequest> ExecuteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        // WHY (v1): cancellation is an HR/admin act on behalf of the worker — same reason
        // submission is (no worker↔user link yet). The entity guards WHICH states can be
        // cancelled (submitted or approved); this use case guards WHO may ask.
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        LeaveRequest request = await requests.FindInOrgAsync(requestId, orgId, cancellationToken)
            ?? throw new NotFoundException("leave request", requestId);

        DateTimeOffset now = clock.GetUtcNow();
        request.Cancel();

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "leave.cancelled", "leave_request", request.Id,
            JsonSerializer.Serialize(new { WorkerId = request.WorkerId }),
            now));

        await unitOfWork.CommitAsync(cancellationToken);

        return request;
    }
}
