namespace Atlas.Domain.Countries.Packs;

/// <summary>
/// Canada — a PURE DATA pack, added to make the counter-point to Japan: most countries do NOT need
/// code. CPP and EI are ordinary rate-on-capped-base contributions; the flat data shape says them
/// perfectly. This pack overrides no hook, inherits the empty defaults, and is discovered exactly the
/// same way. Adding it is one file and zero core changes — the diff --stat in the PR proves it.
/// </summary>
public sealed class CanadaRulePack() : DataDrivenRulePack(
    CountryCode.From("CAN"),
    CurrencyCode.From("CAD"),
    probationMonths: 3,
    annualLeaveMinimumDays: 10m,
    employerBurden:
    [
        new BurdenComponent("Canada Pension Plan (employer)", 0.0595m, AnnualAllowance: 3_500m, AnnualWageCap: 68_500m),
        new BurdenComponent("Employment Insurance (employer)", 0.0322m, AnnualWageCap: 63_200m),
    ],
    employeeDeductions:
    [
        new DeductionComponent("Canada Pension Plan (employee)", 0.0595m, AnnualAllowance: 3_500m, AnnualWageCap: 68_500m),
        new DeductionComponent("Employment Insurance (employee)", 0.0230m, AnnualWageCap: 63_200m),
    ],
    requiredDocuments:
    [
        new RequiredDocument(DocumentKind.IdentityProof, "Passport or provincial ID", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.WorkAuthorization, "Work permit / PR confirmation", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.SocialSecurityRegistration, "Social Insurance Number (SIN)", ExpiryTracked: false),
        new RequiredDocument(DocumentKind.BankMandate, "Direct deposit authorization", ExpiryTracked: false),
    ]);
