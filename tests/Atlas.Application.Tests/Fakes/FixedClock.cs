namespace Atlas.Application.Tests.Fakes;

/// <summary>A TimeProvider frozen at a chosen instant.</summary>
// LEARN: time is an input. A test that reads the real clock is a test whose
// assertions age — freeze it and "created just now" becomes an exact equality.
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
