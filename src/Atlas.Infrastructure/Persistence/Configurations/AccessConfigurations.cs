using Atlas.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Email)
            .HasConversion(email => email.Value, value => EmailAddress.From(value))
            .HasMaxLength(EmailAddress.MaxLength);

        // WHY: uniqueness lives in the DATABASE, not just in RegisterOrg's check.
        // The application-level check gives a friendly error; this index closes the
        // race two concurrent signups can win. Belt and suspenders, deliberately.
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.DisplayName)
            .HasMaxLength(User.MaxDisplayNameLength)
            .IsRequired();

        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();

        // GOTCHA: EF Core does NOT map getter-only auto-properties by convention —
        // it silently skips them, and the column simply never exists. Every
        // read-only property below is mapped EXPLICITLY. The ModelMappingTests
        // fitness test now fails the build if any mapped entity grows a public
        // property EF quietly ignores (that is how this very bug was caught).
        builder.Property(u => u.IsAtlasOps);
        builder.Property(u => u.CreatedAtUtc);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.HasIndex(m => new { m.UserId, m.OrgId }).IsUnique();
        builder.HasIndex(m => m.OrgId);

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(32);

        // Read-only property: must be mapped explicitly (see GOTCHA above).
        builder.Property(m => m.CreatedAtUtc);
    }
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TokenHash).HasMaxLength(64).IsRequired();

        // The auth filter's one query per request: token hash → session.
        builder.HasIndex(s => s.TokenHash).IsUnique();

        builder.HasIndex(s => s.UserId);

        // Read-only properties: must be mapped explicitly (see GOTCHA above).
        // Without these lines the session's expiry NEVER REACHED THE DATABASE and
        // every token read back as expired-at-year-one. Silent schema loss.
        builder.Property(s => s.CreatedAtUtc);
        builder.Property(s => s.ExpiresAtUtc);
    }
}
