namespace Atlas.Application.Abstractions;

/// <summary>Password hashing port. The domain stores the verifier; this makes and checks it.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string storedHash);
}
