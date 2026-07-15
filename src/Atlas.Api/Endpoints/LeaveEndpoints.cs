using Atlas.Api.Mapping;
using Atlas.Application.Leave;
using Atlas.Application.Pagination;
using Atlas.Contracts.V1;
using Atlas.Domain.Leave;

namespace Atlas.Api.Endpoints;

public static class LeaveEndpoints
{
    public static RouteGroupBuilder MapLeave(this RouteGroupBuilder api)
    {
        RouteGroupBuilder policies = api.MapGroup("/leave-policies").WithTags("Leave");

        policies.MapPost("/", async (
            CreateLeavePolicyRequest request,
            CreateLeavePolicy useCase,
            CancellationToken ct) =>
        {
            LeavePolicy policy = await useCase.ExecuteAsync(
                request.Country, request.AnnualEntitlementDays, request.CarryoverCapDays, ct);

            return Results.Created($"/api/v1/leave-policies/{policy.Id}", policy.ToDto());
        })
        .Produces<LeavePolicyDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        policies.MapGet("/", async (ListLeavePolicies useCase, CancellationToken ct) =>
        {
            IReadOnlyList<LeavePolicy> list = await useCase.ExecuteAsync(ct);
            return Results.Ok(list.Select(p => p.ToDto()));
        })
        .Produces<IEnumerable<LeavePolicyDto>>();

        RouteGroupBuilder requests = api.MapGroup("/leave-requests").WithTags("Leave");

        requests.MapPost("/", async (
            SubmitLeaveRequestRequest request,
            SubmitLeaveRequest useCase,
            CancellationToken ct) =>
        {
            LeaveRequest created = await useCase.ExecuteAsync(
                request.WorkerId, request.From, request.To, request.Note, ct);

            return Results.Created($"/api/v1/leave-requests/{created.Id}", created.ToDto());
        })
        .Produces<LeaveRequestDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // WHY: state changes with business meaning get NAMED endpoints, not PATCH-to-status
        // (ADR-0006, S05). Approve, deny, and cancel each carry their own contract — approve
        // can 422 on balance/overlap; deny and cancel cannot. A single PATCH would collapse
        // three different guards into one 409-of-mystery.
        requests.MapPost("/{id:guid}/approve", async (
            Guid id, ApproveLeaveRequest useCase, CancellationToken ct) =>
        {
            LeaveRequest decided = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(decided.ToDto());
        })
        .Produces<LeaveRequestDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        requests.MapPost("/{id:guid}/deny", async (
            Guid id, DenyLeaveRequest useCase, CancellationToken ct) =>
        {
            LeaveRequest decided = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(decided.ToDto());
        })
        .Produces<LeaveRequestDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        requests.MapPost("/{id:guid}/cancel", async (
            Guid id, CancelLeaveRequest useCase, CancellationToken ct) =>
        {
            LeaveRequest cancelled = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(cancelled.ToDto());
        })
        .Produces<LeaveRequestDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        requests.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListLeaveRequests useCase,
            CancellationToken ct) =>
        {
            Page<LeaveRequest> page = await useCase.ExecuteAsync(cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(r => r.ToDto()));
        })
        .Produces<IEnumerable<LeaveRequestDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // WHY: the balance is a worker sub-resource (/workers/{id}/leave-balance), so it is
        // mapped on the shared api group rather than a leave-* group. It is a pure read —
        // a fold over policy + approved requests, computed on demand (ADR-0007).
        api.MapGet("/workers/{id:guid}/leave-balance", async (
            Guid id,
            DateOnly? asOf,
            GetLeaveBalance useCase,
            CancellationToken ct) =>
        {
            LeaveBalanceView balance = await useCase.ExecuteAsync(id, asOf, ct);
            return Results.Ok(balance.ToDto());
        })
        .WithTags("Leave")
        .Produces<LeaveBalanceDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
