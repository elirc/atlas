using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Leave;

namespace Atlas.Application.Leave;

/// <summary>
/// Define an org's leave policy for one country. Enforces the country statutory minimum
/// (from the rule pack) and one-policy-per-country, then writes the policy and an audit
/// row in ONE commit.
/// </summary>
public sealed class CreateLeavePolicy(
    ILeavePolicyRepository policies,
    ICountryRepository countries,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<LeavePolicy> ExecuteAsync(
        string? countryCode,
        decimal annualEntitlementDays,
        decimal carryoverCapDays,
        CancellationToken cancellationToken)
    {
        // WHY: setting compliance policy is an ADMIN act. HR managers submit and decide
        // individual leave; the RULES those decisions run against are an org-admin duty.
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        CountryCode code = CountryCode.From(countryCode);

        // The country must be one Atlas employs in (a seeded reference row) — matches
        // HireWorker/CreateQuote: a well-formed but unsupported code is a 422, not a 404.
        _ = await countries.FindAsync(code, cancellationToken)
            ?? throw new DomainRuleException($"Atlas does not support employment in '{code}' yet.");

        if (await policies.FindForAsync(orgId, code, cancellationToken) is not null)
        {
            // WHY: one policy per org per country. A second is almost always a mistake;
            // editing the existing one is a deliberate future workflow, not a silent upsert.
            throw new DomainRuleException($"A leave policy for '{code}' already exists.");
        }

        // The statutory floor lives on the rule pack (S4's "policy as data"). The domain
        // factory enforces entitlement >= this minimum and names it in the 422 message.
        ICountryRulePack pack = CountryRulePacks.For(code);

        DateTimeOffset now = clock.GetUtcNow();
        var policy = LeavePolicy.Define(
            orgId, code, annualEntitlementDays, carryoverCapDays,
            pack.AnnualLeaveMinimumDays, LeaveAccrualCadence.Monthly, context.UserId, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "leave_policy.created", "leave_policy", policy.Id,
            JsonSerializer.Serialize(new
            {
                Country = code.Value,
                policy.AnnualEntitlementDays,
                policy.CarryoverCapDays,
                Cadence = policy.AccrualCadence.ToString(),
            }),
            now));

        await policies.AddAsync(policy, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return policy;
    }
}
