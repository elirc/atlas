using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class WorkerConfiguration : IEntityTypeConfiguration<Worker>
{
    public void Configure(EntityTypeBuilder<Worker> builder)
    {
        builder.ToTable("workers");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.FullName)
            .HasMaxLength(Worker.MaxNameLength)
            .IsRequired();

        builder.Property(w => w.ContactEmail)
            .HasConversion(email => email.Value, value => EmailAddress.From(value))
            .HasMaxLength(EmailAddress.MaxLength);

        builder.Property(w => w.Country)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        // Read-only properties: mapped explicitly (the S02 lesson; ModelMappingTests enforces).
        builder.Property(w => w.OrgId);
        builder.Property(w => w.HiredOn);
        builder.Property(w => w.CreatedAtUtc);

        // WHY: the org-scoped newest-first list IS this index.
        builder.HasIndex(w => new { w.OrgId, w.CreatedAtUtc, w.Id });

        // ProbationEndsOn is computed, not stored — EF must be told to ignore it.
        builder.Ignore(w => w.ProbationEndsOn);
    }
}

internal sealed class EmploymentRecordConfiguration : IEntityTypeConfiguration<EmploymentRecord>
{
    public void Configure(EntityTypeBuilder<EmploymentRecord> builder)
    {
        builder.ToTable("employment_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.JobTitle)
            .HasMaxLength(EmploymentRecord.MaxTitleLength)
            .IsRequired();

        // GOTCHA: decimal on SQLite is stored as TEXT — fine for storage and
        // in-memory math, but never ORDER BY or SUM it in SQL. Payroll (S7) does
        // its arithmetic in the Domain for exactly this class of reason.
        builder.Property(r => r.AnnualSalary).HasPrecision(14, 2);
        builder.Property(r => r.WeeklyHours).HasPrecision(4, 1);

        builder.Property(r => r.SalaryCurrency).HasMaxLength(3).IsFixedLength().IsRequired();

        // Read-only properties: mapped explicitly.
        builder.Property(r => r.WorkerId);
        builder.Property(r => r.EffectiveFrom);
        builder.Property(r => r.CreatedByUserId);
        builder.Property(r => r.CreatedAtUtc);

        // WHY: one statement of terms per worker per effective day — the domain
        // checks it for a friendly message; the index makes it TRUE under races.
        builder.HasIndex(r => new { r.WorkerId, r.EffectiveFrom }).IsUnique();
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Action).HasMaxLength(80).IsRequired();
        builder.Property(e => e.SubjectType).HasMaxLength(40).IsRequired();
        builder.Property(e => e.DetailsJson).IsRequired();

        // Read-only properties: mapped explicitly.
        builder.Property(e => e.OrgId);
        builder.Property(e => e.ActorUserId);
        builder.Property(e => e.SubjectId);
        builder.Property(e => e.AtUtc);

        // The org-scoped newest-first read; subject index for entity timelines.
        builder.HasIndex(e => new { e.OrgId, e.AtUtc, e.Id });
        builder.HasIndex(e => new { e.SubjectType, e.SubjectId });
    }
}
