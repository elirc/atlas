namespace Atlas.Domain.Countries.Packs;

/// <summary>Philippines — monthly contribution ceilings annualized, illustrative teaching values.</summary>
public sealed class PhilippinesRulePack() : DataDrivenRulePack(
    CountryCode.From("PHL"),
    CurrencyCode.From("PHP"),
    probationMonths: 6,
    annualLeaveMinimumDays: 5m,
    employerBurden:
    [
        new BurdenComponent("SSS (employer)", 0.095m, AnnualWageCap: 420_000m),
        new BurdenComponent("PhilHealth (employer)", 0.025m, AnnualWageCap: 1_200_000m),
        new BurdenComponent("Pag-IBIG (employer)", 0.020m, AnnualWageCap: 120_000m),
    ],
    employeeDeductions:
    [
        new DeductionComponent("SSS (employee)", 0.045m, AnnualWageCap: 420_000m),
        new DeductionComponent("PhilHealth (employee)", 0.025m, AnnualWageCap: 1_200_000m),
        new DeductionComponent("Pag-IBIG (employee)", 0.020m, AnnualWageCap: 120_000m),
    ],
    requiredDocuments:
    [
        new RequiredDocument(DocumentKind.IdentityProof, "PhilID or passport", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.TaxRegistration, "TIN (Tax Identification Number)", ExpiryTracked: false),
        new RequiredDocument(DocumentKind.SocialSecurityRegistration, "SSS number", ExpiryTracked: false),
    ]);
