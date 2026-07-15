using Atlas.Application.Abstractions;
using Atlas.Application.Payroll;
using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Infrastructure.Vendors.LedgerPay;

/// <summary>
/// The anti-corruption layer for LedgerPay: the ONE place that speaks its dialect. It translates an
/// Atlas <see cref="PayrollCalculationInput"/> into LedgerPay's minor-units request, and LedgerPay's
/// coarse bundled lines back into a canonical <see cref="PayslipCalculation"/> the domain already
/// understands. Above this class, the word "LedgerPay" never appears; below it, the words "payslip"
/// and "domain" never do.
/// </summary>
// 📘 ACL, SECOND APPLICATION — the PayGrid pattern reused, not reinvented (ADR-0010 → ADR-0014). The
// entire "we now have a second payroll engine" surface lives here: the minor-units conversion, the
// line-code table, the error mapping. PreparePayrollRun and ShadowComparePayrollRun import none of
// it — they hold an IPayrollCalculationVendor and never learn which vendor answered. Everything the
// stakeholder feared ("LedgerPay returns different line granularity") is absorbed at this boundary:
// LedgerPay's one bundled EE_STATUTORY line becomes one canonical EmployeeDeduction line, its one
// ER_ONCOST becomes one EmployerBurden line, and the payslip that reaches persistence is the same
// SHAPE Atlas has always stored — just, for a LedgerPay run, coarser in its labels.
//
// 🔍 The error asymmetry from ADR-0010, kept faithfully: a country LedgerPay cannot price is a
// business "no" (a clean 422 DomainRuleException); an unmapped line code is a hole in our model of
// the vendor (a loud 500 UnmappedVendorLineException). Same source (LedgerPay said something), two
// shapes, because the caller does two different things with them.
// NOTE: internal (unlike the public PayGrid adapter) because its adversarial dialect tests live in
// Api.Tests, the assembly permitted to see Infrastructure — no need to widen visibility further.
internal sealed class LedgerPayCalculator(LedgerPayClient ledgerPay) : IPayrollCalculationVendor
{
    private const int PeriodsPerYear = 12; // v1 is Monthly-only (matches PayrollCalculator's constant).
    private const decimal MinorUnitsPerUnit = 100m;

    public PayrollVendor Vendor => PayrollVendor.LedgerPay;

    public Task<PayslipCalculation> CalculateAsync(
        PayrollCalculationInput input, CancellationToken cancellationToken)
    {
        // ── Atlas-side prologue: identical guards to the native engine (a vendor swap must not
        //    change WHICH inputs are legal, only who does the arithmetic) ──────────────────────────
        EmploymentRecord terms = EmploymentTerms.Resolve(input.TermsHistory, input.Period.End)
            ?? throw new DomainRuleException(
                $"No employment terms are in effect on {input.Period.End:yyyy-MM-dd}.");

        var currency = CurrencyCode.From(terms.SalaryCurrency);
        if (currency != input.Pack.Currency)
        {
            throw new DomainRuleException(
                $"Worker is paid in {currency} but {input.Pack.Country} payroll is in {input.Pack.Currency}.");
        }

        // ── Atlas → LedgerPay (the outbound half of the translation) ───────────────────────────────
        var request = new LedgerPayCalcRequest(
            EmployeeRef: terms.WorkerId.ToString("N"),
            IsoCountry: input.Pack.Country.Value,
            IsoCurrency: currency.Value,
            AnnualGrossMinor: ToMinor(terms.AnnualSalary),
            PeriodOrdinal: input.Period.IndexInYear,
            PeriodsInYear: PeriodsPerYear,
            UnpaidDays: input.UnpaidLeaveDays);

        LedgerPayCalcResult result;
        try
        {
            result = ledgerPay.Calculate(request);
        }
        catch (LedgerPayException ex) when (ex.Code == "E_COUNTRY_UNSUPPORTED")
        {
            // A business "no": LedgerPay has no model for this country. Neutral prose, a clean 422 —
            // the vendor's code rides only in the log, never into the domain.
            throw new DomainRuleException(
                $"The payroll vendor cannot price {input.Pack.Country} payroll.");
        }

        return Task.FromResult(Translate(result, currency));
    }

