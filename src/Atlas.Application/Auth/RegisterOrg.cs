using Atlas.Application.Abstractions;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Auth;

public sealed record RegisteredOrg(ClientOrg Org, User User, string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Sign-up: a new client org, its first user (OrgAdmin), and a live session —
/// atomically. Either the whole account exists afterwards, or none of it does.
/// </summary>
public sealed class RegisterOrg(
    IClientOrgRepository orgs,
    IUserRepository users,
    IMembershipRepository memberships,
    ISessionRepository sessions,
    IPasswordHasher hasher,
    ISessionTokens tokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public const int MinPasswordLength = 10;

    public async Task<RegisteredOrg> ExecuteAsync(
        string? orgName,
        string? email,
        string? password,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var address = EmailAddress.From(email);

        if (password is null || password.Length < MinPasswordLength)
        {
            // WHY: length is the only rule. Composition rules ("one symbol, one
            // digit") measurably push users toward Password1! — NIST 800-63B
            // agrees. Length + a breach check (S13 backlog) beat complexity theater.
            throw new DomainRuleException(
                $"Password must be at least {MinPasswordLength} characters.");
        }

        if (await users.FindByEmailAsync(address, cancellationToken) is not null)
        {
            throw new DomainRuleException("That email address is already registered.");
        }

        DateTimeOffset now = clock.GetUtcNow();

        var org = ClientOrg.Create(orgName, now);
        var user = User.Create(address, displayName, hasher.Hash(password), now);
        var membership = Membership.Grant(user.Id, org.Id, Role.OrgAdmin, now);

        (string token, string hash) = tokens.Mint();
        var session = Session.Start(user.Id, hash, now);

        await orgs.AddAsync(org, cancellationToken);
        await users.AddAsync(user, cancellationToken);
        await memberships.AddAsync(membership, cancellationToken);
        await sessions.AddAsync(session, cancellationToken);

        // LEARN: four writes, ONE commit. If the membership insert failed, no
        // half-org would exist — this is IUnitOfWork earning its keep, and it is
        // the difference between "signup failed, try again" and a support ticket.
        await unitOfWork.CommitAsync(cancellationToken);

        return new RegisteredOrg(org, user, token, session.ExpiresAtUtc);
    }
}
