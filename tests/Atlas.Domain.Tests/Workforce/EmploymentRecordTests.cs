using Atlas.Domain;
using Atlas.Domain.Workforce;

namespace Atlas.Domain.Tests.Workforce;

public class EmploymentRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    private static EmploymentRecord Create(
        string? title = "Engineer", decimal salary = 60_000m,
        string currency = "EUR", decimal hours = 40) =>
        EmploymentRecord.Create(
            Guid.NewGuid(), new DateOnly(2026, 3, 1), title, salary, currency, hours,
            Guid.NewGuid(), Now);

    [Fact]
    public void Creates_with_valid_terms()
    {
        var record = Create();

        Assert.Equal("Engineer", record.JobTitle);
        Assert.Equal(60_000m, record.AnnualSalary);
    }

    [Theory]
    [InlineData(null, 60_000, "EUR", 40)]     // no title
    [InlineData("", 60_000, "EUR", 40)]       // blank title
    [InlineData("Engineer", 0, "EUR", 40)]    // zero salary
    [InlineData("Engineer", -1, "EUR", 40)]   // negative salary
    [InlineData("Engineer", 60_000, "eur", 40)] // lowercase currency
    [InlineData("Engineer", 60_000, "EURO", 40)] // 4-letter currency
    [InlineData("Engineer", 60_000, "EUR", 0)]  // zero hours
    [InlineData("Engineer", 60_000, "EUR", 81)] // >80 hours
    public void Rejects_invalid_terms(string? title, decimal salary, string currency, decimal hours)
    {
        Assert.Throws<DomainRuleException>(() => Create(title, salary, currency, hours));
    }
}
