using Atlas.Domain.Countries;
using Atlas.Domain.Leave;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class LeavePolicyConfiguration : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> builder)
    {
        builder.ToTable("leave_policies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Country)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        // WHY: the enum is stored by NAME, not integer. "Monthly" reads better in a DB
        // client and, more importantly, reordering the enum can never silently remap
        // existing rows. The cost is a 20-char string column — cheap insurance.
        builder.Property(p => p.AccrualCadence)
            .HasConversion<string>()
            .HasMaxLength(20);

        // GOTCHA: decimal on SQLite is TEXT — fine to store and read, never ORDER BY/SUM
        // in SQL. Balance math sums these in memory (LeaveAccrual), never in the database.
        builder.Property(p => p.AnnualEntitlementDays).HasPrecision(5, 2);
        builder.Property(p => p.CarryoverCapDays).HasPrecision(5, 2);

        // Read-only properties: mapped explicitly (the S02 lesson; ModelMappingTests enforces).
        builder.Property(p => p.OrgId);
        builder.Property(p => p.CreatedByUserId);
        builder.Property(p => p.CreatedAtUtc);

        // WHY: one policy per org per country — the domain checks it for a friendly 422,
        // the unique index makes it TRUE under races (same stance as employment_records).
        builder.HasIndex(p => new { p.OrgId, p.Country }).IsUnique();
    }
}

internal sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("leave_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // GOTCHA: FROM and TO are SQL keywords. Left as "From"/"To" a provider must quote
        // them on every query; explicit column names sidestep the whole class of problem.
        builder.Property(r => r.From).HasColumnName("FromDate");
        builder.Property(r => r.To).HasColumnName("ToDate");

        builder.Property(r => r.WorkingDays).HasPrecision(5, 2);

        builder.Property(r => r.Note).HasMaxLength(LeaveRequest.MaxNoteLength);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Read-only properties: mapped explicitly.
        builder.Property(r => r.WorkerId);
        builder.Property(r => r.OrgId);
        builder.Property(r => r.RequestedByUserId);
        builder.Property(r => r.CreatedAtUtc);
        builder.Property(r => r.SubmittedAtUtc);
        builder.Property(r => r.DecidedByUserId);
        builder.Property(r => r.DecidedAtUtc);

        // The org-scoped newest-first list IS this index.
        builder.HasIndex(r => new { r.OrgId, r.CreatedAtUtc, r.Id });

        // WHY: the balance folds over a worker's APPROVED requests — this index is the read
        // path for both "sum spent" and "does it overlap?".
        builder.HasIndex(r => new { r.WorkerId, r.Status });
    }
}
