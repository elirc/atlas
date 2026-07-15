using Atlas.Api.Mapping;
using Atlas.Application.Payroll;
using Atlas.Contracts.V2;

namespace Atlas.Api.Endpoints;

/// <summary>
/// API v2 of the payslip resource: the grouped-lines shape. A SIBLING route group under /api/v2, not
/// a rewrite — it reuses the SAME <see cref="GetPayslip"/> use case, because the read is
/// version-agnostic and only the PROJECTION differs (grouped vs flat).
/// </summary>
// 📘 A new API version is a new SHAPE over the same domain read, so versioning lives in the mapping
// layer (PayslipV2Mapping), never in the domain or the use case. The domain does not know it has two
// audiences; that ignorance is what keeps a v3 (or a v4) from ever touching a payslip's business
// logic. v1 and v2 both call ExecuteAsync and diverge only at .ToDto() vs .ToV2Dto() (ADR-0015).
public static class PayslipV2Endpoints
{
    public static RouteGroupBuilder MapPayslipsV2(this RouteGroupBuilder apiV2)
    {
        RouteGroupBuilder group = apiV2.MapGroup("/payslips").WithTags("Payroll v2");

        group.MapGet("/{id:guid}", async (
            Guid id, GetPayslip useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToV2Dto()))
        .Produces<PayslipV2Dto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return apiV2;
    }
}
