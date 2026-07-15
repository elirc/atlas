namespace Atlas.Domain.Countries;

/// <summary>
/// A kind of onboarding/compliance document a country may require of a worker. The set is
/// deliberately country-neutral (identity, tax, right-to-work…) — a country's rule pack declares
/// WHICH kinds it requires; the kind itself is shared vocabulary.
/// </summary>
public enum DocumentKind
{
    IdentityProof = 1,
    TaxRegistration = 2,
    WorkAuthorization = 3,
    BankMandate = 4,
    SocialSecurityRegistration = 5,
    HealthInsuranceProof = 6,
}

/// <summary>
/// A document a country's rule pack requires before a worker is fully compliant. When
/// <see cref="ExpiryTracked"/> is true the document must carry an expiry date and is watched by the
/// expiring-documents view (a work permit expires; a bank mandate does not).
/// </summary>
public sealed record RequiredDocument(DocumentKind Kind, string Description, bool ExpiryTracked);
