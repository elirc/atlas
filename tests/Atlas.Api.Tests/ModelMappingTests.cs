using System.Reflection;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Api.Tests;

/// <summary>
/// Fitness function: every public property on a mapped entity must be mapped
/// (a column or a navigation). EF Core SILENTLY skips getter-only auto-properties;
/// this test turns that silence into a red build.
/// </summary>
// LEARN: this test exists because of a real bug in this very sprint — Session's
// CreatedAtUtc/ExpiresAtUtc were getter-only, EF never created their columns, and
// every session read back as expired-at-year-one. The compiler was happy, the
// schema was wrong, and only behavior tests caught it. Now the model itself is
// under test. (Fitness functions grow from incidents — that's the pattern.)
public class ModelMappingTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public ModelMappingTests(AtlasApiFactory factory) => _factory = factory;

    // WHY: EF's runtime model cannot distinguish "deliberately Ignore()d" from
    // "silently forgotten" — so deliberate exceptions are declared HERE, visibly,
    // where a reviewer sees every one. A computed property belongs on this list;
    // a timestamp never does.
    private static readonly HashSet<string> DeliberatelyUnmapped =
    [
        "Worker.ProbationEndsOn", // computed from HiredOn + country rules
        "EngagementChecklistItem.IsComplete", // computed from CompletedAtUtc
    ];

    [Fact]
    public void Every_public_property_of_every_mapped_entity_is_mapped()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();

        var unmapped = new List<string>();

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var mapped = entityType.GetProperties().Select(p => p.Name)
                .Concat(entityType.GetNavigations().Select(n => n.Name))
                .ToHashSet();

            IEnumerable<string> publicProps = entityType.ClrType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => p.Name);

            unmapped.AddRange(
                publicProps.Where(name => !mapped.Contains(name))
                    .Select(name => $"{entityType.ClrType.Name}.{name}")
                    .Where(qualified => !DeliberatelyUnmapped.Contains(qualified)));
        }

        Assert.True(unmapped.Count == 0,
            "Public entity properties EF is silently ignoring (map them explicitly " +
            "or mark the design decision): " + string.Join(", ", unmapped));
    }
}
