using Atlas.Application.Abstractions;

namespace Atlas.Application.Auth;

public sealed class Logout(
    ISessionRepository sessions,
    ISessionTokens tokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task ExecuteAsync(string bearerToken, CancellationToken cancellationToken)
    {
        var session = await sessions.FindByTokenHashAsync(
            tokens.HashOf(bearerToken), cancellationToken);

        // WHY: logging out an already-dead session succeeds silently. The client's
        // goal ("this token must not work anymore") is already true — failing the
        // request would punish retries for being safe.
        session?.Revoke(clock.GetUtcNow());

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
