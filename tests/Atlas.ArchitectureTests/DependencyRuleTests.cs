using System.Reflection;
using NetArchTest.Rules;

namespace Atlas.ArchitectureTests;

// LEARN: these are "fitness functions" — architecture you can FAIL, not just draw.
// The dependency rule of the hexagon is enforced here, in CI, on every PR:
//
//   Domain  ←  Application  ←  Infrastructure
//                    ↑              ↑
//                   Api  ───────────┘   (composition root only)
//   Contracts: logic-free DTO edge, referenced only by Api
//
// If a change violates this map, the build goes red before a human reviewer
// even opens the diff. See docs/concepts/fitness-functions.md and ADR-0001.
public class DependencyRuleTests
{
    private static readonly Assembly Domain = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly Application = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly Contracts = typeof(Contracts.AssemblyReference).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    [Fact]
    public void Domain_depends_on_no_other_layer_and_no_framework()
    {
        // WHY: the Domain is the part of Atlas that must survive every vendor,
        // framework, and database decision. Purity here is not aesthetics — it is
        // what makes the S12-style pivots cheap.
        TestResult result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Atlas.Application",
                "Atlas.Contracts",
                "Atlas.Infrastructure",
                "Atlas.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "System.Text.Json")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        TestResult result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Atlas.Contracts",
                "Atlas.Infrastructure",
                "Atlas.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    [Fact]
    public void Contracts_are_a_leaf_with_no_atlas_dependencies()
    {
        // WHY: Contracts is the shape of Atlas as seen from OUTSIDE. If it leaked
        // domain types, every internal rename would be an API breaking change.
        TestResult result = Types.InAssembly(Contracts)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Atlas.Domain",
                "Atlas.Application",
                "Atlas.Infrastructure",
                "Atlas.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    [Fact]
    public void Infrastructure_never_reaches_up_into_Api_or_Contracts()
    {
        TestResult result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("Atlas.Api", "Atlas.Contracts")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    [Fact]
    public void Api_touches_Infrastructure_only_inside_the_composition_root()
    {
        // WHY: the Api project must reference Infrastructure to wire DI — someone has
        // to be the composition root. The rule is that ONLY the composition root does:
        // endpoints talk to use cases, never to EF or adapters.
        // TRY: add `using Atlas.Infrastructure;` to an endpoint file and run this test.
        TestResult result = Types.InAssembly(Api)
            .That()
            .DoNotResideInNamespace("Atlas.Api.Composition")
            .ShouldNot()
            .HaveDependencyOnAny("Atlas.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    private static string Violations(TestResult result)
    {
        if (result.IsSuccessful)
        {
            return string.Empty;
        }

        IEnumerable<string> names = result.FailingTypes?.Select(t => t.FullName ?? t.Name) ?? [];
        return "Dependency rule violated by: " + string.Join(", ", names);
    }
}
