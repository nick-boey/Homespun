using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Fleece.Core.Models.Graph;
using Fleece.Core.Services.GraphLayout;
using Fleece.Core.Services.Interfaces;

namespace Homespun.Web.LayoutFixtures;

/// <summary>
/// Cross-stack golden-fixture emitter. Reads each <c>fixtures/*.input.json</c>, runs Fleece.Core's
/// <see cref="IIssueLayoutService"/> against it, and either writes the corresponding
/// <c>*.expected.json</c> (when <c>UPDATE_FIXTURES=1</c>) or asserts structural equality against
/// the existing expected output. The TS port consumes both files and validates that its own output
/// matches the C# reference for the same input.
/// </summary>
[TestFixture]
[Category("Fixtures")]
public sealed class EmitFixturesTests
{
    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly JsonSerializerOptions ExpectedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private IIssueLayoutService _service = null!;
    private string _fixturesDir = null!;
    private bool _updateFixtures;

    [SetUp]
    public void Setup()
    {
        _service = new IssueLayoutService(new GraphLayoutService());
        _fixturesDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures");
        var env = Environment.GetEnvironmentVariable("UPDATE_FIXTURES");
        _updateFixtures = string.Equals(env, "1", StringComparison.Ordinal)
                          || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<TestCaseData> FixtureCases()
    {
        var dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures");
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var inputPath in Directory.EnumerateFiles(dir, "*.input.json").OrderBy(p => p))
        {
            var name = Path.GetFileName(inputPath).Replace(".input.json", string.Empty);
            yield return new TestCaseData(name).SetName($"Fixture_{name}");
        }
    }

    [TestCaseSource(nameof(FixtureCases))]
    public void Fixture_matches_reference(string fixtureName)
    {
        var inputPath = Path.Combine(_fixturesDir, $"{fixtureName}.input.json");
        var expectedPath = Path.Combine(_fixturesDir, $"{fixtureName}.expected.json");

        var fixture = JsonSerializer.Deserialize<FixtureInput>(File.ReadAllText(inputPath), InputJsonOptions)
                      ?? throw new InvalidOperationException($"Failed to parse {inputPath}");

        var issues = fixture.Issues;
        var actual = RunLayout(fixture, issues);
        var actualJson = JsonSerializer.Serialize(actual, ExpectedJsonOptions);

        if (_updateFixtures)
        {
            // Write the source fixtures so the source-of-truth file (under the project, not bin)
            // is updated. CopyToOutputDirectory will refresh bin on next build.
            var sourcePath = ResolveSourceFixturePath(expectedPath);
            File.WriteAllText(sourcePath, actualJson);
            // Also refresh the copy in the test output so subsequent assertions in this run see it.
            File.WriteAllText(expectedPath, actualJson);
            Assert.Pass($"Updated {Path.GetFileName(sourcePath)}");
            return;
        }

        if (!File.Exists(expectedPath))
        {
            Assert.Fail($"Missing expected file {expectedPath}. Run with UPDATE_FIXTURES=1 to emit.");
        }

        var expectedJson = File.ReadAllText(expectedPath);
        AssertJsonEqual(expectedJson, actualJson, fixtureName);
    }

    private FixtureOutput RunLayout(FixtureInput fixture, IReadOnlyList<Issue> issues)
    {
        var mode = fixture.Mode ?? FixtureMode.Tree;
        try
        {
            var layout = mode switch
            {
                FixtureMode.Tree => _service.LayoutForTree(
                    issues,
                    fixture.Visibility ?? InactiveVisibility.Hide,
                    fixture.AssignedTo,
                    sort: null),
                FixtureMode.Next => _service.LayoutForNext(
                    issues,
                    fixture.MatchedIds is { Count: > 0 }
                        ? new HashSet<string>(fixture.MatchedIds, StringComparer.OrdinalIgnoreCase)
                        : null,
                    fixture.Visibility ?? InactiveVisibility.Hide,
                    fixture.AssignedTo,
                    sort: null),
                _ => throw new ArgumentOutOfRangeException(nameof(fixture.Mode), mode, "Unknown fixture mode"),
            };
            return FixtureOutput.FromLayout(layout);
        }
        catch (InvalidGraphException ex)
        {
            return FixtureOutput.FromCycle(ex.Cycle);
        }
    }

