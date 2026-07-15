using Atlas.Domain;
using Atlas.Domain.Orgs;

namespace Atlas.Domain.Tests.Orgs;

public class ClientOrgTests
{
    private static readonly DateTimeOffset SomeInstant =
        new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_name_and_assigns_identity()
    {
        var org = ClientOrg.Create("  Initech GmbH  ", SomeInstant);

        Assert.Equal("Initech GmbH", org.Name);
        Assert.NotEqual(Guid.Empty, org.Id);
        Assert.Equal(SomeInstant, org.CreatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_rejects_missing_name(string? name)
    {
        Assert.Throws<DomainRuleException>(() => ClientOrg.Create(name, SomeInstant));
    }

    [Fact]
    public void Create_rejects_names_beyond_max_length()
    {
        string tooLong = new('x', ClientOrg.MaxNameLength + 1);

        Assert.Throws<DomainRuleException>(() => ClientOrg.Create(tooLong, SomeInstant));
    }

    [Fact]
    public void Rename_applies_the_same_rules_as_creation()
    {
        var org = ClientOrg.Create("Initech", SomeInstant);

        org.Rename("  Initech International  ");
        Assert.Equal("Initech International", org.Name);

        Assert.Throws<DomainRuleException>(() => org.Rename(" "));
    }
}
