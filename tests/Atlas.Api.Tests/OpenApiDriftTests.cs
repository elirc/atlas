using Atlas.Api.Tests.Infrastructure;

namespace Atlas.Api.Tests;

/// <summary>
/// The committed OpenAPI document must match what the API actually serves.
/// </summary>
// LEARN: this is a CONTRACT test. The spec in docs/openapi/ is what integrators
// read and generate clients from; if code and document can drift apart, the
// document is marketing. CI runs this on every PR, so "update the spec" cannot
// be forgotten — it can only be done.
//
// To bless an intentional change:  ATLAS_UPDATE_OPENAPI=1 dotnet test
// (the test then rewrites docs/openapi/atlas-v1.json and passes; commit the diff).
public class OpenApiDriftTests : IClassFixture<AtlasApiFactory>
{
    private readonly HttpClient _client;

    public OpenApiDriftTests(AtlasApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Served_spec_matches_the_committed_document()
    {
        string served = Normalize(await _client.GetStringAsync("/openapi/v1.json"));

        string specPath = Path.Combine(RepoRoot(), "docs", "openapi", "atlas-v1.json");

        if (Environment.GetEnvironmentVariable("ATLAS_UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(specPath)!);
            await File.WriteAllTextAsync(specPath, served + Environment.NewLine);
            return;
        }

        Assert.True(File.Exists(specPath),
            $"Committed OpenAPI document missing at {specPath}. " +
            "Run with ATLAS_UPDATE_OPENAPI=1 to create it.");

        string committed = Normalize(await File.ReadAllTextAsync(specPath));

        // WHY: a plain string equality gives an unreadable failure for a 1-line
        // drift, so point at the first differing line instead.
        if (committed != served)
        {
            string[] a = committed.Split('\n');
            string[] b = served.Split('\n');
            int line = Enumerable.Range(0, Math.Min(a.Length, b.Length))
                .FirstOrDefault(i => a[i] != b[i], Math.Min(a.Length, b.Length));

            Assert.Fail(
                $"OpenAPI drift at line {line + 1}:\n" +
                $"  committed: {(line < a.Length ? a[line] : "<eof>")}\n" +
                $"  served:    {(line < b.Length ? b[line] : "<eof>")}\n" +
                "If this change is intentional, bless it: ATLAS_UPDATE_OPENAPI=1 dotnet test, " +
                "then commit docs/openapi/atlas-v1.json.");
        }
    }

    private static string Normalize(string json) =>
        json.Replace("\r\n", "\n").TrimEnd('\n', ' ');

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Atlas.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
