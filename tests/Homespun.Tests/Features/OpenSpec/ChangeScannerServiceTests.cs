using Homespun.Features.Commands;
using Homespun.Features.OpenSpec.Services;
using Homespun.Shared.Models.Commands;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class ChangeScannerServiceTests
{
    private string _tempDir = null!;
    private Mock<ICommandRunner> _commandRunner = null!;
    private SidecarService _sidecarService = null!;
    private ChangeScannerService _scanner = null!;

    private const string BranchFleeceId = "issue-abc";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"scanner-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _commandRunner = new Mock<ICommandRunner>();
        _sidecarService = new SidecarService(NullLogger<SidecarService>.Instance);
        _scanner = new ChangeScannerService(
            _sidecarService,
            _commandRunner.Object,
            NullLogger<ChangeScannerService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task ScanBranchAsync_NoChangesDir_ReturnsEmpty()
    {
        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Is.Empty);
        Assert.That(result.OrphanChanges, Is.Empty);
        Assert.That(result.InheritedChangeNames, Is.Empty);
    }

    [Test]
    public async Task ScanBranchAsync_LinkedChange_AppearsLinked()
    {
        var changeDir = CreateChangeDir("my-change");
        await _sidecarService.WriteSidecarAsync(changeDir,
            new ChangeSidecar { FleeceId = BranchFleeceId, CreatedBy = "agent" });

        StubArtifactStatusSuccess("my-change", isComplete: false);

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Has.Count.EqualTo(1));
        Assert.That(result.LinkedChanges[0].Name, Is.EqualTo("my-change"));
        Assert.That(result.LinkedChanges[0].IsArchived, Is.False);
        Assert.That(result.LinkedChanges[0].ArtifactState!.IsComplete, Is.False);
    }

    [Test]
    public async Task ScanBranchAsync_InheritedChange_IsFilteredOut()
    {
        var changeDir = CreateChangeDir("inherited");
        await _sidecarService.WriteSidecarAsync(changeDir,
            new ChangeSidecar { FleeceId = "other-issue", CreatedBy = "server" });

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Is.Empty);
        Assert.That(result.InheritedChangeNames, Does.Contain("inherited"));
        Assert.That(result.OrphanChanges, Is.Empty);
    }

    [Test]
    public async Task ScanBranchAsync_OrphanChange_DetectedWithoutSidecar()
    {
        CreateChangeDir("orphan-change");

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.OrphanChanges, Has.Count.EqualTo(1));
        Assert.That(result.OrphanChanges[0].Name, Is.EqualTo("orphan-change"));
        Assert.That(result.LinkedChanges, Is.Empty);
    }

    [Test]
    public async Task ScanBranchAsync_ArchivedChange_FallsBackToArchive()
    {
        var archivedDir = CreateArchivedChangeDir("2026-04-16-old-change");
        await _sidecarService.WriteSidecarAsync(archivedDir,
            new ChangeSidecar { FleeceId = BranchFleeceId, CreatedBy = "agent" });

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Has.Count.EqualTo(1));
        var linked = result.LinkedChanges[0];
        Assert.That(linked.Name, Is.EqualTo("old-change"));
        Assert.That(linked.IsArchived, Is.True);
        Assert.That(linked.ArchivedFolderName, Is.EqualTo("2026-04-16-old-change"));
    }

    [Test]
    public async Task ScanBranchAsync_LiveAndArchived_PrefersLive()
    {
        // Live version
        var liveDir = CreateChangeDir("dup-change");
        await _sidecarService.WriteSidecarAsync(liveDir,
            new ChangeSidecar { FleeceId = BranchFleeceId, CreatedBy = "agent" });

        // Archived copy
        var archivedDir = CreateArchivedChangeDir("2026-03-01-dup-change");
        await _sidecarService.WriteSidecarAsync(archivedDir,
            new ChangeSidecar { FleeceId = BranchFleeceId, CreatedBy = "agent" });

        StubArtifactStatusSuccess("dup-change", isComplete: false);

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Has.Count.EqualTo(1));
        Assert.That(result.LinkedChanges[0].IsArchived, Is.False);
    }

    [Test]
    public async Task ScanBranchAsync_IncludesTaskState()
    {
        var changeDir = CreateChangeDir("with-tasks");
        await _sidecarService.WriteSidecarAsync(changeDir,
            new ChangeSidecar { FleeceId = BranchFleeceId, CreatedBy = "agent" });

        await File.WriteAllTextAsync(Path.Combine(changeDir, "tasks.md"),
            "## 1. Phase\n\n- [x] Done\n- [ ] Pending\n");

        StubArtifactStatusSuccess("with-tasks", isComplete: true);

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        var linked = result.LinkedChanges.Single();
        Assert.That(linked.TaskState.TasksTotal, Is.EqualTo(2));
        Assert.That(linked.TaskState.TasksDone, Is.EqualTo(1));
        Assert.That(linked.TaskState.NextIncomplete, Is.EqualTo("Pending"));
    }

    [Test]
    public async Task GetArtifactStateAsync_ParsesOpenSpecJson()
    {
        _commandRunner
            .Setup(r => r.RunAsync("openspec", It.Is<string>(s => s.Contains("--change \"my-change\"")), _tempDir))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                ExitCode = 0,
                Output = """
                    - Loading change status...
                    {
                      "changeName": "my-change",
                      "schemaName": "spec-driven",
                      "isComplete": true,
                      "applyRequires": ["tasks"],
                      "artifacts": [
                        { "id": "proposal", "outputPath": "proposal.md", "status": "done" }
                      ]
                    }
                    """
            });

        var result = await _scanner.GetArtifactStateAsync(_tempDir, "my-change");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ChangeName, Is.EqualTo("my-change"));
        Assert.That(result.SchemaName, Is.EqualTo("spec-driven"));
        Assert.That(result.IsComplete, Is.True);
        Assert.That(result.Artifacts, Has.Count.EqualTo(1));
        Assert.That(result.Artifacts[0].Id, Is.EqualTo("proposal"));
    }

    [Test]
    public async Task GetArtifactStateAsync_CliFailure_ReturnsNull()
    {
        _commandRunner
            .Setup(r => r.RunAsync("openspec", It.IsAny<string>(), _tempDir))
            .ReturnsAsync(new CommandResult { Success = false, ExitCode = 1, Error = "no such change" });

        var result = await _scanner.GetArtifactStateAsync(_tempDir, "missing");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryAutoLinkSingleOrphanAsync_SingleOrphan_WritesSidecarAndReturnsName()
    {
        var orphanDir = CreateChangeDir("lonely-orphan");

        var scan = new BranchScanResult
        {
            BranchFleeceId = BranchFleeceId,
            OrphanChanges = new List<OrphanChangeInfo>
            {
                new() { Name = "lonely-orphan", Directory = orphanDir }
            }
        };

        var name = await _scanner.TryAutoLinkSingleOrphanAsync(scan, BranchFleeceId);

        Assert.That(name, Is.EqualTo("lonely-orphan"));

        var sidecar = await _sidecarService.ReadSidecarAsync(orphanDir);
        Assert.That(sidecar, Is.Not.Null);
        Assert.That(sidecar!.FleeceId, Is.EqualTo(BranchFleeceId));
        Assert.That(sidecar.CreatedBy, Is.EqualTo("agent"));
    }

    [Test]
    public async Task TryAutoLinkSingleOrphanAsync_MultipleOrphans_ReturnsNullAndWritesNothing()
    {
        var orphanA = CreateChangeDir("a");
        var orphanB = CreateChangeDir("b");

        var scan = new BranchScanResult
        {
            BranchFleeceId = BranchFleeceId,
            OrphanChanges = new List<OrphanChangeInfo>
            {
                new() { Name = "a", Directory = orphanA },
                new() { Name = "b", Directory = orphanB }
            }
        };

        var name = await _scanner.TryAutoLinkSingleOrphanAsync(scan, BranchFleeceId);

        Assert.That(name, Is.Null);
        Assert.That(await _sidecarService.ReadSidecarAsync(orphanA), Is.Null);
        Assert.That(await _sidecarService.ReadSidecarAsync(orphanB), Is.Null);
    }

    [Test]
    public async Task ScanBranchAsync_WithBaseBranch_FlagsAddedOrphans()
    {
        CreateChangeDir("brand-new-orphan");

        _commandRunner
            .Setup(r => r.RunAsync(
                "git",
                It.Is<string>(s => s.Contains("--diff-filter=A") && s.Contains("main..HEAD")),
                _tempDir))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                ExitCode = 0,
                Output = "openspec/changes/brand-new-orphan/proposal.md\nopenspec/changes/brand-new-orphan/tasks.md\n"
            });

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId, baseBranch: "main");

        Assert.That(result.OrphanChanges, Has.Count.EqualTo(1));
        Assert.That(result.OrphanChanges[0].CreatedOnBranch, Is.True);
    }

    [Test]
    public void StripDatePrefix_ValidDate_RemovesPrefix()
    {
        Assert.That(ChangeScannerService.StripDatePrefix("2026-04-16-my-change"),
            Is.EqualTo("my-change"));
    }

    [Test]
    public void StripDatePrefix_NoDatePrefix_ReturnsUnchanged()
    {
        Assert.That(ChangeScannerService.StripDatePrefix("just-a-name"),
            Is.EqualTo("just-a-name"));
    }

    // --- helpers ---

    private string CreateChangeDir(string name)
    {
        var dir = Path.Combine(_tempDir, "openspec", "changes", name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string CreateArchivedChangeDir(string archivedFolderName)
    {
        var dir = Path.Combine(_tempDir, "openspec", "changes", "archive", archivedFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void StubArtifactStatusSuccess(string changeName, bool isComplete)
    {
        var json = $$"""
            {
              "changeName": "{{changeName}}",
              "schemaName": "spec-driven",
              "isComplete": {{(isComplete ? "true" : "false")}},
              "applyRequires": ["tasks"],
              "artifacts": []
            }
            """;

        _commandRunner
            .Setup(r => r.RunAsync("openspec",
                It.Is<string>(s => s.Contains($"--change \"{changeName}\"")),
                _tempDir))
            .ReturnsAsync(new CommandResult { Success = true, ExitCode = 0, Output = json });
    }
}
