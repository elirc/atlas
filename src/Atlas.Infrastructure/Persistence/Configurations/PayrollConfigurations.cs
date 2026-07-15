using System.Linq.Expressions;
using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class PayCycleConfiguration : IEntityTypeConfiguration<PayCycle>
{
    public void Configure(EntityTypeBuilder<PayCycle> builder)
    {
        builder.ToTable("pay_cycles");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Country)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        // Enum persisted as text — a string "Monthly" is legible in the DB and survives
        // enum-value renumbering, at the cost of a few bytes. (ADR-0006's stance for states.)
        builder.Property(c => c.Frequency).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Read-only properties: mapped explicitly (ModelMappingTests enforces).
        builder.Property(c => c.OrgId);
        builder.Property(c => c.CreatedAtUtc);

        // WHY: one cycle per org per country per cadence — the CreatePayCycle duplicate
        // guard made true under races.
        builder.HasIndex(c => new { c.OrgId, c.Country, c.Frequency }).IsUnique();
    }
}

internal sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("payroll_runs");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.State).HasConversion<string>().HasMaxLength(20).IsRequired();

        // S12: the calculation vendor this run was PINNED to at creation (cycle-in-flight rule).
        builder.Property(r => r.SelectedVendor).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Read-only properties: mapped explicitly.
        builder.Property(r => r.OrgId);
        builder.Property(r => r.PayCycleId);
        builder.Property(r => r.PeriodStart);
        builder.Property(r => r.PeriodEnd);
        builder.Property(r => r.CreatedByUserId);
        builder.Property(r => r.CreatedAtUtc);
        builder.Property(r => r.ExecutedAtUtc);

        // WHY: the natural key. One run per (cycle, period start) — the index is what makes
        // idempotent preparation TRUE under a concurrent double-submit, not just friendly.
        builder.HasIndex(r => new { r.PayCycleId, r.PeriodStart }).IsUnique();

        // The org-scoped newest-first list (ADR-0002 keyset).
        builder.HasIndex(r => new { r.OrgId, r.CreatedAtUtc, r.Id });

        // The run owns its payslips; deleting a run cascades. FK is Payslip.RunId (a real
        // column, also mapped on the Payslip side).
        builder.HasMany(r => r.Payslips)
            .WithOne()
            .HasForeignKey(p => p.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        // GOTCHA: run totals are DOMAIN methods (TotalGross()/TotalNet()/TotalEmployerCost()),
        // not properties — nothing here to map or Ignore(). They fold over the loaded
        // Payslips in memory because decimal cannot be SUM'd in SQL on SQLite.
    }
}

internal sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> builder)
    {
        builder.ToTable("payslips");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // Read-only properties: mapped explicitly.
        builder.Property(p => p.RunId);
        builder.Property(p => p.WorkerId);
        builder.Property(p => p.OrgId);
        builder.Property(p => p.PeriodStart);
        builder.Property(p => p.PeriodEnd);
        builder.Property(p => p.CreatedAtUtc);

        // Money as an owned value object — one amount column + one currency column each.
        OwnMoney(builder, p => p.Gross, "gross");
        OwnMoney(builder, p => p.Net, "net");
        OwnMoney(builder, p => p.EmployerCost, "employer_cost");

        // Payslip owns its lines; deleting a payslip cascades (FK is a shadow "PayslipId").
        builder.HasMany(p => p.Lines)
            .WithOne()
            .HasForeignKey("PayslipId")
            .OnDelete(DeleteBehavior.Cascade);

        // The payslips-by-run read (prompt: "payslips by (RunId)").
        builder.HasIndex(p => p.RunId);
    }

    // GOTCHA: decimal is stored as TEXT on SQLite — fine to persist and read back, but
    // NEVER ORDER BY or SUM these amount columns in SQL. Every payroll total is folded in
    // the Domain (PayrollRun.Total*()), which is the whole reason those are methods.
    private static void OwnMoney(
        EntityTypeBuilder<Payslip> builder,
        Expression<Func<Payslip, Money?>> navigation,
        string columnPrefix)
    {
        builder.OwnsOne(navigation, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName($"{columnPrefix}_amount")
                .HasPrecision(14, 2);

            money.Property(m => m.Currency)
                .HasConversion(code => code.Value, value => CurrencyCode.From(value))
                .HasColumnName($"{columnPrefix}_currency")
                .HasMaxLength(3)
                .IsFixedLength();
        });
    }
}

internal sealed class PayslipLineConfiguration : IEntityTypeConfiguration<PayslipLine>
{
    public void Configure(EntityTypeBuilder<PayslipLine> builder)
    {
        builder.ToTable("payslip_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Label).HasMaxLength(120).IsRequired();
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.OwnsOne(l => l.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(14, 2);
            money.Property(m => m.Currency)
                .HasConversion(code => code.Value, value => CurrencyCode.From(value))
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsFixedLength();
        });

        // The FK column "PayslipId" (shadow) is indexed by EF's FK convention.
    }
}
