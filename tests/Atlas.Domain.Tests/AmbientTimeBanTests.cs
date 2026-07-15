using System.Text;
using System.Text.RegularExpressions;

namespace Atlas.Domain.Tests;

/// <summary>
/// Fitness function: NO source file in Atlas.Domain may read ambient wall-clock time
/// (DateTime.Now / DateTime.UtcNow / DateTimeOffset.Now / DateTimeOffset.UtcNow). Time is
/// an INPUT — it arrives as a TimeProvider-derived value at the edge, or as a DateOnly /
/// DateTimeOffset PARAMETER inside the domain. It is never read from a static clock here.
/// </summary>
// LEARN: this test was born from an INCIDENT, which is where fitness functions come from
// (ModelMappingTests, S02, was born the same way). A worker's probation flipped a day
// early for a Manila-based client because the probation check read the server's local
// wall clock — so the answer depended on which timezone Atlas was hosted in. The fix is
// not "use UtcNow right here". It is a BAN, enforced on every file, so the next convenient
// static-clock read is a red build in this repo, not a support ticket six months later.
// Read the fix commit-first: this test is the SPEC of the bug. See ADR-0007 and
// docs/concepts/date-math.md.
public class AmbientTimeBanTests
{
    private static readonly Regex Banned = new(
        @"DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.Now|DateTimeOffset\.UtcNow",
        RegexOptions.Compiled);

    [Fact]
    public void No_domain_source_reads_ambient_time()
    {
        string domainRoot = Path.Combine(RepoRoot(), "src", "Atlas.Domain");
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(domainRoot, "*.cs", SearchOption.AllDirectories))
        {
            // WHY: the build spits generated files into obj/ — they are not our source.
            if (file.Replace('\\', '/').Contains("/obj/"))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            bool inBlockComment = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripComments(lines[i], ref inBlockComment);
                if (Banned.IsMatch(code))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Ambient wall-clock reads are banned in Atlas.Domain — inject the time instead " +
            "(TimeProvider at the edge; a DateOnly/DateTimeOffset parameter in the domain):\n" +
            string.Join("\n", offenders));
    }

    // WHY: we scan CODE, not comments — a WHY-comment that mentions the banned APIs by name
    // (and there are several in this codebase) must not trip the ban. Strip // line comments
    // and /* ... */ block comments before matching. String literals are not stripped: a
    // domain file has no business putting "DateTime.Now" in a string either.
    private static string StripComments(string line, ref bool inBlockComment)
    {
        var sb = new StringBuilder(line.Length);

        for (int i = 0; i < line.Length; i++)
        {
            if (inBlockComment)
            {
                if (i + 1 < line.Length && line[i] == '*' && line[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
            {
                break;
            }

            sb.Append(line[i]);
        }

        return sb.ToString();
    }

    // Mirrors OpenApiDriftTests: walk up from the test binary to the solution root.
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
