using Atlas.Domain;
using Atlas.Domain.Access;

namespace Atlas.Domain.Tests.Access;

public class EmailAddressTests
{
    [Theory]
    [InlineData("ada@example.com", "ada@example.com")]
    [InlineData("  Ada@Example.COM  ", "ada@example.com")]
    [InlineData("a@b.co", "a@b.co")]
    public void Normalizes_case_and_whitespace(string raw, string expected)
    {
        Assert.Equal(expected, EmailAddress.From(raw).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("@nothing-before")]
    [InlineData("nothing-after@")]
    [InlineData("two@@ats.com")]
    [InlineData("has space@example.com")]
    public void Rejects_unusable_shapes(string? raw)
    {
        Assert.Throws<DomainRuleException>(() => EmailAddress.From(raw));
    }

    [Fact]
    public void Differently_cased_inputs_are_the_same_address()
    {
        // LEARN: normalization at construction is what makes the unique index on
        // email actually unique — two casings can't smuggle in two accounts.
        Assert.Equal(EmailAddress.From("Ada@Example.com"), EmailAddress.From("ada@EXAMPLE.com"));
    }
}
