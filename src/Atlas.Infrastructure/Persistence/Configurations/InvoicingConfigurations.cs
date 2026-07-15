using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class ContractorConfiguration : IEntityTypeConfiguration<Contractor>
{
    public void Configure(EntityTypeBuilder<Contractor> builder)
    {
        builder.ToTable("contractors");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(Contractor.MaxNameLength).IsRequired();

        builder.Property(c => c.Email)
            .HasConversion(email => email.Value, value => EmailAddress.From(value))
            .HasMaxLength(EmailAddress.MaxLength);

        builder.Property(c => c.Country)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(c => c.PayoutCurrency)
            .HasConversion(code => code.Value, value => CurrencyCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        // Read-only properties: mapped explicitly (ModelMappingTests enforces).
        builder.Property(c => c.OrgId);
        builder.Property(c => c.CreatedAtUtc);

        builder.HasIndex(c => new { c.OrgId, c.CreatedAtUtc, c.Id });
    }
}

internal sealed class ContractorInvoiceConfiguration : IEntityTypeConfiguration<ContractorInvoice>
{
    public void Configure(EntityTypeBuilder<ContractorInvoice> builder)
    {
        builder.ToTable("contractor_invoices");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        // Enum persisted as text — legible in the DB and survives enum renumbering (ADR-0006).
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(i => i.Currency)
            .HasConversion(code => code.Value, value => CurrencyCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        // Read-only + private-set properties: mapped explicitly.
        builder.Property(i => i.OrgId);
        builder.Property(i => i.ContractorId);
        builder.Property(i => i.SubmittedByUserId);
        builder.Property(i => i.CreatedAtUtc);
        builder.Property(i => i.SubmittedAtUtc);
        builder.Property(i => i.HrApprovedByUserId);
        builder.Property(i => i.HrApprovedAtUtc);
        builder.Property(i => i.AdminApprovedByUserId);
        builder.Property(i => i.AdminApprovedAtUtc);
        builder.Property(i => i.VendorReference).HasMaxLength(100);
        builder.Property(i => i.PaidAtUtc);
        builder.Property(i => i.FailureReason).HasMaxLength(300);
        builder.Property(i => i.LastAttemptedAtUtc);

        // The org-scoped newest-first list (ADR-0002 keyset). The status filter rides on top of
        // this ordering in memory-cheap fashion; a status-leading composite index is a future
        // perf tweak recorded in NOTES, not needed at this scale.
        builder.HasIndex(i => new { i.OrgId, i.CreatedAtUtc, i.Id });

        // The invoice owns its lines; deleting an invoice cascades. FK is a shadow column.
        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey("ContractorInvoiceId")
            .OnDelete(DeleteBehavior.Cascade);

        // GOTCHA: Total() is a DOMAIN METHOD, not a property — nothing here to map or Ignore().
        // It folds over the loaded Lines in memory because decimal cannot be SUM'd in SQL on SQLite.
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Description).HasMaxLength(InvoiceLine.MaxDescriptionLength).IsRequired();

        // Money as an owned value object — one amount column + one currency column.
        builder.OwnsOne(l => l.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(14, 2);
            money.Property(m => m.Currency)
                .HasConversion(code => code.Value, value => CurrencyCode.From(value))
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsFixedLength();
        });

        // The FK column "ContractorInvoiceId" (shadow) is indexed by EF's FK convention.
    }
}