    // ── LedgerPay → Atlas (the inbound half — this method IS the line-code dialect table) ──────────
    private static PayslipCalculation Translate(LedgerPayCalcResult result, CurrencyCode currency)
    {
        Money? gross = null;
        Money? employeeStatutory = null;
        Money? unpaidLeave = null;
        Money? employerOnCost = null;

        foreach (LedgerPayLine line in result.Lines)
        {
            switch (line.Code)
            {
                case "GROSS":
                    gross = Money.Of(FromMinor(line.AmountMinor), currency);
                    break;
                case "EE_STATUTORY":
                    employeeStatutory = Money.Of(FromMinor(line.AmountMinor), currency);
                    break;
                case "UNPAID_LEAVE":
                    unpaidLeave = Money.Of(FromMinor(line.AmountMinor), currency);
                    break;
                case "ER_ONCOST":
                    employerOnCost = Money.Of(FromMinor(line.AmountMinor), currency);
                    break;
                case "NET":
                    // KNOWN-but-IGNORED: we derive net from gross minus withheld ourselves rather
                    // than trust the vendor's self-reported total (a vendor's own net is a claim to
                    // check, not a fact to store). Listed here so it is not treated as unmapped.
                    break;
                default:
                    // A code we have never been taught — a hole in our model of LedgerPay, not a
                    // deduction. Fail loud (500) rather than guess it into "some other line".
                    throw new UnmappedVendorLineException(line.Code, "period calculation");
            }
        }

        // A response missing a structural line is a malformed answer — the same fail-loud stance.
        if (gross is null || employeeStatutory is null || employerOnCost is null)
        {
            throw new UnmappedVendorLineException("<missing GROSS/EE_STATUTORY/ER_ONCOST>", "period calculation");
        }

        var lines = new List<PayslipLineItem>
        {
            // Different GRANULARITY, same KIND: LedgerPay's one bundled withholding line becomes one
            // canonical EmployeeDeduction. Consumers who want the old itemised shape read v1 (which
            // never itemised across vendors anyway); v2 groups by kind, which is stable regardless.
            new("Statutory deductions (LedgerPay)", PayslipLineKind.EmployeeDeduction, employeeStatutory),
        };

        Money net = gross - employeeStatutory;
        if (unpaidLeave is { } unpaid)
        {
            lines.Add(new PayslipLineItem("Unpaid leave", PayslipLineKind.UnpaidLeave, unpaid));
            net -= unpaid;
        }

        lines.Add(new PayslipLineItem("Employer on-costs (LedgerPay)", PayslipLineKind.EmployerBurden, employerOnCost));
        Money employerCost = gross + employerOnCost;

        // Net + Σ(withheld) == Gross holds BY CONSTRUCTION here (we derived net from those very lines),
        // so a LedgerPay payslip satisfies the same invariant S07's golden test pins on the native one.
        return new PayslipCalculation(gross, lines, net, employerCost);
    }

    // GOTCHA: LedgerPay speaks integer minor units; Money speaks a decimal amount + a currency. Scale
    // is Money.Scale (2 for every currency Atlas models today). Round banker's-style at the boundary
    // — the vendor cannot represent a fractional minor unit, and rounding is a boundary decision made
    // once, here. (LedgerPay's OWN internal rounding is away-from-zero; that is its business, and its
    // divergence from ours is exactly what the shadow comparison is built to measure.)
    private static long ToMinor(decimal amount) =>
        (long)decimal.Round(amount * MinorUnitsPerUnit, 0, MidpointRounding.ToEven);

    private static decimal FromMinor(long minor) => minor / MinorUnitsPerUnit;
}
