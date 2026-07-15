using Atlas.Domain.Access;

namespace Atlas.Domain.Tests.Access;

public class SessionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_session_is_active_until_its_expiry()
    {
        var session = Session.Start(Guid.NewGuid(), "hash", Now);

        Assert.True(session.IsActive(Now));
        Assert.True(session.IsActive(Now + Session.Lifetime - TimeSpan.FromSeconds(1)));
        Assert.False(session.IsActive(Now + Session.Lifetime));
    }

    [Fact]
    public void Revocation_ends_the_session_immediately()
    {
        var session = Session.Start(Guid.NewGuid(), "hash", Now);

        session.Revoke(Now + TimeSpan.FromMinutes(5));

        Assert.False(session.IsActive(Now + TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public void Revoking_twice_is_a_safe_no_op_that_keeps_the_first_timestamp()
    {
        var session = Session.Start(Guid.NewGuid(), "hash", Now);

        session.Revoke(Now + TimeSpan.FromMinutes(1));
        session.Revoke(Now + TimeSpan.FromMinutes(9));

        Assert.Equal(Now + TimeSpan.FromMinutes(1), session.RevokedAtUtc);
    }
}
