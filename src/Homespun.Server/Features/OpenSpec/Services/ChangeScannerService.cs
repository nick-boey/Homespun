using System.Collections.Concurrent;
using System.Text.Json;
using Homespun.Features.Commands;
using Homespun.Features.OpenSpec.Telemetry;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Scans a clone's OpenSpec change directories and matches them to Fleece issues via sidecars.
/// </summary>
public class ChangeScannerService(
    ISidecarService sidecarService,
    ICommandRunner commandRunner,
    ILogger<ChangeScannerService> logger) : IChangeScannerService
{
    private const string ChangesRelativePath = "openspec/changes";
    private const string ArchiveRelativePath = "openspec/changes/archive";
    private const string TasksFileName = "tasks.md";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// mtime-keyed micro-cache for parsed <see cref="ChangeArtifactState"/>.
    /// Key = (clonePath, changeName, mtimeTupleHash) — mtime changes make old
    /// entries unreachable and the subprocess runs again on the next call.
    /// </summary>
    private readonly ConcurrentDictionary<ArtifactStateCacheKey, ChangeArtifactState?> _artifactStateCache = new();

    internal readonly record struct ArtifactStateCacheKey(string ClonePath, string ChangeName, long MtimeHash);

    /// <inheritdoc />
    public async Task<BranchScanResult> ScanBranchAsync(
        string clonePath,
        string branchFleeceId,
        string? baseBranch = null,
        CancellationToken ct = default)
    {
        using var activity = OpenSpecActivitySource.Instance.StartActivity("openspec.scan.branch");

        var linked = new List<LinkedChangeInfo>();
        var orphans = new List<OrphanChangeInfo>();
        var inherited = new List<string>();

        var changesRoot = Path.Combine(clonePath, ChangesRelativePath);
        var archiveRoot = Path.Combine(clonePath, ArchiveRelativePath);

        // Live changes: direct children of openspec/changes/ (excluding the archive directory).
        if (Directory.Exists(changesRoot))
        {
            foreach (var changeDir in Directory.GetDirectories(changesRoot))
            {
                ct.ThrowIfCancellationRequested();

                var name = Path.GetFileName(changeDir);
                if (string.Equals(name, "archive", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sidecar = await sidecarService.ReadSidecarAsync(changeDir, ct);
                if (sidecar is null)
                {
                    orphans.Add(new OrphanChangeInfo { Name = name, Directory = changeDir });
                    continue;
                }

                if (!string.Equals(sidecar.FleeceId, branchFleeceId, StringComparison.Ordinal))
                {
                    inherited.Add(name);
                    continue;
                }

                var artifactState = await GetArtifactStateAsync(clonePath, name, ct);
                var taskState = await ParseTasksAsync(changeDir, ct);

                linked.Add(new LinkedChangeInfo
                {
                    Name = name,
                    Directory = changeDir,
                    CreatedBy = sidecar.CreatedBy,
                    IsArchived = false,
                    ArtifactState = artifactState,
                    TaskState = taskState
                });
            }
        }

        // Archive fallback: any archived change whose sidecar matches the branch fleece id.
        if (Directory.Exists(archiveRoot))
        {
            foreach (var archivedDir in Directory.GetDirectories(archiveRoot))
            {
                ct.ThrowIfCancellationRequested();

                var archiveFolder = Path.GetFileName(archivedDir);
                var sidecar = await sidecarService.ReadSidecarAsync(archivedDir, ct);
                if (sidecar is null || !string.Equals(sidecar.FleeceId, branchFleeceId, StringComparison.Ordinal))
                {
                    continue;
                }

                var changeName = StripDatePrefix(archiveFolder);

                // Prefer a live copy when present; skip the archive entry to avoid duplicates.
                if (linked.Any(l => !l.IsArchived && string.Equals(l.Name, changeName, StringComparison.Ordinal)))
                {
                    continue;
                }

                var taskState = await ParseTasksAsync(archivedDir, ct);

                linked.Add(new LinkedChangeInfo
                {
                    Name = changeName,
                    Directory = archivedDir,
                    CreatedBy = sidecar.CreatedBy,
                    IsArchived = true,
                    ArchivedFolderName = archiveFolder,
                    ArtifactState = null,
                    TaskState = taskState
                });
            }
        }

        if (orphans.Count > 0 && !string.IsNullOrWhiteSpace(baseBranch))
        {
            var addedChangeNames = await GetAddedChangeNamesOnBranchAsync(clonePath, baseBranch!, ct);
            orphans = orphans.Select(o => new OrphanChangeInfo
            {
                Name = o.Name,
                Directory = o.Directory,
                CreatedOnBranch = addedChangeNames.Contains(o.Name)
            }).ToList();
        }

        return new BranchScanResult
        {
            BranchFleeceId = branchFleeceId,
            LinkedChanges = linked,
            OrphanChanges = orphans,
            InheritedChangeNames = inherited
        };
    }

    /// <inheritdoc />
    public async Task<ChangeArtifactState?> GetArtifactStateAsync(
        string clonePath,
        string changeName,
        CancellationToken ct = default)
    {
        using var activity = OpenSpecActivitySource.Instance.StartActivity("openspec.artifact.state");
        activity?.SetTag("change.name", changeName);

        var mtimeHash = BuildMtimeTuple(clonePath, changeName);
        if (mtimeHash.HasValue)
        {
            var key = new ArtifactStateCacheKey(clonePath, changeName, mtimeHash.Value);
            if (_artifactStateCache.TryGetValue(key, out var cached))
            {
                activity?.SetTag("cache.hit", true);
                return cached;
            }

            activity?.SetTag("cache.hit", false);

            var computed = await InvokeOpenSpecStatusAsync(clonePath, changeName);
            _artifactStateCache[key] = computed;
            return computed;
        }

        // Change directory (or all three hashed files) missing — skip the cache
        // so a stale entry cannot outlive the directory being deleted and
        // recreated.
        activity?.SetTag("cache.hit", false);
        return await InvokeOpenSpecStatusAsync(clonePath, changeName);
    }

    /// <summary>
    /// Builds a stable hash over the last-write times of <c>proposal.md</c>,
    /// <c>tasks.md</c>, and the <c>specs/</c> subtree root under the given
    /// change directory. Returns <c>null</c> if the change directory does not
    /// exist (caller falls back to the uncached subprocess path).
    /// </summary>
    private static long? BuildMtimeTuple(string clonePath, string changeName)
    {
        var changeDir = Path.Combine(clonePath, ChangesRelativePath, changeName);
        if (!Directory.Exists(changeDir))
        {
            return null;
        }

        var proposal = GetLastWriteTicksOrSentinel(Path.Combine(changeDir, "proposal.md"));
        var tasks = GetLastWriteTicksOrSentinel(Path.Combine(changeDir, TasksFileName));
        var specs = GetLastWriteTicksOrSentinel(Path.Combine(changeDir, "specs"));

        // HashCode.Combine is sufficient — we only need per-change uniqueness
        // and churn detection, not cryptographic strength.
        return ((long)HashCode.Combine(proposal, tasks, specs)) & 0xFFFFFFFFL;
    }

    private static long GetLastWriteTicksOrSentinel(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return File.GetLastWriteTimeUtc(path).Ticks;
            }
            if (Directory.Exists(path))
            {
                return Directory.GetLastWriteTimeUtc(path).Ticks;
            }
        }
        catch (IOException)
        {
            // Fall through — sentinel tells the cache the file is unreachable.
        }
        return -1L;
    }

    private async Task<ChangeArtifactState?> InvokeOpenSpecStatusAsync(
        string clonePath,
        string changeName)
    {
        CommandResult result;
        try
        {
            result = await commandRunner.RunAsync(
                "openspec",
                $"status --change \"{changeName}\" --json",
                clonePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "openspec status failed for change {Change} in {Clone}", changeName, clonePath);
            return null;
        }

        if (!result.Success)
        {
            logger.LogDebug(
                "openspec status returned exit code {ExitCode} for change {Change}: {Error}",
                result.ExitCode, changeName, result.Error);
            return null;
        }

        var json = ExtractJson(result.Output);
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("openspec status produced no JSON payload for {Change}", changeName);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChangeArtifactState>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse openspec status JSON for {Change}", changeName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<TaskStateSummary> ParseTasksAsync(
        string changeDirectory,
        CancellationToken ct = default)
    {
        var tasksPath = Path.Combine(changeDirectory, TasksFileName);
        if (!File.Exists(tasksPath))
        {
            return TaskStateSummary.Empty;
        }

        var content = await File.ReadAllTextAsync(tasksPath, ct);
        return TasksParser.Parse(content);
    }

    /// <inheritdoc />
    public async Task<string?> TryAutoLinkSingleOrphanAsync(
        BranchScanResult scan,
        string branchFleeceId,
        CancellationToken ct = default)
    {
        if (scan.OrphanChanges.Count != 1)
        {
            return null;
        }

        var orphan = scan.OrphanChanges[0];
        await sidecarService.WriteSidecarAsync(
            orphan.Directory,
            new ChangeSidecar { FleeceId = branchFleeceId, CreatedBy = "agent" },
            ct);

        logger.LogInformation(
            "Auto-linked orphan change {Change} to fleece issue {Fleece}", orphan.Name, branchFleeceId);

        return orphan.Name;
    }

    /// <summary>
    /// Strips a leading date prefix (<c>YYYY-MM-DD-</c>) from an archive folder name.
    /// </summary>
    internal static string StripDatePrefix(string archiveFolder)
    {
        // YYYY-MM-DD-foo → foo (10 chars + a dash). Falls back to the original when the prefix is missing.
        if (archiveFolder.Length <= 11 || archiveFolder[10] != '-')
        {
            return archiveFolder;
        }

        var prefix = archiveFolder[..10];
        if (DateOnly.TryParseExact(prefix, "yyyy-MM-dd", out _))
        {
            return archiveFolder[11..];
        }

        return archiveFolder;
    }

    /// <summary>
    /// Extracts a JSON payload from command output. <c>openspec</c> emits a status line
    /// ("- Loading change status...") before the JSON body.
    /// </summary>
    private static string ExtractJson(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        var braceIndex = output.IndexOf('{');
        return braceIndex < 0 ? string.Empty : output[braceIndex..];
    }

    /// <summary>
    /// Returns the set of change names that have a newly-added file since the given base branch,
    /// using <c>git log --diff-filter=A --name-only</c>. Failures return an empty set.
    /// </summary>
    private async Task<HashSet<string>> GetAddedChangeNamesOnBranchAsync(
        string clonePath,
        string baseBranch,
        CancellationToken ct)
    {
        var added = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var result = await commandRunner.RunAsync(
                "git",
                $"log --diff-filter=A --name-only --format= {baseBranch}..HEAD -- {ChangesRelativePath}/",
                clonePath);

            if (!result.Success)
            {
                logger.LogDebug(
                    "git log (diff-filter=A) failed against {Base} in {Clone}: {Error}",
                    baseBranch, clonePath, result.Error);
                return added;
            }

            foreach (var rawLine in result.Output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || !line.StartsWith(ChangesRelativePath + "/", StringComparison.Ordinal))
                {
                    continue;
                }

                var relative = line[(ChangesRelativePath.Length + 1)..];
                var separatorIdx = relative.IndexOf('/');
                if (separatorIdx <= 0)
                {
                    continue;
                }

                var firstSegment = relative[..separatorIdx];
                if (string.Equals(firstSegment, "archive", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                added.Add(firstSegment);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to determine added change directories on branch");
        }

        return added;
    }
}
