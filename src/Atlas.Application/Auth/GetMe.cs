using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;

namespace Atlas.Application.Auth;

public sealed record Me(User User, Membership? Membership);

public sealed class GetMe(
    ICurrentContext context,
    IUserRepository users,
    IMembershipRepository memberships)
{
    public async Task<Me> ExecuteAsync(CancellationToken cancellationToken)
    {
        context.RequireUser();

        User user = await users.FindAsync(context.UserId, cancellationToken)
            ?? throw new UnauthenticatedException();

        Membership? membership =
            await memberships.FindForUserAsync(user.Id, cancellationToken);

        return new Me(user, membership);
    }
}
