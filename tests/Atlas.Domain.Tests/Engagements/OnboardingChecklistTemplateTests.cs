using Atlas.Domain.Countries;
using Atlas.Domain.Engagements;

namespace Atlas.Domain.Tests.Engagements;

public class OnboardingChecklistTemplateTests
{
    [Fact]
    public void Every_country_gets_the_universal_items_first()
    {
        var items = OnboardingChecklistTemplate.For(CountryCode.From("USA"));

        // USA has no country-specific items — just the three universal ones, in order.
        Assert.Equal(new[] { "contract-signed", "id-verified", "bank-details" }, items);
    }

    [Fact]
    public void Germany_adds_the_works_council_notification()
    {
        var items = OnboardingChecklistTemplate.For(CountryCode.From("DEU"));

        Assert.Contains("works-council-notification", items);
        // Country-specific items come AFTER the universal ones.
        Assert.Equal("works-council-notification", items[^1]);
    }

    [Fact]
    public void Philippines_adds_the_sss_registration()
    {
        var items = OnboardingChecklistTemplate.For(CountryCode.From("PHL"));

        Assert.Contains("sss-registration", items);
    }

    [Fact]
    public void An_unmodelled_country_gets_only_the_universal_items()
    {
        var items = OnboardingChecklistTemplate.For(CountryCode.From("FRA"));

        Assert.Equal(OnboardingChecklistTemplate.UniversalItems, items);
    }
}
