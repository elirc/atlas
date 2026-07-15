using Atlas.Contracts.V1;
using Atlas.Contracts.V2;
using Atlas.Domain.Payroll;

namespace Atlas.Api.Mapping;

/// <summary>
/// The payslip's two wire projections from ONE domain read: v2 groups lines by kind (the new
/// canonical shape), and v1 is an ADAPTER over v2 — the grouped shape flattened back to the
/// historical flat, Kind-tagged list. Same numbers, two structures, no fork.
/// </summary>
// 🔍 "Check what v1 consumers observe: same JSON, byte-for-byte? PROVE it." — the review demand this
// file answers (playbook teaching placement). The proof is PayslipVersioningTests: a recorded v1
// payslip response is replayed against THIS re-implementation and must match to the byte. That green
// golden is what lets v1 be re-expressed through v2 without gambling a client's integration.
internal static class PayslipV2Mapping
{
    // v2 is the canonical projection: group the flat domain lines by kind. WITHIN each group the
    // stored order is preserved, and the stored order IS the calculator's emission order
    // (EmployeeDeduction… then UnpaidLeave… then EmployerBurden…). That ordering property is what
    // makes the v1 flatten below reproduce v1 exactly.
    public static PayslipV2Dto ToV2Dto(this Payslip payslip)
    {
        var employeeDeductions = new List<PayslipLineV2Dto>();
        var unpaidLeave = new List<PayslipLineV2Dto>();
        var employerBurden = new List<PayslipLineV2Dto>();

        foreach (PayslipLine line in payslip.Lines)
        {
            var dto = new PayslipLineV2Dto(line.Label, line.Amount.ToDto());
            switch (line.Kind)
            {
                case PayslipLineKind.EmployeeDeduction:
                    employeeDeductions.Add(dto);
                    break;
                case PayslipLineKind.UnpaidLeave:
                    unpaidLeave.Add(dto);
                    break;
                case PayslipLineKind.EmployerBurden:
                    employerBurden.Add(dto);
                    break;
                default:
                    // A kind we have no group for is a hole in the projection, not a line to drop —
                    // fail loud (500) rather than silently omit money from a payslip.
                    throw new InvalidOperationException($"Unknown payslip line kind '{line.Kind}'.");
            }
        }

        return new PayslipV2Dto(
            payslip.Id, payslip.RunId, payslip.WorkerId,
            payslip.PeriodStart, payslip.PeriodEnd,
            payslip.Gross.ToDto(),
            new PayslipLineGroupsDto(employeeDeductions, unpaidLeave, employerBurden),
            payslip.Net.ToDto(),
            payslip.EmployerCost.ToDto());
    }

    /// <summary>
    /// v1 payslip — served UNCHANGED, but built as an adapter over v2 rather than forked from the
    /// domain a second time. Flatten the groups back to a flat, Kind-tagged list in the historical
    /// order; the JSON is byte-identical to pre-S12 for every payslip the current vendors produce.
    /// </summary>
    // ⚠️ The byte-for-byte guarantee rests on ONE property: the flatten order (EE, then unpaid, then
    // ER) equals the stored order, which holds because every calculator — native and LedgerPay —
    // emits its lines already grouped in that order. If a future vendor INTERLEAVED kinds, this
    // flatten would regroup them and a v1 client could observe a reordering. That is a real seam, so
    // the contract test pins byte-for-byte against the real vendors, and this comment is the tripwire
    // for the next author. (docs/concepts/contract-evolution.md.)
    public static PayslipDto ToDto(this Payslip payslip)
    {
        PayslipV2Dto v2 = payslip.ToV2Dto();
        PayslipLineGroupsDto g = v2.Lines;

        var lines = new List<PayslipLineDto>(
            g.EmployeeDeductions.Count + g.UnpaidLeave.Count + g.EmployerBurden.Count);
        lines.AddRange(g.EmployeeDeductions.Select(l =>
            new PayslipLineDto(l.Label, nameof(PayslipLineKind.EmployeeDeduction), l.Amount)));
        lines.AddRange(g.UnpaidLeave.Select(l =>
            new PayslipLineDto(l.Label, nameof(PayslipLineKind.UnpaidLeave), l.Amount)));
        lines.AddRange(g.EmployerBurden.Select(l =>
            new PayslipLineDto(l.Label, nameof(PayslipLineKind.EmployerBurden), l.Amount)));

        return new PayslipDto(
            v2.Id, v2.RunId, v2.WorkerId, v2.PeriodStart, v2.PeriodEnd,
            v2.Gross, lines, v2.Net, v2.EmployerCost);
    }
}
