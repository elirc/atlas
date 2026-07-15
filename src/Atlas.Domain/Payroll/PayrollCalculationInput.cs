using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;

namespace Atlas.Domain.Payroll;

/// <summary>
/// Everything the pure <see cref="PayrollCalculator"/> needs to produce one worker's
/// payslip for one period — as a single parameter object.
/// </summary>
// 📘 This type is the outcome of a design debate (see the PR thread / docs/adr/ADR-0008).
// The calculator started as `Calculate(termsHistory, pack, period, unpaidLeaveDays)`.
// The Reviewer's point won: four positional arguments — two of them easy to swap at a
// call site — is a bug waiting to happen, and a named bag makes each input's ROLE
// legible. The debate the Reviewer LOST is just as important: the calculator stays a
// pure static function, not a DI class. A parameter object groups inputs; it does not
// smuggle in mutable state or hidden dependencies. Stateless domain calculations stay
// pure; stateful workflows (PayrollRun) get objects.
//
// WHY the FULL terms history, not the resolved record: the calculator resolves terms
// "as of the period end" itself, in memory, from the whole history. This is S03's
// promise kept — "derivation stays cheap" — settled here in code: a bounded in-memory
// fold (EmploymentTerms.Resolve), no extra query, no stored "current salary" column to
// drift. The caller hands over history; the domain decides which record governs.
public sealed record PayrollCalculationInput(
    IReadOnlyList<EmploymentRecord> TermsHistory,
    ICountryRulePack Pack,
    PayPeriod Period,
    int UnpaidLeaveDays);
