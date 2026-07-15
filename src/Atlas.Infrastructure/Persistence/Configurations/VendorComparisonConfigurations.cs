using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class VendorComparisonReportConfiguration : IEntityTypeConfiguration<VendorComparisonReport>
{
    public void Configure(EntityTypeBuilder<VendorComparisonReport> builder)
    {
        builder.ToTable("vendor_comparison_reports");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Country)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(r => r.PrimaryVendor).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ShadowVendor).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Read-only properties: mapped explicitly. All deltas/counters are INTEGERS (minor units and
        // plain ints) — no decimal columns here, so none of the "decimal can't be SUM'd on SQLite"
        // trouble the payroll totals had to fold around. The redaction-to-deltas choice pays off
        // twice: no sensitive net pay stored, and every summary aggregate is integer-safe.
        builder.Property(r => r.OrgId);
        builder.Property(r => r.RunId);
        builder.Property(r => r.ToleranceMinorUnits);
        builder.Property(r => r.WorkerCount);
        builder.Property(r => r.DivergentCount);
        builder.Property(r => r.MaxAbsNetDeltaMinor);
        builder.Property(r => r.CreatedAtUtc);

        // IsClean is a computed convenience (DivergentCount == 0), never a column. Ignored here and
        // declared in ModelMappingTests' DeliberatelyUnmapped list, where a reviewer sees every such
        // exception — a computed property belongs on that list; a stored counter never does.
        builder.Ignore(r => r.IsClean);

        // The report owns its per-worker delta lines; deleting a report cascades. FK is a shadow column.
        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey("VendorComparisonReportId")
            .OnDelete(DeleteBehavior.Cascade);

        // FindLatestForRun: newest report per run (within an org). Index the run + recency.
        builder.HasIndex(r => new { r.RunId, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.OrgId, r.CreatedAtUtc, r.Id });
    }
}

internal sealed class VendorComparisonLineConfiguration : IEntityTypeConfiguration<VendorComparisonLine>
{
    public void Configure(EntityTypeBuilder<VendorComparisonLine> builder)
    {
        builder.ToTable("vendor_comparison_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.WorkerId);

        // Deltas as whole-minor-unit longs (INTEGER). Deliberately NOT decimals: they are the ONLY
        // figures the comparison persists (net pay is redacted away in VendorComparison.DeltaOf), and
        // integers keep the summary folds and any future ORDER BY off SQLite's decimal-as-TEXT rocks.
        builder.Property(l => l.GrossDeltaMinor);
        builder.Property(l => l.NetDeltaMinor);
        builder.Property(l => l.EmployerCostDeltaMinor);

        builder.Property(l => l.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(l => l.Divergent);

        // The FK column "VendorComparisonReportId" (shadow) is indexed by EF's FK convention.
    }
}
