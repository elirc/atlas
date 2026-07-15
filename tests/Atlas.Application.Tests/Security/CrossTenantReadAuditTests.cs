using System.Reflection;
using Atlas.Application.Abstractions;
using Atlas.Application.Workforce;

namespace Atlas.Application.Tests.Security;

/// <summary>
/// The S13 audit's standing guardrail: a fitness function over EVERY by-id use case in the
/// application. A use case that operates on a resource by <see cref="Guid"/> but does not even
/// take <see cref="ICurrentContext"/> cannot be tenant-scoped — that is the shape of finding AF-3
/// (ledger #5), where <c>GetWorker</c> loaded a worker by primary key with no org filter.
/// </summary>
// 🔍 [Reviewer] Read this as necessary-but-not-sufficient, and say so out loud. Reflection can
// prove the context DEPENDENCY exists; it CANNOT prove the query used it (GetWorker held the
// context all along and still leaked — it called RequireUser but queried by bare id). The
// sufficient guard is the per-use-case cross-org characterization test (WorkerTenancyTests,
// QuoteQueriesTests, EngagementTransitionUseCaseTests…), which drives an org-B caller at an
// org-A id and demands a 404. This is exactly the gap docs/concepts/fitness-functions.md warns
// about: "find a violation these tests would miss." Both layers earn their place.
public class CrossTenantReadAuditTests
{
    private static readonly Assembly Application = typeof(GetWorker).Assembly;

    // A "by-id" use case: a public ExecuteAsync whose FIRST parameter is a Guid — a read or a
    // command that names a single resource. Every one of these touches org-owned data in Atlas.
    public static IEnumerable<object[]> ByIdUseCases() =>
        Application.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(HasGuidFirstExecuteAsync)
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(ByIdUseCases))]
    public void Every_by_id_use_case_injects_the_current_context(Type useCase)
    {
        bool holdsContext = useCase.GetConstructors()
            .SelectMany(ctor => ctor.GetParameters())
            .Any(p => p.ParameterType == typeof(ICurrentContext));

        Assert.True(
            holdsContext,
            $"{useCase.Name} operates on a resource by id but does not take ICurrentContext — " +
            "it cannot be tenant-scoped. This is the AF-3 failure shape; add the context port and " +
            "an org-scoped repository read (FindInOrgAsync), then pin it with a cross-org test.");
    }

    [Fact]
    public void The_sweep_actually_found_the_by_id_surface()
    {
        // A guard on the guard: if reflection silently matched nothing, the Theory above would
        // pass vacuously. The application has dozens of by-id use cases; assert we see them.
        Assert.True(
            ByIdUseCases().Count() >= 20,
            "The by-id use-case sweep matched too few types — the reflection filter is likely broken.");
    }

    private static bool HasGuidFirstExecuteAsync(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "ExecuteAsync")
            .Any(m => m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Guid));
}
