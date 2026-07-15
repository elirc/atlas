using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Contractors;

/// <summary>
/// Register a contractor an org engages: name, contact email, country, and the currency Atlas
/// pays them in. The country must be one Atlas actually operates in (a seeded reference row),
/// exactly like HireWorker/CreateQuote — a well-formed but unsupported code is a business "no"
/// (422), not a 404.
/// </summary>
public sealed class CreateContractor(
    IContractorRepository contractors,
    ICountryRepository countries,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<Contractor> ExecuteAsync(
        string? name,
        string? email,
        string? countryCode,
        string? payoutCurrency,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        CountryCode code = CountryCode.From(countryCode);
        _ = await countries.FindAsync(code, cancellationToken)
            ?? throw new DomainRuleException($"Atlas does not operate in '{code}' yet.");

        var contractorEmail = EmailAddress.From(email);
        var currency = CurrencyCode.From(payoutCurrency);

        DateTimeOffset now = clock.GetUtcNow();
        var contractor = Contractor.Create(orgId, name, contractorEmail, code, currency, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "contractor.created", "contractor", contractor.Id,
            JsonSerializer.Serialize(new
            {
                contractor.Name,
                Email = contractorEmail.Value,
                Country = code.Value,
                PayoutCurrency = currency.Value,
            }),
            now));

        await contractors.AddAsync(contractor, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return contractor;
    }
}
