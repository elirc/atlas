using Atlas.Domain;
using Atlas.Domain.Countries;

namespace Atlas.Domain.Tests.Countries;

public class CountryTests
{
    [Fact]
    public void Constructs_with_valid_inputs_and_trims_name()
    {
        var country = new Country(CountryCode.From("DEU"), "  Germany ", "EUR");

        Assert.Equal("Germany", country.Name);
        Assert.Equal("EUR", country.Currency);
    }

    [Theory]
    [InlineData("", "EUR")]
    [InlineData("Germany", "eur")]
    [InlineData("Germany", "EURO")]
    [InlineData("Germany", "")]
    public void Rejects_invalid_name_or_currency(string name, string currency)
    {
        Assert.Throws<DomainRuleException>(
            () => new Country(CountryCode.From("DEU"), name, currency));
    }
}
