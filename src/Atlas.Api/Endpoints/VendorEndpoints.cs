using Atlas.Api.Mapping;
using Atlas.Application.Payroll;
using Atlas.Contracts.V1;
using Atlas.Domain.Payroll;

namespace Atlas.Api.Endpoints;

/// <summary>
/// Vendor selection (effective-dated per org+country) and shadow-run comparison — the S12 pivot's
/// API surface. All additive under /api/v1: choosing a payroll vendor and trialling one are NEW
/// capabilities, not changes to an existing shape, so they cost new routes, never a version bump.
/// </summary>
public static class VendorEndpoints
{
    public static RouteGroupBuilder MapPayrollVendors(this RouteGroupBuilder api)
    {
        RouteGroupBuilder assignments = api.MapGroup("/payroll/vendor-assignments").WithTags("Payroll vendors");
        RouteGroupBuilder runs = api.MapGroup("/payroll/runs").WithTags("Payroll vendors");

        // ── Effective-dated vendor selection ─────────────────────────────────────────────────────
        assignments.MapPost("/", async (
            AssignPayrollVendorRequest request,
            AssignPayrollVendor useCase,
            CancellationToken ct) =>
        {
            // Parse the wire vendor string to the enum at the edge — a typo is a clean 400 here,
            // before any business logic runs (see VendorMapping.ParseVendor).
            PayrollVendor vendor = VendorMapping.ParseVendor(request.Vendor);

            VendorAssignment assignment = await useCase.ExecuteAsync(
                request.Country, vendor, request.EffectiveFrom, ct);

            return Results.Created(
                $"/api/v1/payroll/vendor-assignments/{assignment.Id}", assignment.ToDto());
        })
        .Produces<VendorAssignmentDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        assignments.MapGet("/", async (
            string? country,
            ListVendorAssignments useCase,
            CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(country, ct)).ToDto()))
        .Produces<VendorHistoryDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // ── Shadow-run comparison (gated, redacted) ──────────────────────────────────────────────
        runs.MapPost("/{id:guid}/shadow-compare", async (
            Guid id,
            ShadowComparePayrollRun useCase,
            CancellationToken ct) =>
        {
            VendorComparisonReport report = await useCase.ExecuteAsync(id, ct);
            // 201: a fresh comparison document, which supersedes any prior one for this run.
            return Results.Created(
                $"/api/v1/payroll/runs/{report.RunId}/shadow-comparison", report.ToDto());
        })
        .Produces<VendorComparisonReportDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        runs.MapGet("/{id:guid}/shadow-comparison", async (
            Guid id,
            GetVendorComparison useCase,
            CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<VendorComparisonReportDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
