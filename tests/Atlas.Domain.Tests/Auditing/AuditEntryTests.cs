using Atlas.Domain;
using Atlas.Domain.Auditing;

namespace Atlas.Domain.Tests.Auditing;

public class AuditEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Records_the_full_who_what_when()
    {
        var entry = AuditEntry.Record(
            Guid.NewGuid(), Guid.NewGuid(), " worker.hired ", " worker ",
            Guid.NewGuid(), "{}", Now);

        Assert.Equal("worker.hired", entry.Action);
        Assert.Equal("worker", entry.SubjectType);
        Assert.Equal(Now, entry.AtUtc);
    }

    [Theory]
    [InlineData("", "worker")]
    [InlineData("worker.hired", "")]
    [InlineData("   ", "worker")]
    public void Requires_action_and_subject_type(string action, string subjectType)
    {
        Assert.Throws<DomainRuleException>(() => AuditEntry.Record(
            Guid.NewGuid(), Guid.NewGuid(), action, subjectType, Guid.NewGuid(), "{}", Now));
    }
}
