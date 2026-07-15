namespace Atlas.Domain.Orgs;

/// <summary>
/// A client organization — the tenant boundary of Atlas. Everything a client owns
/// (workers, quotes, payroll) hangs off exactly one ClientOrg.
/// </summary>
public sealed class ClientOrg
{
    public const int MaxNameLength = 200;

    public Guid Id { get; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }

    private ClientOrg(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public static ClientOrg Create(string? name, DateTimeOffset createdAtUtc)
    {
        // LEARN: entities are born valid. There is no ClientOrg instance anywhere in
        // the process with a bad name — so no downstream code ever re-validates.
        // Compare with the anemic alternative: public setters + a validator someone
        // must remember to call.
        return new ClientOrg(Guid.NewGuid(), ValidName(name), createdAtUtc);
    }

    public void Rename(string? name) => Name = ValidName(name);

    private static string ValidName(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("Organization name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainRuleException($"Organization name exceeds {MaxNameLength} characters.");
        }

        return trimmed;
    }

    // WHY: EF Core materialization only.
    private ClientOrg()
    {
        Name = null!;
    }
}
