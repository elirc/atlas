using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Engagements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class EngagementConfiguration : IEntityTypeConfiguration<Engagement>
{
    public void Configure(EntityTypeBuilder<Engagement> builder)
    {
        builder.ToTable("engagements");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // WHY: the state is stored as its NAME, not an int — a payroll DBA reading
        // the table sees "Onboarding", and reordering the enum can never silently
        // re-map existing rows. Same call MembershipConfiguration makes for Role.
        builder.Property(e => e.State).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(e => e.Country)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        // CandidateEmail is nullable (set at send-offer). EF lifts the non-nullable
        // converter to the nullable property; nulls pass through untouched.
        builder.Property(e => e.CandidateEmail)
            .HasConversion(new ValueConverter<EmailAddress, string>(
                email => email.Value, value => EmailAddress.From(value)))
            .HasMaxLength(EmailAddress.MaxLength);

        builder.Property(e => e.CandidateName).HasMaxLength(Engagement.MaxCandidateNameLength);

        builder.Property(e => e.RoleTitle)
            .HasMaxLength(Engagement.MaxRoleTitleLength)
            .IsRequired();

        // GOTCHA: decimal on SQLite is TEXT — fine to store and compare in memory,
        // never ORDER BY or SUM in SQL (payroll does its math in the Domain, S7).
        builder.Property(e => e.AnnualSalary).HasPrecision(14, 2);
        builder.Property(e => e.WeeklyHours).HasPrecision(4, 1);
        builder.Property(e => e.SalaryCurrency).HasMaxLength(3).IsFixedLength().IsRequired();

        // GOTCHA: EF Core does NOT map getter-only auto-properties by convention —
        // every read-only property is mapped EXPLICITLY or ModelMappingTests fails.
        builder.Property(e => e.OrgId);
        builder.Property(e => e.QuoteId);
        builder.Property(e => e.CreatedWorkerId);
        builder.Property(e => e.CreatedAtUtc);
        builder.Property(e => e.OfferSentAtUtc);
        builder.Property(e => e.AcceptedOn);
        builder.Property(e => e.OnboardingStartedAtUtc);
        builder.Property(e => e.ActivatedAtUtc);
        builder.Property(e => e.WithdrawnAtUtc);

        // The org-scoped newest-first list IS this index.
        builder.HasIndex(e => new { e.OrgId, e.CreatedAtUtc, e.Id });

        // The checklist is OWNED — its own table, but no aggregate root of its own.
        // Owned collections load with the parent, so FindInOrg returns a complete
        // aggregate the transition guards can trust.
        builder.OwnsMany(e => e.Checklist, cb =>
        {
            cb.ToTable("engagement_checklist_items");
            cb.WithOwner().HasForeignKey("EngagementId");

            cb.HasKey(i => i.Id);
            cb.Property(i => i.Id).ValueGeneratedNever();
            cb.Property(i => i.Name).HasMaxLength(EngagementChecklistItem.MaxNameLength).IsRequired();
            cb.Property(i => i.CompletedAtUtc);

            // IsComplete is derived from CompletedAtUtc — not a column.
            cb.Ignore(i => i.IsComplete);
        });

        // WHY: the get-only Checklist navigation is materialized through its backing
        // field (_checklist) by EF convention — the standard aggregate-owned-collection
        // shape. No setter is exposed, so the field is the only write path.
    }
}
