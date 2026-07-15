using Atlas.Domain;
using Atlas.Domain.Expenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseClaimConfiguration : IEntityTypeConfiguration<ExpenseClaim>
{
    public void Configure(EntityTypeBuilder<ExpenseClaim> builder)
    {
        builder.ToTable("expense_claims");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // The SHARED approval sub-state, stored by NAME in the "Status" column (ADR-0006's stance:
        // a string survives enum-renumbering and reads in a DB client). NOTE the column is named
        // "Status" though the property is `Approval` — the consumer-facing ExpenseClaim.Status is a
        // COMPUTED projection (Approval + ReimbursedAtUtc) and is declared unmapped in
        // ModelMappingTests. The stored truth is Approval; Reimbursed is ReimbursedAtUtc != null.
        builder.Property(c => c.Approval)
            .HasColumnName("Status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Category)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.ReceiptUrl).HasMaxLength(ExpenseClaim.MaxReceiptUrlLength);
        builder.Property(c => c.ReceiptFilename).HasMaxLength(ExpenseClaim.MaxReceiptFilenameLength);
        builder.Property(c => c.Note).HasMaxLength(ExpenseClaim.MaxNoteLength);
        builder.Property(c => c.DecisionReason).HasMaxLength(ExpenseClaim.MaxNoteLength);

        // Read-only properties: mapped explicitly (the S02 lesson; ModelMappingTests enforces).
        builder.Property(c => c.WorkerId);
        builder.Property(c => c.OrgId);
        builder.Property(c => c.RequestedByUserId);
        builder.Property(c => c.CreatedAtUtc);
        builder.Property(c => c.SubmittedAtUtc);
        builder.Property(c => c.DecidedByUserId);
        builder.Property(c => c.DecidedAtUtc);
        builder.Property(c => c.ReimbursedAtUtc);

        // Money as an owned value object — one amount column + one currency column (the S04/S07
        // idiom). GOTCHA: decimal is TEXT on SQLite — persist and read, but NEVER ORDER BY/SUM in
        // SQL. Nothing sums expense amounts in SQL this sprint.
        builder.OwnsOne(c => c.Amount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("amount")
                .HasPrecision(14, 2);

            money.Property(m => m.Currency)
                .HasConversion(code => code.Value, value => CurrencyCode.From(value))
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsFixedLength();
        });

        // The org-scoped newest-first list IS this index (ADR-0002 keyset).
        builder.HasIndex(c => new { c.OrgId, c.CreatedAtUtc, c.Id });

        // WHY: the approval inbox reads submitted claims per org — this index is that read path.
        builder.HasIndex(c => new { c.OrgId, c.Approval });
    }
}
