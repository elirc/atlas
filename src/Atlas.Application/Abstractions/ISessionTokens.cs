namespace Atlas.Application.Abstractions;

/// <summary>Mints opaque bearer tokens and derives their at-rest hashes.</summary>
// WHY: the raw token exists exactly twice — in the login response and in the
// client's memory. The database sees only Hash(token); an exfiltrated sessions
// table therefore cannot be replayed. Mint returns both halves so the use case
// never learns how either is produced.
public interface ISessionTokens
{
    (string Token, string Hash) Mint();

    string HashOf(string token);
}
