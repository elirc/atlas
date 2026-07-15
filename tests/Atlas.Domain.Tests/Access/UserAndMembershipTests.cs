using Atlas.Domain;
using Atlas.Domain.Access;

namespace Atlas.Domain.Tests.Access;

public class UserAndMembershipTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void User_creation_trims_name_and_requires_a_verifier()
    {
        var user = User.Create(EmailAddress.From("ada@example.com"), "  Ada  ", "hash", Now);

        Assert.Equal("Ada", user.DisplayName);
        Assert.False(user.IsAtlasOps);
    }

    [Theory]
    [InlineData("", "hash")]
    [InlineData("Ada", "")]
    [InlineData("Ada", "   ")]
    public void User_creation_rejects_missing_name_or_verifier(string name, string hash)
    {
        Assert.Throws<DomainRuleException>(
            () => User.Create(EmailAddress.From("ada@example.com"), name, hash, Now));
    }

    [Fact]
    public void Membership_requires_both_sides_of_the_grant()
    {
        Assert.Throws<DomainRuleException>(
            () => Membership.Grant(Guid.Empty, Guid.NewGuid(), Role.OrgAdmin, Now));
        Assert.Throws<DomainRuleException>(
            () => Membership.Grant(Guid.NewGuid(), Guid.Empty, Role.HrManager, Now));
    }
}
