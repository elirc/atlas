using Atlas.Api.Mapping;
using Atlas.Application.Engagements;
using Atlas.Application.Pagination;
using Atlas.Contracts.V1;
using Atlas.Domain.Engagements;

namespace Atlas.Api.Endpoints;

public static class EngagementEndpoints
{
    // LEARN: the transitions are NAMED endpoints (`/send-offer`, `/activate`), not a
    // PATCH-to-status. A hire's verbs are part of the resource — each carries its own
    // payload (an offer needs a candidate; activate needs nothing) and its own guard.
    // See docs/adr/ADR-0006-state-machines.md and the debate thread on the PR.
    public static RouteGroupBuilder MapEngagements(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/engagements").WithTags("Engagements");

        group.MapPost("/", async (
            CreateEngagementRequest request,
            CreateEngagement useCase,
            CancellationToken ct) =>
        {
            Engagement engagement = await useCase.ExecuteAsync(
                request.QuoteId, request.RoleTitle, request.WeeklyHours, ct);

            return Results.Created(
                $"/api/v1/engagements/{engagement.Id}", engagement.ToDetailDto());
        })
        .Produces<EngagementDetailDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/send-offer", async (
            Guid id,
            SendOfferRequest request,
            SendOffer useCase,
            CancellationToken ct) =>
        {
            Engagement engagement = await useCase.ExecuteAsync(
                id, request.CandidateName, request.CandidateEmail, ct);

            return Results.Ok(engagement.ToDetailDto());
        })
        .Produces<EngagementDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/accept-offer", async (
            Guid id,
            AcceptOfferRequest request,
            AcceptOffer useCase,
            CancellationToken ct) =>
        {
            Engagement engagement = await useCase.ExecuteAsync(id, request.AcceptedOn, ct);
            return Results.Ok(engagement.ToDetailDto());
        })
        .Produces<EngagementDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/checklist/{item}/complete", async (
            Guid id,
            string item,
            CompleteOnboardingItem useCase,
            CancellationToken ct) =>
        {
            Engagement engagement = await useCase.ExecuteAsync(id, item, ct);
            return Results.Ok(engagement.ToDetailDto());
        })
        .Produces<EngagementDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/activate", async (
            Guid id,
            ActivateEngagement useCase,
            CancellationToken ct) =>
        {
            // WHY: activation returns the created Worker too, but the response is the
            // engagement (its CreatedWorkerId now points at the worker) — the worker
            // itself is fetched at /workers/{id}, its own resource.
            ActivationOutcome outcome = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(outcome.Engagement.ToDetailDto());
        })
        .Produces<EngagementDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/withdraw", async (
            Guid id,
            WithdrawEngagementRequest request,
            WithdrawEngagement useCase,
            CancellationToken ct) =>
        {
            Engagement engagement = await useCase.ExecuteAsync(id, request.Reason, ct);
            return Results.Ok(engagement.ToDetailDto());
        })
        .Produces<EngagementDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", async (Guid id, GetEngagement useCase, CancellationToken ct) =>
        {
            Engagement engagement = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(engagement.ToDetailDto());
        })
        .Produces<EngagementDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListEngagements useCase,
            CancellationToken ct) =>
        {
            Page<Engagement> page = await useCase.ExecuteAsync(cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(e => e.ToDto()));
        })
        .Produces<IEnumerable<EngagementDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return api;
    }
}
