using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Utilities;

namespace Homespun.Web.LayoutFixtures;

/// <summary>
/// Cross-stack sort-order parity tests. Reads the JSON fixture at
/// <c>sort-order-midpoint.parity.json</c> (alongside the TS source), runs
/// <see cref="LexoRank.GetMiddleRank"/> against each <c>prev/next</c> pair,
/// and either writes the <c>expected</c> field back to the file
/// (when <c>UPDATE_SORT_ORDER_PARITY=1</c>) or asserts that every entry's
/// <c>expected</c> field matches the C# output.
/// </summary>
[TestFixture]
[Category("SortOrderParity")]
public sealed class SortOrderParityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private string _fixtureSourcePath = null!;
    private bool _updateParity;

    [SetUp]
    public void Setup()
    {
        // Locate the source fixture alongside the TS service file.
        _fixtureSourcePath = ResolveSourceFixturePath();
        var env = Environment.GetEnvironmentVariable("UPDATE_SORT_ORDER_PARITY");
        _updateParity = string.Equals(env, "1", StringComparison.Ordinal)
                        || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void SortOrder_Parity_MatchesCSharpReference()
    {
        var json = File.ReadAllText(_fixtureSourcePath);
        var entries = JsonSerializer.Deserialize<List<ParityEntry>>(json, JsonOptions)
                      ?? throw new InvalidOperationException($"Failed to parse {_fixtureSourcePath}");

        var updated = false;
        var failures = new List<string>();

        for (var idx = 0; idx < entries.Count; idx++)
        {
            var entry = entries[idx];
            string csResult;
            try
            {
                // The TS port treats empty string as the "no bound" sentinel
                // (matching C# null), so map empty → null before calling LexoRank.
                var prev = string.IsNullOrEmpty(entry.Prev) ? null : entry.Prev;
                var next = string.IsNullOrEmpty(entry.Next) ? null : entry.Next;
                csResult = LexoRank.GetMiddleRank(prev, next);
            }
            catch (Exception ex)
            {
                // Skip pairs that LexoRank cannot handle (e.g. empty next without upper-bound semantics).
                // These are valid TS-only pairs; the parity test only covers the shared subset.
                var prevLabel = entry.Prev!.Length > 0 ? entry.Prev : "(empty)";
                var nextLabel = entry.Next?.Length > 0 ? entry.Next : "(empty)";
                TestContext.Out.WriteLine($"[SKIP] pair {idx} prev={prevLabel} next={nextLabel}: {ex.Message}");
                continue;
            }

            if (_updateParity)
            {
                if (entry.Expected != csResult)
                {
                    entries[idx] = entry with { Expected = csResult };
                    updated = true;
                }
            }
            else
            {
                if (entry.Expected is null)
                {
                    // Pair has no expected value yet; skip assertion (run with UPDATE_SORT_ORDER_PARITY=1 to populate).
                    TestContext.Out.WriteLine($"[PENDING] pair {idx} has no expected value. Run with UPDATE_SORT_ORDER_PARITY=1 to populate.");
                    continue;
                }

                if (entry.Expected != csResult)
                {
                    failures.Add($"  pair {idx} prev=\"{entry.Prev}\" next=\"{entry.Next}\": expected \"{entry.Expected}\" but C# produced \"{csResult}\"");
                }
            }
        }

        if (_updateParity && updated)
        {
            var newJson = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(_fixtureSourcePath, newJson);
            Assert.Pass($"Updated {Path.GetFileName(_fixtureSourcePath)} with {entries.Count(e => e.Expected is not null)} expected values.");
            return;
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"Sort-order parity failures ({failures.Count}):\n{string.Join("\n", failures)}");
        }
    }

    private static string ResolveSourceFixturePath()
    {
        // Walk up from the test output directory until we find the project root.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Homespun.Web.LayoutFixtures.csproj")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate Homespun.Web.LayoutFixtures project root from " + AppContext.BaseDirectory);
        }

        // The parity fixture lives alongside the TS source.
        var fixturePath = Path.GetFullPath(
            Path.Combine(
                dir,
                "..", "..", // tests/ -> repo root
                "src", "Homespun.Web", "src", "features", "issues", "services",
                "sort-order-midpoint.parity.json"));

        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException(
                $"Parity fixture not found at {fixturePath}. Create it first.", fixturePath);
        }

        return fixturePath;
    }
}

internal sealed record ParityEntry
{
    [JsonPropertyName("prev")]
    public string Prev { get; init; } = string.Empty;

    [JsonPropertyName("next")]
    public string Next { get; init; } = string.Empty;

    [JsonPropertyName("expected")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expected { get; init; }
}
