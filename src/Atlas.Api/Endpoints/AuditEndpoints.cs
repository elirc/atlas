using Atlas.Api.Mapping;
using Atlas.Application.Auditing;
using Atlas.Application.Pagination;
using Atlas.Contracts.V1;
using Atlas.Domain.Auditing;

namespace Atlas.Api.Endpoints;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditLog(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/audit-log").WithTags("Audit log");

        group.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListAuditLog useCase,
            CancellationToken ct) =>
        {
            Page<AuditEntry> page = await useCase.ExecuteAsync(cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(e => e.ToDto()));
        })
        .Produces<IEnumerable<AuditEntryDto>>()
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return api;
    }
}
