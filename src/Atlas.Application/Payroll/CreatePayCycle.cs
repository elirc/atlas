using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>
/// Open a payroll schedule for one org in one country at one cadence. The country must be
/// one Atlas actually employs in (a seeded reference row) AND have a rule pack; a duplicate
/// cycle for the same country+frequency is refused.
/// </summary>
public sealed class CreatePayCycle(
    IPayCycleRepository cycles,
    ICountryRepository countries,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<PayCycle> ExecuteAsync(
        string? countryCode,
        string? frequency,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        CountryCode code = CountryCode.From(countryCode);

        // Same stance as HireWorker/CreateQuote: a well-formed but unsupported country is a
        // business "no" (422), not a 404.
        Country country = await countries.FindAsync(code, cancellationToken)
            ?? throw new DomainRuleException($"Atlas does not support employment in '{code}' yet.");

        // The rule pack must exist too — you cannot run payroll for a country we can't price.
        _ = CountryRulePacks.For(code);

        PayFrequency parsed = ParseFrequency(frequency);

        // WHY: one cycle per org per country per cadence. A second "German monthly cycle"
        // would split that country's runs across two schedules and break the natural key's
        // meaning. Refuse the duplicate rather than silently create ambiguity.
        if (await cycles.FindForCountryAsync(orgId, code, parsed, cancellationToken) is not null)
        {
            throw new DomainRuleException(
                $"A {parsed} pay cycle already exists for {code} in this org.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        var cycle = PayCycle.Create(orgId, code, parsed, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "payroll.cycle_created", "pay_cycle", cycle.Id,
            JsonSerializer.Serialize(new { Country = code.Value, Frequency = parsed.ToString() }),
            now));

        await cycles.AddAsync(cycle, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return cycle;
    }

    // WHY 422 for an unknown frequency: "Atlas only runs monthly" is a business statement,
    // not a malformed-request one. The one supported value parses; everything else is a
    // capability we don't have yet — the same shape as an unsupported country.
    private static PayFrequency ParseFrequency(string? raw) =>
        Enum.TryParse(raw, ignoreCase: true, out PayFrequency frequency)
            && Enum.IsDefined(frequency)
            ? frequency
            : throw new DomainRuleException($"'{raw}' is not a supported pay frequency.");
}
