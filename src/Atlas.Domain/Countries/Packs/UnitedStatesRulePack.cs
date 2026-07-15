namespace Atlas.Domain.Countries.Packs;

/// <summary>United States — FICA social security has a wage base; Medicare is uncapped.</summary>
public sealed class UnitedStatesRulePack() : DataDrivenRulePack(
    CountryCode.From("USA"),
    CurrencyCode.From("USD"),
    probationMonths: 3,
    annualLeaveMinimumDays: 0m,
    employerBurden:
    [
        new BurdenComponent("Social Security (employer FICA)", 0.062m, AnnualWageCap: 168_600m),
        new BurdenComponent("Medicare (employer)", 0.0145m),
        new BurdenComponent("Federal unemployment (FUTA)", 0.006m, AnnualWageCap: 7_000m),
    ],
    employeeDeductions:
    [
        new DeductionComponent("Social Security (employee FICA)", 0.062m, AnnualWageCap: 168_600m),
        new DeductionComponent("Medicare (employee)", 0.0145m),
    ],
    requiredDocuments:
    [
        new RequiredDocument(DocumentKind.IdentityProof, "State ID or passport", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.WorkAuthorization, "Form I-9 work authorization", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.TaxRegistration, "Form W-4 / SSN", ExpiryTracked: false),
        new RequiredDocument(DocumentKind.BankMandate, "Direct deposit authorization", ExpiryTracked: false),
    ]);
