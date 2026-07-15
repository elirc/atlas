namespace Atlas.Domain.Countries.Packs;

/// <summary>Germany — contribution ceilings (Beitragsbemessungsgrenze), illustrative teaching values.</summary>
public sealed class GermanyRulePack() : DataDrivenRulePack(
    CountryCode.From("DEU"),
    CurrencyCode.From("EUR"),
    probationMonths: 6,
    annualLeaveMinimumDays: 20m,
    employerBurden:
    [
        new BurdenComponent("Pension insurance (employer)", 0.093m, AnnualWageCap: 90_600m),
        new BurdenComponent("Health insurance (employer)", 0.073m, AnnualWageCap: 62_100m),
        new BurdenComponent("Unemployment insurance (employer)", 0.013m, AnnualWageCap: 90_600m),
        new BurdenComponent("Long-term care insurance (employer)", 0.017m, AnnualWageCap: 62_100m),
    ],
    employeeDeductions:
    [
        new DeductionComponent("Pension insurance (employee)", 0.093m, AnnualWageCap: 90_600m),
        new DeductionComponent("Health insurance (employee)", 0.073m, AnnualWageCap: 62_100m),
        new DeductionComponent("Unemployment insurance (employee)", 0.013m, AnnualWageCap: 90_600m),
        new DeductionComponent("Long-term care insurance (employee)", 0.017m, AnnualWageCap: 62_100m),
    ],
    requiredDocuments:
    [
        new RequiredDocument(DocumentKind.IdentityProof, "Passport or national ID", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.TaxRegistration, "Steuer-ID (tax identification number)", ExpiryTracked: false),
        new RequiredDocument(DocumentKind.SocialSecurityRegistration, "Sozialversicherungsnummer", ExpiryTracked: false),
        new RequiredDocument(DocumentKind.HealthInsuranceProof, "Krankenversicherung enrolment", ExpiryTracked: false),
    ]);
