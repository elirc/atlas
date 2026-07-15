using Atlas.Api.Mapping;
using Atlas.Application.Pagination;
using Atlas.Application.Payroll;
using Atlas.Contracts.V1;
using Atlas.Domain.Payroll;

namespace Atlas.Api.Endpoints;

// LEARN: transitions are NAMED endpoints (submit-for-review / approve / reject / execute),
// not a PATCH to a status field. That is S05's rule (ADR-0006): state changes with business
// meaning are the resource's verbs — each can carry its own contract and its own authz, and
// an illegal move is a clean 422 from the domain, not a 409-of-mystery.
public static class PayrollEndpoints
{
    public static RouteGroupBuilder MapPayroll(this RouteGroupBuilder api)
    {
        MapCycles(api.MapGroup("/payroll/cycles").WithTags("Payroll"));
        MapRuns(api.MapGroup("/payroll/runs").WithTags("Payroll"));
        MapPayslips(api.MapGroup("/payslips").WithTags("Payroll"));
        return api;
    }

    private static void MapCycles(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            CreatePayCycleRequest request,
            CreatePayCycle useCase,
            CancellationToken ct) =>
        {
            PayCycle cycle = await useCase.ExecuteAsync(request.Country, request.Frequency, ct);
            return Results.Created($"/api/v1/payroll/cycles/{cycle.Id}", cycle.ToDto());
        })
        .Produces<PayCycleDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (ListPayCycles useCase, CancellationToken ct) =>
        {
            IReadOnlyList<PayCycle> cycles = await useCase.ExecuteAsync(ct);
            return Results.Ok(cycles.Select(c => c.ToDto()));
        })
        .Produces<IEnumerable<PayCycleDto>>();
    }

    private static void MapRuns(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            PreparePayrollRunRequest request,
            PreparePayrollRun useCase,
            CancellationToken ct) =>
        {
            PreparedRun prepared = await useCase.ExecuteAsync(
                request.CycleId, request.Year, request.Month, ct);

            PayrollRunDetailDto dto = prepared.Run.ToDetailDto();

            // WHY 201 for a fresh run, 200 for one that already existed: preparing is
            // idempotent, and the status is how the client learns which happened without
            // a body diff. The ADR-0008 note records why we don't collapse both to 200.
            return prepared.AlreadyExisted
                ? Results.Ok(dto)
                : Results.Created($"/api/v1/payroll/runs/{prepared.Run.Id}", dto);
        })
        .Produces<PayrollRunDetailDto>(StatusCodes.Status201Created)
        .Produces<PayrollRunDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/submit-for-review", async (
            Guid id, SubmitPayrollRun useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<PayrollRunDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/approve", async (
            Guid id, ApprovePayrollRun useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<PayrollRunDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/reject", async (
            Guid id, RejectPayrollRun useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<PayrollRunDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/execute", async (
            Guid id, ExecutePayrollRun useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<PayrollRunDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", async (
            Guid id, GetPayrollRun useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDetailDto()))
        .Produces<PayrollRunDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListPayrollRuns useCase,
            CancellationToken ct) =>
        {
            Page<PayrollRun> page = await useCase.ExecuteAsync(cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(r => r.ToDto()));
        })
        .Produces<IEnumerable<PayrollRunDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapPayslips(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (
            Guid id, HttpResponse response, GetPayslip useCase, CancellationToken ct) =>
        {
            Payslip payslip = await useCase.ExecuteAsync(id, ct);

            // S12: v1 payslips are DEPRECATED in favour of /api/v2/payslips (line-grouping change).
            // Announce it in-band on every read — Deprecation + Sunset + a successor Link — so a
            // client learns to migrate, by when, and to what, without reading a changelog (ADR-0015).
            // The BODY is unchanged (byte-for-byte, proven by PayslipVersioningTests); only headers add.
            ApiDeprecation.StampPayslipV1(response, payslip.Id);

            return Results.Ok(payslip.ToDto());
        })
        .Produces<PayslipDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
