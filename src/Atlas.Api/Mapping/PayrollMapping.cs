using Atlas.Contracts.V1;
using Atlas.Domain.Payroll;

namespace Atlas.Api.Mapping;

// WHY a second mapping file, not more lines in DtoMapping: payroll is a whole feature's
// worth of translations, and keeping them beside each other (rather than scrolling past
// the workforce maps) makes the payroll surface legible on its own. Both classes live in
// Atlas.Api.Mapping, so `using Atlas.Api.Mapping;` brings every .ToDto() into scope.
//
// S12 NOTE: the full-payslip maps moved to PayslipV2Mapping.cs, where the v2 grouping is the
// canonical projection and v1's flat ToDto(Payslip) is an ADAPTER over it. What stays here is the
// run/cycle/summary surface — including PayrollRunDto's new SelectedVendor (an additive v1 field).
internal static class PayrollMapping
{
    // NOTE: Money.ToDto lives in QuoteMapping (S04) and is reused here — one Money→MoneyDto
    // mapping for the whole Api (same Atlas.Api.Mapping namespace, so the extension resolves).

    public static PayCycleDto ToDto(this PayCycle cycle) =>
        new(cycle.Id, cycle.Country.Value, cycle.Frequency.ToString(), cycle.CreatedAtUtc);

    public static PayrollRunDto ToDto(this PayrollRun run) =>
        new(run.Id, run.PayCycleId, run.PeriodStart, run.PeriodEnd,
            run.State.ToString(), run.SelectedVendor.ToString(), run.CreatedAtUtc, run.ExecutedAtUtc);

    public static PayslipSummaryDto ToSummaryDto(this Payslip payslip) =>
        new(payslip.Id, payslip.WorkerId,
            payslip.Gross.ToDto(), payslip.Net.ToDto(), payslip.EmployerCost.ToDto());

    // Totals are Money? (null when the run has no payslips) — the null-conditional keeps
    // that nullability all the way to the wire, so the client sees null, not a fake zero.
    public static PayrollRunDetailDto ToDetailDto(this PayrollRun run) =>
        new(run.ToDto(),
            run.TotalGross()?.ToDto(),
            run.TotalNet()?.ToDto(),
            run.TotalEmployerCost()?.ToDto(),
            [.. run.Payslips.Select(p => p.ToSummaryDto())]);
}
