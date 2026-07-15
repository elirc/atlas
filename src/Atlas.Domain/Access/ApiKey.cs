namespace Atlas.Domain.Access;

/// <summary>
/// A partner credential: an org-scoped, hashed-at-rest key with a set of SCOPES bounding what it may
/// do. Like a session token, only the hash is stored — a leaked table cannot be replayed. Unlike a
/// session, it belongs to a machine, never expires on its own, and is narrowed by scopes rather than
/// a role.
/// </summary>
// LEARN: scopes are least-privilege made concrete. A payroll integration gets `write:payroll`; a
// read-only dashboard gets `read:workers`. The verb:resource shape (plus `verb:*` and `*` wildcards)
// keeps the check trivial while letting a partner hold exactly the authority they need and no more —
// the opposite of handing out a full admin login and hoping.
public sealed class ApiKey
{
    public const string PlaintextPrefix = "atk_";

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public string Name { get; private set; }

    /// <summary>The first few chars of the raw key, kept for display ("atk_ab12…") — never the secret.</summary>
    public string DisplayPrefix { get; private set; }

    public string KeyHash { get; private set; }

    /// <summary>CSV of scopes, e.g. <c>read:workers,write:invoices</c>, or <c>*</c> for full access.</summary>
    public string ScopeCsv { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private ApiKey(
        Guid orgId, string name, string displayPrefix, string keyHash, string scopeCsv, DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        OrgId = orgId;
        Name = name;
        DisplayPrefix = displayPrefix;
        KeyHash = keyHash;
        ScopeCsv = scopeCsv;
        CreatedAtUtc = createdAtUtc;
    }

    public static ApiKey Issue(
        Guid orgId, string? name, string displayPrefix, string keyHash,
        IReadOnlyList<string>? scopes, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("An API key needs a name so it can be recognised and revoked.");
        }

        if (scopes is null or { Count: 0 })
        {
            throw new DomainRuleException("An API key needs at least one scope; a key that can do nothing is a mistake.");
        }

        string csv = string.Join(',', scopes.Select(Normalize));
        return new ApiKey(orgId, name.Trim(), displayPrefix, keyHash, csv, nowUtc);
    }

    public bool IsActive => RevokedAtUtc is null;

    /// <summary>
    /// Does this key hold the required scope? Honors two wildcard forms: <c>*</c> (everything) and
    /// <c>verb:*</c> (every resource for that verb, e.g. <c>write:*</c>).
    /// </summary>
    public bool HasScope(string required)
    {
        string want = Normalize(required);
        string verb = want.Split(':', 2)[0];

        foreach (string held in ScopeCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (held == "*" || held == want || held == $"{verb}:*")
            {
                return true;
            }
        }

        return false;
    }

    public void Revoke(DateTimeOffset nowUtc) => RevokedAtUtc ??= nowUtc; // idempotent

    private static string Normalize(string scope) => scope.Trim().ToLowerInvariant();

    // EF materialization only.
    private ApiKey()
    {
        Name = null!;
        DisplayPrefix = null!;
        KeyHash = null!;
        ScopeCsv = null!;
    }
}