    private static string ResolveSourceFixturePath(string outputPath)
    {
        // outputPath = <bin>/fixtures/<name>.expected.json. Walk up to project root.
        var fileName = Path.GetFileName(outputPath);
        var projectDir = AppContext.BaseDirectory;
        // Walk up from bin/Debug/net10.0 until we find the csproj.
        while (projectDir is not null && !File.Exists(Path.Combine(projectDir, "Homespun.Web.LayoutFixtures.csproj")))
        {
            projectDir = Path.GetDirectoryName(projectDir);
        }
        if (projectDir is null)
        {
            throw new InvalidOperationException("Could not locate project root from " + AppContext.BaseDirectory);
        }
        return Path.Combine(projectDir, "fixtures", fileName);
    }

    private static void AssertJsonEqual(string expected, string actual, string fixtureName)
    {
        // Structural equality on parsed JSON (whitespace-insensitive).
        using var expectedDoc = JsonDocument.Parse(expected);
        using var actualDoc = JsonDocument.Parse(actual);
        if (!JsonElementsEqual(expectedDoc.RootElement, actualDoc.RootElement))
        {
            Assert.Fail($"Fixture '{fixtureName}' diverges from expected.\nExpected:\n{expected}\n\nActual:\n{actual}");
        }
    }

    private static bool JsonElementsEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                if (aProps.Count != bProps.Count) return false;
                foreach (var (k, v) in aProps)
                {
                    if (!bProps.TryGetValue(k, out var other)) return false;
                    if (!JsonElementsEqual(v, other)) return false;
                }
                return true;
            }
            case JsonValueKind.Array:
            {
                if (a.GetArrayLength() != b.GetArrayLength()) return false;
                using var ae = a.EnumerateArray().GetEnumerator();
                using var be = b.EnumerateArray().GetEnumerator();
                while (ae.MoveNext() && be.MoveNext())
                {
                    if (!JsonElementsEqual(ae.Current, be.Current)) return false;
                }
                return true;
            }
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            default:
                return false;
        }
    }
}

internal enum FixtureMode
{
    Tree,
    Next,
}

internal sealed record FixtureInput
{
    public FixtureMode? Mode { get; init; }
    public InactiveVisibility? Visibility { get; init; }
    public string? AssignedTo { get; init; }
    public IReadOnlyList<string>? MatchedIds { get; init; }
    public IReadOnlyList<Issue> Issues { get; init; } = Array.Empty<Issue>();
}

internal sealed record FixtureOutput
{
    public bool Ok { get; init; }
    public IReadOnlyList<string>? Cycle { get; init; }
    public int? TotalRows { get; init; }
    public int? TotalLanes { get; init; }
    public IReadOnlyList<FixtureNode>? Nodes { get; init; }
    public IReadOnlyList<FixtureEdge>? Edges { get; init; }

    public static FixtureOutput FromLayout(GraphLayout<Issue> layout) => new()
    {
        Ok = true,
        TotalRows = layout.TotalRows,
        TotalLanes = layout.TotalLanes,
        Nodes = layout.Nodes.Select(FixtureNode.From).ToList(),
        Edges = layout.Edges.Select(FixtureEdge.From).ToList(),
    };

    public static FixtureOutput FromCycle(IReadOnlyList<string> cycle) => new()
    {
        Ok = false,
        Cycle = cycle,
    };
}

internal sealed record FixtureNode(string Id, int Row, int Lane, int AppearanceIndex, int TotalAppearances)
{
    public static FixtureNode From(PositionedNode<Issue> n) =>
        new(n.Node.Id, n.Row, n.Lane, n.AppearanceIndex, n.TotalAppearances);
}

internal sealed record FixtureEdge(
    string FromId,
    string ToId,
    EdgeKind Kind,
    int StartRow,
    int StartLane,
    int EndRow,
    int EndLane,
    int? PivotLane,
    EdgeAttachSide SourceAttach,
    EdgeAttachSide TargetAttach)
{
    public static FixtureEdge From(Edge<Issue> e) => new(
        e.From.Id,
        e.To.Id,
        e.Kind,
        e.Start.Row,
        e.Start.Lane,
        e.End.Row,
        e.End.Lane,
        e.PivotLane,
        e.SourceAttach,
        e.TargetAttach);
}
