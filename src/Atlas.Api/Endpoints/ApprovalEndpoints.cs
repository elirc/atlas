using Atlas.Api.Mapping;
using Atlas.Application.Approvals;
using Atlas.Contracts.V1;

namespace Atlas.Api.Endpoints;

// 🔗 The unified approval inbox — ONE endpoint that answers "what is waiting for me to decide?"
// across leave AND expenses. It exists because both aggregates now share the approval lifecycle;
// before the S08 extraction this endpoint would have had to know each type's private shape. It is
// mounted on the shared /api/v1 group (not under /expenses) because it is nobody's sub-resource.
public static class ApprovalEndpoints
{
    public static RouteGroupBuilder MapApprovals(this RouteGroupBuilder api)
    {
        api.MapGet("/approvals", async (
            int? limit,
            ListPendingApprovals useCase,
            CancellationToken ct) =>
        {
            IReadOnlyList<PendingApproval> pending = await useCase.ExecuteAsync(limit, ct);
            return Results.Ok(pending.Select(p => p.ToDto()));
        })
        .WithTags("Approvals")
        .Produces<IEnumerable<ApprovalInboxItemDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return api;
    }
}
