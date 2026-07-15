using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class VendorAssignmentConfiguration : IEntityTypeConfiguration<VendorAssignment>
{
    public void Configure(EntityTypeBuilder<VendorAssignment> builder)
    {
        builder.ToTable("vendor_assignments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Country)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        // Enum persisted as text — legible in the DB and survives enum renumbering (ADR-0006).
        builder.Property(a => a.Vendor).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Read-only properties: mapped explicitly (ModelMappingTests enforces).
        builder.Property(a => a.OrgId);
        builder.Property(a => a.EffectiveFrom);
        builder.Property(a => a.CreatedByUserId);
        builder.Property(a => a.CreatedAtUtc);

        // WHY: one assignment per org per country per effective day — the AssignPayrollVendor
        // duplicate guard made TRUE under a race (ADR-0004, effective-dating's second application).
        // This is also the read path (resolve the governing/pending vendor for an org+country).
        builder.HasIndex(a => new { a.OrgId, a.Country, a.EffectiveFrom }).IsUnique();
    }
}
