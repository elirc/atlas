using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Leave;

namespace Atlas.Application.Leave;

public sealed class DenyLeaveRequest(
    ILeaveRequestRepository requests,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<LeaveRequest> ExecuteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        LeaveRequest request = await requests.FindInOrgAsync(requestId, orgId, cancellationToken)
            ?? throw new NotFoundException("leave request", requestId);

        // WHY: no balance or overlap math — a denial is unconditional. The entity's own
        // guard (must be Submitted) is the only rule, and it lives in the domain, not here.
        DateTimeOffset now = clock.GetUtcNow();
        request.Deny(context.UserId, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "leave.denied", "leave_request", request.Id,
            JsonSerializer.Serialize(new { WorkerId = request.WorkerId }),
            now));

        await unitOfWork.CommitAsync(cancellationToken);

        return request;
    }
}
