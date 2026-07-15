// NOTE: namespace is Atlas.Domain.Tests (not ...Tests.Money). A namespace named `Money`
// under Atlas.Domain.Tests would shadow the Money TYPE for every simple-name reference in
// this folder (CS0118). Money is a root-level domain type; its tests sit at the test root.
namespace Atlas.Domain.Tests;

public class CurrencyCodeTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("usd")]
    [InlineData("  Eur  ")]
    public void Accepts_alpha3_in_any_case_and_normalizes(string raw)
    {
        CurrencyCode code = CurrencyCode.From(raw);

        Assert.Equal(raw.Trim().ToUpperInvariant(), code.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("US")]        // alpha-2
    [InlineData("USDD")]      // too long
    [InlineData("U5D")]       // digit
    [InlineData("€UR")]       // non-ASCII
    [InlineData("Dollar")]    // a name, not a code
    public void Rejects_everything_that_is_not_alpha3(string? raw)
    {
        Assert.Throws<DomainRuleException>(() => CurrencyCode.From(raw));
    }

    [Fact]
    public void Value_equality_holds_regardless_of_input_casing()
    {
        // Two codes parsed from different strings are the SAME currency — which is why a
        // CurrencyCode can be part of a Money's identity and an EF key/converter.
        Assert.Equal(CurrencyCode.From("php"), CurrencyCode.From("PHP"));
    }
}
