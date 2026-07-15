using Atlas.Domain;
using Atlas.Domain.Countries;

namespace Atlas.Domain.Tests.Countries;

public class CountryCodeTests
{
    [Theory]
    [InlineData("DEU")]
    [InlineData("deu")]
    [InlineData("  Phl  ")]
    public void Accepts_alpha3_in_any_case_and_normalizes(string raw)
    {
        CountryCode code = CountryCode.From(raw);

        Assert.Equal(raw.Trim().ToUpperInvariant(), code.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DE")]       // alpha-2
    [InlineData("DEUT")]     // too long
    [InlineData("D3U")]      // digits
    [InlineData("DÉU")]      // non-ASCII
    [InlineData("Germany")]  // a name, not a code
    public void Rejects_everything_that_is_not_alpha3(string? raw)
    {
        Assert.Throws<DomainRuleException>(() => CountryCode.From(raw));
    }

    [Fact]
    public void Value_equality_holds_regardless_of_input_casing()
    {
        // LEARN: value objects compare by value. Two codes parsed from different
        // strings are THE SAME country — which is exactly why they can be dictionary
        // keys and EF key types.
        Assert.Equal(CountryCode.From("usa"), CountryCode.From("USA"));
    }
}
