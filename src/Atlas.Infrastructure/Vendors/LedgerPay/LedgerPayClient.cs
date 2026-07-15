namespace Atlas.Infrastructure.Vendors.LedgerPay;

/// <summary>
/// LedgerPay — an in-repo mock of the second payroll-calculation vendor, built to misbehave the way
/// a real vendor does, not the way its sales deck promises. It speaks integer minor units, rounds
/// half-AWAY-from-zero, bundles its lines coarsely, and models each country with a flat cap-free
/// rate that disagrees with Atlas at the edges. This XML doc IS LedgerPay's README; treat it as the
/// vendor's manual.
/// </summary>
/// <remarks>
/// <para><b>THE DIALECT (what makes LedgerPay LedgerPay):</b></para>
/// <list type="bullet">
///   <item>
///     <b>Money is integer minor units.</b> <see cref="LedgerPayCalcRequest.AnnualGrossMinor"/> and
///     every <see cref="LedgerPayLine.AmountMinor"/> are <c>long</c> minor units. €5,000.00 is
///     <c>500000</c>. There is no decimal on the wire; converting Atlas's <c>Money</c> to and from
///     minor units is the adapter's job, done once, at the boundary. (Same shape PayGrid used — the
///     dialects rhyme, which is exactly why one ACL pattern serves both.)
///   </item>
///   <item>
///     <b>Rounding is half-AWAY-from-zero.</b> Atlas rounds banker's (half-to-even); LedgerPay does
///     not. On a line that lands on a half-cent the two engines differ by one minor unit — a tiny,
///     systematic "rounding dialect" divergence shadow mode is tuned to tolerate (see the report
///     tolerance) rather than flag.
///   </item>
///   <item>
///     <b>Lines are bundled, from an OPEN vocabulary.</b> LedgerPay returns one <c>EE_STATUTORY</c>
///     line for ALL employee withholding and one <c>ER_ONCOST</c> line for ALL employer cost, plus
///     <c>GROSS</c>, an optional <c>UNPAID_LEAVE</c>, and <c>NET</c>. New codes appear without a
///     changelog; the adapter's policy for a code it has never seen is to FAIL LOUD, never guess.
///   </item>
///   <item>
///     <b>Its country model is flat and cap-free.</b> LedgerPay applies one aggregate rate to gross
///     and ignores the wage ceilings Atlas honours (<see cref="LedgerPaySettings.CountryRates"/>).
///     Below every cap it agrees with Atlas to the cent; above a cap it over-withholds and DIVERGES.
///     That divergence is the point — it is what shadow running exists to catch.
///   </item>
///   <item>
///     <b>It refuses countries it has no model for.</b> A country absent from its rate table raises
///     <see cref="LedgerPayException"/> with <c>E_COUNTRY_UNSUPPORTED</c> — a business "no", which
///     the adapter turns into a clean 422, distinct from an unmapped line code (our-bug 500).
///   </item>
/// </list>
/// <para>
/// The mock is in-process and computes from its own rate table. It is not a network client; there is
/// no serialization, no base address. It exists so the adapter's tests can drive LedgerPay through
/// its dialect (bundled lines, away-from-zero rounding, an unsupported country, a surprise code) as
/// an executable spec.
/// </para>
/// </remarks>
public sealed class LedgerPayClient(LedgerPaySettings settings)
{
    /// <summary>Every request LedgerPay has received — a spy the adapter's tests read to pin the wire
    /// translation (Money → minor units, period ordinal, unpaid days). A mock affordance, not a real API.</summary>
    public List<LedgerPayCalcRequest> ReceivedRequests { get; } = [];

    /// <summary>Calculate one period. Returns coarse, bundled lines in LedgerPay's minor units, or throws.</summary>
    public LedgerPayCalcResult Calculate(LedgerPayCalcRequest request)
    {
        ReceivedRequests.Add(request);

        if (request.PeriodsInYear <= 0)
        {
            throw new LedgerPayException("E_BAD_REQUEST", "LedgerPay: periodsInYear must be positive.");
        }

        if (!settings.CountryRates.TryGetValue(request.IsoCountry, out (decimal EmployeeRate, decimal EmployerRate) rates))
        {
            // A business "no": LedgerPay has no statutory model for this country. Distinct from a
            // transport error and distinct from an unmapped line code — the adapter maps it to a 422.
            throw new LedgerPayException(
                "E_COUNTRY_UNSUPPORTED", $"LedgerPay does not support payroll for '{request.IsoCountry}'.");
        }

        long grossMinor = PerPeriod(request.AnnualGrossMinor, request.PeriodsInYear);
        long employeeStatutoryMinor = PerPeriod(
            (decimal)request.AnnualGrossMinor * rates.EmployeeRate, request.PeriodsInYear);
        long employerOnCostMinor = PerPeriod(
            (decimal)request.AnnualGrossMinor * rates.EmployerRate, request.PeriodsInYear);

        // Unpaid leave happens THIS period — never allocated across the year. Daily rate is annual
        // gross over LedgerPay's working-days-per-year, times the days, rounded LedgerPay's way.
        long unpaidMinor = request.UnpaidDays > 0
            ? RoundAwayFromZero((decimal)request.AnnualGrossMinor / settings.WorkingDaysPerYear * request.UnpaidDays)
            : 0;

        long netMinor = grossMinor - employeeStatutoryMinor - unpaidMinor;

        var lines = new List<LedgerPayLine>
        {
            new("GROSS", grossMinor),
            new("EE_STATUTORY", employeeStatutoryMinor),
        };

        if (unpaidMinor > 0)
        {
            lines.Add(new LedgerPayLine("UNPAID_LEAVE", unpaidMinor));
        }

        lines.Add(new LedgerPayLine("ER_ONCOST", employerOnCostMinor));
        lines.Add(new LedgerPayLine("NET", netMinor));

        // Test knob: emit a code the adapter has never been taught, to drive its fail-loud path.
        if (settings.UnknownLineCode is { } surprise)
        {
            lines.Add(new LedgerPayLine(surprise, 0));
        }

        return new LedgerPayCalcResult(request.EmployeeRef, request.IsoCurrency, lines);
    }

    // LedgerPay divides the annual figure by the period count and rounds AWAY from zero — its
    // rounding dialect. (Atlas allocates largest-remainder and rounds banker's; the two differ by at
    // most one minor unit on a half-cent line, which is what the shadow tolerance absorbs.)
    private long PerPeriod(decimal annualMinor, int periods) => RoundAwayFromZero(annualMinor / periods);

    private static long RoundAwayFromZero(decimal value) =>
        (long)decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
