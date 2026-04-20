using System.Text.RegularExpressions;

namespace Homespun.Tests.Features.Observability;

/// <summary>
/// Enforces that every ActivitySource + span name emitted by
/// <c>src/Homespun.Server/**/*.cs</c> appears in
/// <c>docs/traces/dictionary.md</c>, and vice-versa.
///
/// See <c>docs/traces/README.md</c> for how contributors update the
/// dictionary when adding / renaming / removing spans.
/// </summary>
[TestFixture]
public class TraceDictionaryTests
{
    /// <summary>
    /// Files that emit spans whose first argument is not a compile-time
    /// string literal (interpolated expression, identifier, etc.). Each
    /// entry MUST carry a comment naming the emitter and the canonical
    /// shape that ends up in the dictionary.
    /// </summary>
    private static readonly HashSet<string> NonLiteralSpanAllowlist = new(StringComparer.Ordinal)
    {
        // TraceparentHubFilter.cs emits $"SignalR.{hubName}/{methodName}"
        // on the Homespun.Signalr source. Documented in dictionary as
        // `SignalR.<Hub>/<Method>`.
        "src/Homespun.Server/Features/Observability/TraceparentHubFilter.cs",
    };

    /// <summary>
    /// Documented spans that have no corresponding literal emit site — either
    /// because a .NET auto-instrumentation source produces them, or because
    /// the documented form is a placeholder for a dynamic span that appears
    /// in code only as an interpolated expression. Each entry MUST carry a
    /// comment explaining why the code→doc direction skips it.
    /// </summary>
    private static readonly HashSet<string> OrphanExemptSpans = new(StringComparer.Ordinal)
    {
        // Emitted by Microsoft.AspNetCore.Hosting auto-instrumentation.
        "http.server.request",
        // Emitted by System.Net.Http auto-instrumentation.
        "http.client.request",
        // Dictionary placeholder for the interpolated form
        // $"SignalR.{hubName}/{methodName}" — see NonLiteralSpanAllowlist.
        "SignalR.<Hub>/<Method>",
    };

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string DictionaryPath = Path.Combine(RepoRoot, "docs", "traces", "dictionary.md");
    private static readonly string ServerSrcRoot = Path.Combine(RepoRoot, "src", "Homespun.Server");

    private const string ServerTierHeader = "Server-originated traces";

    [Test]
    public void Dictionary_File_Exists()
    {
        Assert.That(File.Exists(DictionaryPath), Is.True,
            $"Trace dictionary missing at {DictionaryPath}. Create it before merging server span changes.");
    }

    [Test]
    public void Every_Server_ActivitySource_Is_Registered_In_Dictionary()
    {
        var dictionary = ParseDictionary(DictionaryPath);
        var (sourceNames, _, _) = ScanServerSources();

        var missing = sourceNames
            .Where(n => !dictionary.RegistryNames.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.That(missing, Is.Empty,
            $"ActivitySource(s) in src/Homespun.Server not in docs/traces/dictionary.md registry: "
            + string.Join(", ", missing)
            + ". Add them to the Tracer / ActivitySource registry table.");
    }

    [Test]
    public void Every_Literal_Server_Span_Is_Documented()
    {
        var dictionary = ParseDictionary(DictionaryPath);
        var (_, literalSpans, _) = ScanServerSources();

        var documented = new HashSet<string>(
            dictionary.TierH3Names.GetValueOrDefault(ServerTierHeader, new HashSet<string>()),
            StringComparer.Ordinal);

        var undocumented = literalSpans
            .Where(s => !documented.Contains(s.Name))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToArray();

        if (undocumented.Length > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                undocumented.Select(s =>
                    $"  - Span `{s.Name}` used in {s.RelativePath}:{s.Line} is not documented in docs/traces/dictionary.md"));
            Assert.Fail(
                $"Undocumented span name(s) emitted by src/Homespun.Server:{Environment.NewLine}{detail}{Environment.NewLine}"
                + "Add an H3 entry under '## Server-originated traces' in docs/traces/dictionary.md.");
        }
    }

