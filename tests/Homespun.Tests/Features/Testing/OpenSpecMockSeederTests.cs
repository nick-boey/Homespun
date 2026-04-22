using Homespun.Features.OpenSpec.Services;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Testing;

[TestFixture]
public class OpenSpecMockSeederTests
{
    private string _tempDir = null!;
    private OpenSpecMockSeeder _seeder = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openspec-seeder-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var folderService = new Mock<ITempDataFolderService>().Object;
        _seeder = new OpenSpecMockSeeder(folderService, NullLogger<OpenSpecMockSeeder>.Instance);
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
    public async Task SeedAsync_WritesProjectMd()
    {
        await _seeder.SeedAsync(_tempDir, EmptyMap);

        Assert.That(File.Exists(Path.Combine(_tempDir, "openspec", "project.md")), Is.True);
    }

    [Test]
    public async Task SeedAsync_WritesAllFourMainBranchChanges()
    {
        await _seeder.SeedAsync(_tempDir, EmptyMap);

        Assert.That(Directory.Exists(ChangeDir("api-v2-design")), Is.True, "api-v2-design");
        Assert.That(Directory.Exists(ChangeDir("rate-limiting")), Is.True, "rate-limiting");
        Assert.That(Directory.Exists(ChangeDir("orphan-on-main")), Is.True, "orphan-on-main");
        Assert.That(Directory.Exists(Path.Combine(_tempDir, "openspec", "changes", "archive", "2026-01-15-old-feature")),
            Is.True, "archived");
    }

    [Test]
    public async Task SeedAsync_NonOrphanChangesHaveSidecarsLinkingToSeededIssues()
    {
        await _seeder.SeedAsync(_tempDir, EmptyMap);

        // In-progress and ready-to-archive both linked
        Assert.That(File.Exists(SidecarPath("api-v2-design")), Is.True);
        Assert.That(File.Exists(SidecarPath("rate-limiting")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempDir, "openspec", "changes", "archive",
            "2026-01-15-old-feature", ".homespun.yaml")), Is.True);

        // Orphan-on-main has NO sidecar
        Assert.That(File.Exists(SidecarPath("orphan-on-main")), Is.False);
    }

    [Test]
    public async Task SeedAsync_InProgressChange_HasThreePhasesWithPartialCompletion()
    {
        await _seeder.SeedAsync(_tempDir, EmptyMap);

        var tasksMd = await File.ReadAllTextAsync(TasksPath("api-v2-design"));
        var parsed = TasksParser.Parse(tasksMd);

        Assert.That(parsed.Phases, Has.Count.EqualTo(3), "expected three ## Phase headings");
        Assert.That(parsed.TasksTotal, Is.GreaterThan(parsed.TasksDone), "partial completion");
        Assert.That(parsed.TasksDone, Is.GreaterThan(0), "some tasks ticked");
    }

    [Test]
    public async Task SeedAsync_ReadyToArchiveChange_HasAllTasksTicked()
    {
        await _seeder.SeedAsync(_tempDir, EmptyMap);

        var tasksMd = await File.ReadAllTextAsync(TasksPath("rate-limiting"));
        var parsed = TasksParser.Parse(tasksMd);

        Assert.That(parsed.TasksTotal, Is.GreaterThan(0));
        Assert.That(parsed.TasksDone, Is.EqualTo(parsed.TasksTotal),
            "ready-to-archive change should have every checkbox ticked");
    }

    [Test]
    public async Task SeedAsync_InProgressChange_HasDesignAndSpec()
    {
        await _seeder.SeedAsync(_tempDir, EmptyMap);

        Assert.That(File.Exists(Path.Combine(ChangeDir("api-v2-design"), "design.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(ChangeDir("api-v2-design"), "specs", "api-v2-design", "spec.md")), Is.True);
    }

    [Test]
    public async Task SeedAsync_SidecarFleeceIdMatchesIssue006ForInProgressChange()
    {
        await _seeder.SeedAsync(_tempDir, EmptyMap);

        var sidecarYaml = await File.ReadAllTextAsync(SidecarPath("api-v2-design"));
        Assert.That(sidecarYaml, Does.Contain("fleeceId: ISSUE-006"));
        Assert.That(sidecarYaml, Does.Contain("createdBy: agent"));
    }

    [Test]
    public async Task SeedAsync_FollowedByGitInit_TracksOpenspecFiles()
    {
        // Mirrors MockDataSeederService.StartAsync ordering: seed openspec, then init git.
        await _seeder.SeedAsync(_tempDir, EmptyMap);
        var fleeceSeeder = new FleeceIssueSeeder(NullLogger<FleeceIssueSeeder>.Instance);
        fleeceSeeder.InitializeGitRepository(_tempDir);

        var lsFiles = await RunGitAsync(_tempDir, "ls-files");

        Assert.That(lsFiles, Does.Contain("openspec/project.md"));
        Assert.That(lsFiles, Does.Contain("openspec/changes/api-v2-design/tasks.md"));
        Assert.That(lsFiles, Does.Contain("openspec/changes/archive/2026-01-15-old-feature/.homespun.yaml"));
    }

    private static async Task<string> RunGitAsync(string workingDirectory, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout;
    }

    private string ChangeDir(string name) => Path.Combine(_tempDir, "openspec", "changes", name);
    private string SidecarPath(string changeName) => Path.Combine(ChangeDir(changeName), ".homespun.yaml");
    private string TasksPath(string changeName) => Path.Combine(ChangeDir(changeName), "tasks.md");

    private static readonly IReadOnlyDictionary<string, string> EmptyMap =
        new Dictionary<string, string>();
}
