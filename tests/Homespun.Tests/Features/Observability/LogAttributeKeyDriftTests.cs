using System.Text.RegularExpressions;

namespace Homespun.Tests.Features.Observability;

/// <summary>
/// Enforces that every <c>[TagName(...)]</c> override on a server log site
/// follows the OpenTelemetry semantic-conventions naming spec — either a
/// <c>homespun.*</c> private-namespace key matching
/// <c>^homespun\.[a-z0-9_.]+$</c>, or one of the registered semconv names
/// in <see cref="SemconvKnownNames"/>.
///
/// <para>
/// See <c>docs/observability/otlp-proxy.md#attribute-key-contract</c> for the
/// full attribute-key contract and the rationale behind the naming spec.
/// </para>
/// </summary>
[TestFixture]
public class LogAttributeKeyDriftTests
{
    /// <summary>
    /// Registered OpenTelemetry semconv attribute names that may appear on a
    /// log site without a <c>homespun.</c> prefix. Add new entries with a
    /// reference to the semconv registry section that introduces them.
    /// </summary>
    private static readonly HashSet<string> SemconvKnownNames = new(StringComparer.Ordinal)
    {
        // https://opentelemetry.io/docs/specs/semconv/general/session/
        "session.id",
        // https://opentelemetry.io/docs/specs/semconv/general/events/
        "event.name",
    };

    private static readonly Regex HomespunKeyRegex = new(
        @"^homespun\.[a-z0-9_.]+$",
        RegexOptions.Compiled);

    private static readonly Regex TagNameAttributeRegex = new(
        @"\[TagName\s*\(\s*""(?<key>[^""]+)""\s*\)\]",
        RegexOptions.Compiled);

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ServerLoggingRoot = Path.Combine(RepoRoot, "src", "Homespun.Server");

    [Test]
    public void Every_TagName_On_Server_Log_Site_Is_Semconv_Aligned()
    {
        var keys = ScanTagNameOverrides();

        Assert.That(keys, Is.Not.Empty,
            "No [TagName(...)] declarations found under src/Homespun.Server. "
            + "Either the source-generated log sites were removed (update this test) "
            + "or the scan is broken.");

        var offenders = keys
            .Where(k => !HomespunKeyRegex.IsMatch(k.Key) && !SemconvKnownNames.Contains(k.Key))
            .OrderBy(k => k.RelativePath, StringComparer.Ordinal)
            .ThenBy(k => k.Line)
            .ToArray();

        if (offenders.Length > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                offenders.Select(k =>
                    $"  - `{k.Key}` at {k.RelativePath}:{k.Line}"));
            Assert.Fail(
                $"Server log site [TagName(...)] override(s) violate the attribute-key contract:"
                + $"{Environment.NewLine}{detail}{Environment.NewLine}"
                + $"Allowed shapes: `^homespun\\.[a-z0-9_.]+$` or one of {{{string.Join(", ", SemconvKnownNames)}}}. "
                + "See docs/observability/otlp-proxy.md#attribute-key-contract.");
        }
    }

    private sealed record TagNameSite(string Key, string RelativePath, int Line);

    private static List<TagNameSite> ScanTagNameOverrides()
    {
        var sites = new List<TagNameSite>();

        var csFiles = Directory.EnumerateFiles(ServerLoggingRoot, "*Log.cs", SearchOption.AllDirectories)
            .Where(p => !IsGenerated(p))
            .ToArray();

        foreach (var file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in TagNameAttributeRegex.Matches(lines[i]))
                {
                    sites.Add(new TagNameSite(m.Groups["key"].Value, rel, i + 1));
                }
            }
        }

        return sites;
    }

    private static bool IsGenerated(string path)
    {
        var norm = path.Replace('\\', '/');
        return norm.Contains("/bin/") || norm.Contains("/obj/");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "observability", "otlp-proxy.md")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root from test directory — "
            + "docs/observability/otlp-proxy.md not found walking up parents of "
            + TestContext.CurrentContext.TestDirectory);
    }
}
