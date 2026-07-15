using Atlas.Domain;
using Atlas.Domain.Access;
using Xunit;

namespace Atlas.Domain.Tests.Access;

public class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static ApiKey Issue(params string[] scopes) =>
        ApiKey.Issue(Guid.NewGuid(), "CI integration", "atk_ab12cd", "hash", scopes, Now);

    [Fact]
    public void An_exact_scope_matches()
    {
        ApiKey key = Issue("read:workers", "write:invoices");

        Assert.True(key.HasScope("read:workers"));
        Assert.True(key.HasScope("write:invoices"));
        Assert.False(key.HasScope("write:workers"));
        Assert.False(key.HasScope("read:invoices"));
    }

    [Fact]
    public void A_verb_wildcard_matches_every_resource_for_that_verb()
    {
        ApiKey key = Issue("read:*");

        Assert.True(key.HasScope("read:workers"));
        Assert.True(key.HasScope("read:invoices"));
        Assert.False(key.HasScope("write:invoices")); // wildcard is per-verb, not god-mode
    }

    [Fact]
    public void A_star_scope_matches_anything()
    {
        ApiKey key = Issue("*");

        Assert.True(key.HasScope("read:workers"));
        Assert.True(key.HasScope("write:payroll"));
    }

    [Fact]
    public void Scope_matching_is_case_insensitive()
    {
        ApiKey key = Issue("Read:Workers");

        Assert.True(key.HasScope("read:workers"));
    }

    [Fact]
    public void A_key_with_no_scopes_is_rejected()
    {
        // A credential that can do nothing is a mistake, not a valid least-privilege key.
        Assert.Throws<DomainRuleException>(() => Issue());
    }

    [Fact]
    public void A_key_needs_a_name()
    {
        Assert.Throws<DomainRuleException>(() =>
            ApiKey.Issue(Guid.NewGuid(), "  ", "atk_x", "hash", ["read:workers"], Now));
    }

    [Fact]
    public void Revoking_is_idempotent_and_deactivates()
    {
        ApiKey key = Issue("read:workers");
        Assert.True(key.IsActive);

        key.Revoke(Now);
        DateTimeOffset firstRevocation = key.RevokedAtUtc!.Value;

        key.Revoke(Now.AddHours(1)); // second click
        Assert.Equal(firstRevocation, key.RevokedAtUtc); // unchanged
        Assert.False(key.IsActive);
    }
}
