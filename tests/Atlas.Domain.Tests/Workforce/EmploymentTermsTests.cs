using Atlas.Domain.Workforce;

namespace Atlas.Domain.Tests.Workforce;

public class EmploymentTermsTests
{
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EmploymentRecord Record(string title, decimal salary, int year, int month, int day, int createdMinutesAfterT0 = 0) =>
        EmploymentRecord.Create(
            WorkerId, new DateOnly(year, month, day), title, salary, "EUR", 40,
            Actor, T0.AddMinutes(createdMinutesAfterT0));

    private static readonly EmploymentRecord Hired = Record("Engineer", 60_000m, 2026, 1, 1);
    private static readonly EmploymentRecord Promoted = Record("Senior Engineer", 75_000m, 2026, 6, 1, 10);
    private static readonly EmploymentRecord Raised = Record("Senior Engineer", 80_000m, 2026, 9, 15, 20);

    public static TheoryData<int[]> Permutations => new()
    {
        new[] { 0, 1, 2 },
        new[] { 2, 1, 0 },
        new[] { 1, 2, 0 },
        new[] { 2, 0, 1 },
    };

    [Theory]
    [MemberData(nameof(Permutations))]
    public void Resolution_is_order_independent(int[] order)
    {
        // LEARN: property-style testing without a framework — the invariant
        // (input order is irrelevant) is asserted across explicit permutations.
        EmploymentRecord[] all = [Hired, Promoted, Raised];
        var shuffled = order.Select(i => all[i]).ToList();

        var resolved = EmploymentTerms.Resolve(shuffled, new DateOnly(2026, 7, 1));

        Assert.Equal(75_000m, resolved!.AnnualSalary);
        Assert.Equal("Senior Engineer", resolved.JobTitle);
    }

    [Theory]
    [InlineData(2026, 1, 1, 60_000)]   // exactly on the first record's effective date
    [InlineData(2026, 5, 31, 60_000)]  // day before the promotion
    [InlineData(2026, 6, 1, 75_000)]   // promotion day itself
    [InlineData(2026, 9, 15, 80_000)]  // raise day
    [InlineData(2030, 1, 1, 80_000)]   // far future: latest record governs
    public void As_of_boundaries_are_inclusive_on_effective_from(
        int year, int month, int day, decimal expectedSalary)
    {
        var resolved = EmploymentTerms.Resolve(
            [Raised, Hired, Promoted], new DateOnly(year, month, day));

        Assert.Equal(expectedSalary, resolved!.AnnualSalary);
    }

    [Fact]
    public void Before_the_first_record_there_are_no_terms()
    {
        // WHY: null, not an exception — "no terms yet" is a fact about the date,
        // not an error in the question. Callers decide what absence means.
        Assert.Null(EmploymentTerms.Resolve(
            [Hired, Promoted], new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void Same_day_records_resolve_to_the_latest_created()
    {
        // The DB forbids duplicate EffectiveFrom per worker, but Resolve is pure
        // and must be total over any input: latest CreatedAtUtc wins.
        var first = Record("Engineer", 60_000m, 2026, 3, 1, 0);
        var correction = Record("Engineer II", 62_000m, 2026, 3, 1, 5);

        var resolved = EmploymentTerms.Resolve(
            [first, correction], new DateOnly(2026, 3, 2));

        Assert.Equal("Engineer II", resolved!.JobTitle);
    }
}
