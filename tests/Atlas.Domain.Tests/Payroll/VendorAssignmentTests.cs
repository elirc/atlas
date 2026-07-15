using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Domain.Tests.Payroll;

public class VendorAssignmentTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly CountryCode Deu = CountryCode.From("DEU");
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_captures_the_effective_dated_choice()
    {
        VendorAssignment assignment = VendorAssignment.Create(
            Org, Deu, new DateOnly(2026, 8, 1), PayrollVendor.LedgerPay, Actor, T0);

        Assert.Equal(Org, assignment.OrgId);
        Assert.Equal("DEU", assignment.Country.Value);
        Assert.Equal(new DateOnly(2026, 8, 1), assignment.EffectiveFrom);
        Assert.Equal(PayrollVendor.LedgerPay, assignment.Vendor);
    }

    [Fact]
    public void An_assignment_needs_an_org()
    {
        Assert.Throws<DomainRuleException>(() =>
            VendorAssignment.Create(Guid.Empty, Deu, new DateOnly(2026, 8, 1), PayrollVendor.LedgerPay, Actor, T0));
    }

    [Fact]
    public void An_undefined_vendor_is_refused()
    {
        // A future vendor name a client invents (cast past the enum) is a well-formed request the
        // business must refuse — not a silent bad row persisted as text.
        Assert.Throws<DomainRuleException>(() =>
            VendorAssignment.Create(Org, Deu, new DateOnly(2026, 8, 1), (PayrollVendor)99, Actor, T0));
    }
}
