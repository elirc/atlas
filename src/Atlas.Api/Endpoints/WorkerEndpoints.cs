using Atlas.Api.Mapping;
using Atlas.Application.Pagination;
using Atlas.Application.Workforce;
using Atlas.Contracts.V1;
using Atlas.Domain.Workforce;

namespace Atlas.Api.Endpoints;

public static class WorkerEndpoints
{
    public static RouteGroupBuilder MapWorkers(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/workers").WithTags("Workers");

        group.MapPost("/", async (
            HireWorkerRequest request,
            HireWorker useCase,
            CancellationToken ct) =>
        {
            HiredWorker hired = await useCase.ExecuteAsync(
                request.FullName, request.ContactEmail, request.Country, request.HiredOn,
                request.JobTitle, request.AnnualSalary, request.WeeklyHours, ct);

            return Results.Created(
                $"/api/v1/workers/{hired.Worker.Id}",
                new WorkerDetailDto(hired.Worker.ToDto(), hired.InitialTerms.ToDto()));
        })
        .Produces<WorkerDetailDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", async (
            Guid id,
            GetWorker getWorker,
            GetWorkerTerms getTerms,
            CancellationToken ct) =>
        {
            Worker worker = await getWorker.ExecuteAsync(id, ct);
            WorkerTerms terms = await getTerms.ExecuteAsync(id, null, ct);

            return Results.Ok(new WorkerDetailDto(worker.ToDto(), terms.Effective?.ToDto()));
        })
        .Produces<WorkerDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/terms", async (
            Guid id,
            DateOnly? asOf,
            GetWorkerTerms useCase,
            CancellationToken ct) =>
        {
            WorkerTerms terms = await useCase.ExecuteAsync(id, asOf, ct);

            return Results.Ok(new WorkerTermsDto(
                terms.Worker.Id,
                terms.AsOf,
                terms.Effective?.ToDto(),
                [.. terms.History.Select(r => r.ToDto())]));
        })
        .Produces<WorkerTermsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/terms", async (
            Guid id,
            UpdateTermsRequest request,
            UpdateEmploymentTerms useCase,
            CancellationToken ct) =>
        {
            var record = await useCase.ExecuteAsync(
                id, request.EffectiveFrom, request.JobTitle,
                request.AnnualSalary, request.WeeklyHours, ct);

            // WHY: 201 — a terms change CREATES a record (append-only). The Location
            // points at the history view where the new record now lives.
            return Results.Created($"/api/v1/workers/{id}/terms", record.ToDto());
        })
        .Produces<EmploymentTermsDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListWorkers useCase,
            CancellationToken ct) =>
        {
            Page<Worker> page = await useCase.ExecuteAsync(cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(w => w.ToDto()));
        })
        .Produces<IEnumerable<WorkerDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return api;
    }
}
