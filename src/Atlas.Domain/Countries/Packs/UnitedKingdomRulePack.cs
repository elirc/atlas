namespace Atlas.Domain.Countries.Packs;

/// <summary>United Kingdom — employer NI is a rate ABOVE the secondary threshold (an allowance).</summary>
public sealed class UnitedKingdomRulePack() : DataDrivenRulePack(
    CountryCode.From("GBR"),
    CurrencyCode.From("GBP"),
    probationMonths: 3,
    annualLeaveMinimumDays: 28m,
    employerBurden:
    [
        new BurdenComponent("Employer National Insurance", 0.138m, AnnualAllowance: 9_100m),
    ],
    employeeDeductions:
    [
        new DeductionComponent("Employee National Insurance", 0.08m, AnnualAllowance: 12_570m),
    ],
    requiredDocuments:
    [
        new RequiredDocument(DocumentKind.IdentityProof, "Passport or biometric residence permit", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.WorkAuthorization, "Right to work confirmation", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.TaxRegistration, "National Insurance number", ExpiryTracked: false),
    ]);
