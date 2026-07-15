using Atlas.Api.Mapping;
using Atlas.Application.Compliance;
using Atlas.Contracts.V1;

namespace Atlas.Api.Endpoints;

public static class ComplianceEndpoints
{
    public static RouteGroupBuilder MapCompliance(this RouteGroupBuilder api)
    {
        // Worker-scoped document + compliance routes hang off /workers/{id}; the org-wide
        // expiring view lives under /compliance.
        RouteGroupBuilder workers = api.MapGroup("/workers").WithTags("Compliance");

        workers.MapPost("/{id:guid}/documents", async (
            Guid id, RecordDocumentRequest request, RecordWorkerDocument useCase, CancellationToken ct) =>
        {
            var document = await useCase.ExecuteAsync(id, request.Kind, request.Reference, request.ExpiresOn, ct);
            return Results.Created($"/api/v1/workers/{id}/documents", document.ToDto());
        })
        .Produces<WorkerDocumentDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        workers.MapGet("/{id:guid}/documents", async (
            Guid id, ListWorkerDocuments useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).Select(d => d.ToDto())))
        .Produces<IEnumerable<WorkerDocumentDto>>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        workers.MapGet("/{id:guid}/compliance", async (
            Guid id, GetWorkerCompliance useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<WorkerComplianceDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        RouteGroupBuilder compliance = api.MapGroup("/compliance").WithTags("Compliance");

        compliance.MapGet("/expiring", async (
            int? withinDays, ListExpiringDocuments useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(withinDays, ct)).Select(d => d.ToExpiringDto())))
        .Produces<IEnumerable<ExpiringDocumentDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return api;
    }
}