    [Test]
    public void Dynamic_Server_Spans_Are_Allowlisted()
    {
        var (_, _, dynamicSites) = ScanServerSources();

        var offenders = dynamicSites
            .Where(s => !NonLiteralSpanAllowlist.Contains(s.RelativePath))
            .OrderBy(s => s.RelativePath, StringComparer.Ordinal)
            .ToArray();

        if (offenders.Length > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                offenders.Select(s => $"  - Dynamic span at {s.RelativePath}:{s.Line}"));
            Assert.Fail(
                "Non-literal (interpolated / variable) span name(s) in src/Homespun.Server:"
                + $"{Environment.NewLine}{detail}{Environment.NewLine}"
                + "Add the file to NonLiteralSpanAllowlist in TraceDictionaryTests.cs with a justifying comment, "
                + "and document the canonical shape under '## Server-originated traces' in docs/traces/dictionary.md.");
        }
    }

    [Test]
    public void No_Orphan_Server_Section_Entries()
    {
        var dictionary = ParseDictionary(DictionaryPath);
        var (_, literalSpans, _) = ScanServerSources();
        var documentedNames = dictionary.TierH3Names
            .GetValueOrDefault(ServerTierHeader, new HashSet<string>());

        var emitted = new HashSet<string>(literalSpans.Select(s => s.Name), StringComparer.Ordinal);
        var orphans = documentedNames
            .Where(name => !emitted.Contains(name) && !OrphanExemptSpans.Contains(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.That(orphans, Is.Empty,
            "Dictionary H3 entries under '## Server-originated traces' with no matching StartActivity call "
            + "in src/Homespun.Server: "
            + string.Join(", ", orphans)
            + ". Remove the entry or add its emit site — or add to OrphanExemptSpans with a justifying comment.");
    }

    // ---------------------------------------------------------------------
    // Dictionary parsing
    // ---------------------------------------------------------------------

    private sealed record ParsedDictionary(
        HashSet<string> RegistryNames,
        Dictionary<string, HashSet<string>> TierH3Names);

    private static ParsedDictionary ParseDictionary(string path)
    {
        var lines = File.ReadAllLines(path);
        var registry = new HashSet<string>(StringComparer.Ordinal);
        var tiers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var tierSections = new HashSet<string>(StringComparer.Ordinal)
        {
            "Client-originated traces",
            "Server-originated traces",
            "Worker-originated traces",
        };

        string? currentH2 = null;
        var inRegistryTable = false;
        // Backtick-quoted string with at least one character: `foo.bar`.
        var backtickRegex = new Regex("`([^`]+)`", RegexOptions.Compiled);

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentH2 = line.Substring(3).Trim();
                inRegistryTable = currentH2 == "Tracer / ActivitySource registry";
                continue;
            }

            if (inRegistryTable && line.StartsWith("|", StringComparison.Ordinal))
            {
                // Skip header row and separator row. Header contains "Name",
                // separator contains only dashes + pipes + spaces + colons.
                var stripped = line.Trim('|', ' ');
                if (stripped.StartsWith("Name", StringComparison.Ordinal)) continue;
                if (stripped.Replace(" ", "").Replace("|", "").All(c => c == '-' || c == ':')) continue;

                var cells = line.Trim('|').Split('|');
                if (cells.Length == 0) continue;

                var firstCellMatch = backtickRegex.Match(cells[0]);
                if (firstCellMatch.Success)
                {
                    registry.Add(firstCellMatch.Groups[1].Value.Trim());
                }
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal)
                && currentH2 is not null
                && tierSections.Contains(currentH2))
            {
                var headerText = line.Substring(4).Trim();
                var m = backtickRegex.Match(headerText);
                if (!m.Success) continue;
                if (!tiers.TryGetValue(currentH2, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    tiers[currentH2] = set;
                }
                set.Add(m.Groups[1].Value.Trim());
            }
        }

        return new ParsedDictionary(registry, tiers);
    }

    // ---------------------------------------------------------------------
    // Server source scanning
    // ---------------------------------------------------------------------

    private sealed record SpanSite(string Name, string RelativePath, int Line);
    private sealed record DynamicSpanSite(string RelativePath, int Line);

    private static readonly Regex ConstStringRegex = new(
        @"\bconst\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled);

    // `new ActivitySource("…")` or `new ActivitySource(IDENT)`.
    private static readonly Regex NewActivitySourceExplicit = new(
        @"new\s+ActivitySource\s*\(\s*(?:""(?<lit>[^""]+)""|(?<ident>\w+))\s*\)",
        RegexOptions.Compiled);

    // `ActivitySource <name> = new("…")` or `... = new(IDENT)`.
    private static readonly Regex ActivitySourceTargetTyped = new(
        @"ActivitySource\s+\w+\s*=\s*new\s*\(\s*(?:""(?<lit>[^""]+)""|(?<ident>\w+))\s*\)",
        RegexOptions.Compiled);

    // Capture `.StartActivity(` and the start of its first argument, preserving
    // enough context to tell literal from non-literal.
    private static readonly Regex StartActivityCall = new(
        @"\.StartActivity\s*\(\s*(?<arg>""[^""]*""|\$""[^""]*""|[A-Za-z_][\w\.]*|[^,)\s]+)",
        RegexOptions.Compiled);

    private static (HashSet<string> SourceNames, List<SpanSite> LiteralSpans, List<DynamicSpanSite> DynamicSites)
        ScanServerSources()
    {
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        var literalSpans = new List<SpanSite>();
        var dynamicSites = new List<DynamicSpanSite>();

        var csFiles = Directory.EnumerateFiles(ServerSrcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsGenerated(p))
            .ToArray();

        // First pass: collect const string values per file. Constants are
        // file-local for our purposes — all ActivitySource references in the
        // server tree live in the same file as the const that names them.
        var constsByFile = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            var consts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in ConstStringRegex.Matches(text))
            {
                consts[m.Groups["name"].Value] = m.Groups["value"].Value;
            }
            constsByFile[file] = consts;
        }

        // Global const fallback — resolves `new(HomespunActivitySources.AgentOrchestration)`
        // style references across files by the bare identifier (`AgentOrchestration`).
        var globalConsts = constsByFile.Values
            .SelectMany(d => d)
            .GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        foreach (var file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            var text = string.Join('\n', lines);
            var fileConsts = constsByFile[file];
            var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');

            foreach (Match m in NewActivitySourceExplicit.Matches(text))
            {
                AddSourceName(m, fileConsts, globalConsts, sourceNames);
            }
            foreach (Match m in ActivitySourceTargetTyped.Matches(text))
            {
                AddSourceName(m, fileConsts, globalConsts, sourceNames);
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var matches = StartActivityCall.Matches(line);
                foreach (Match m in matches)
                {
                    var arg = m.Groups["arg"].Value.Trim();
                    if (arg.Length >= 2 && arg[0] == '"' && arg[^1] == '"')
                    {
                        literalSpans.Add(new SpanSite(arg[1..^1], rel, i + 1));
                    }
                    else
                    {
                        dynamicSites.Add(new DynamicSpanSite(rel, i + 1));
                    }
                }
            }
        }

        return (sourceNames, literalSpans, dynamicSites);
    }

    private static void AddSourceName(
        Match m,
        IReadOnlyDictionary<string, string> fileConsts,
        IReadOnlyDictionary<string, string> globalConsts,
        HashSet<string> sourceNames)
    {
        var lit = m.Groups["lit"];
        if (lit.Success)
        {
            sourceNames.Add(lit.Value);
            return;
        }

        var ident = m.Groups["ident"].Value;
        if (fileConsts.TryGetValue(ident, out var fileValue))
        {
            sourceNames.Add(fileValue);
            return;
        }
        if (globalConsts.TryGetValue(ident, out var globalValue))
        {
            sourceNames.Add(globalValue);
        }
        // Unresolved identifier: silently skip. The drift check for
        // ActivitySources relies on literal or const-resolved values; a
        // truly dynamic source (unknown at build time) is exotic enough
        // that we'd want to see the failure manifest separately.
    }

    private static bool IsGenerated(string path)
    {
        var norm = path.Replace('\\', '/');
        return norm.Contains("/bin/") || norm.Contains("/obj/");
    }

    // ---------------------------------------------------------------------
    // Repo-root discovery
    // ---------------------------------------------------------------------

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "traces", "dictionary.md")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root from test directory — "
            + "docs/traces/dictionary.md not found walking up parents of "
            + TestContext.CurrentContext.TestDirectory);
    }
}
