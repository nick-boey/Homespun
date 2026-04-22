using Homespun.Features.Testing;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Homespun.Tests.Features.Testing;

[TestFixture]
public class MockGitCloneServiceTests
{
    private string _tempDir = null!;
    private string _repoPath = null!;
    private OpenSpecMockSeeder _seeder = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mock-clone-test-{Guid.NewGuid():N}");
        _repoPath = Path.Combine(_tempDir, "demo-project");
        Directory.CreateDirectory(_repoPath);

        // Seed minimal source content the clone service should mirror.
        Directory.CreateDirectory(Path.Combine(_repoPath, ".fleece"));
        File.WriteAllText(Path.Combine(_repoPath, ".fleece", "issues_abc.jsonl"), "{}");
        Directory.CreateDirectory(Path.Combine(_repoPath, "openspec", "changes"));
        File.WriteAllText(Path.Combine(_repoPath, "openspec", "project.md"), "# demo");

        _seeder = new OpenSpecMockSeeder(
            new TempDataFolderService(NullLogger<TempDataFolderService>.Instance),
            NullLogger<OpenSpecMockSeeder>.Instance);
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
    public async Task CreateCloneAsync_DevMockMode_MaterialisesDirectoryWithOpenspecAndFleece()
    {
        var service = BuildService(testWorkingDir: null);

        var clonePath = await service.CreateCloneAsync(_repoPath, "feature/api-v2+ISSUE-006");

        Assert.That(clonePath, Is.Not.Null);
        Assert.That(Directory.Exists(clonePath), Is.True, "clone dir created on disk");
        Assert.That(Directory.Exists(Path.Combine(clonePath!, "openspec")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(clonePath!, ".fleece")), Is.True);
    }

    [Test]
    public async Task CreateCloneAsync_BranchMappedToIssue006_HasInProgressChange()
    {
        var service = BuildService(testWorkingDir: null);

        var clonePath = await service.CreateCloneAsync(_repoPath, "feature/api-v2+ISSUE-006");

        Assert.That(Directory.Exists(Path.Combine(clonePath!, "openspec", "changes", "api-v2-impl")), Is.True);
    }

    [Test]
    public async Task CreateCloneAsync_BranchMappedToIssue002_HasTwoOrphanChanges()
    {
        var service = BuildService(testWorkingDir: null);

        var clonePath = await service.CreateCloneAsync(_repoPath, "feature/dark-mode+ISSUE-002");

        Assert.That(Directory.Exists(Path.Combine(clonePath!, "openspec", "changes", "dark-mode-tokens")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(clonePath!, "openspec", "changes", "dark-mode-toggle")), Is.True);
        Assert.That(File.Exists(Path.Combine(clonePath!, "openspec", "changes", "dark-mode-tokens", ".homespun.yaml")), Is.False);
        Assert.That(File.Exists(Path.Combine(clonePath!, "openspec", "changes", "dark-mode-toggle", ".homespun.yaml")), Is.False);
    }

    [Test]
    public async Task CreateCloneAsync_BranchMappedToIssue003_HasNoOpenspec()
    {
        var service = BuildService(testWorkingDir: null);

        var clonePath = await service.CreateCloneAsync(_repoPath, "feature/logging+ISSUE-003");

        Assert.That(Directory.Exists(Path.Combine(clonePath!, "openspec")), Is.False,
            "ISSUE-003 scenario removes the entire openspec/ tree from the clone");
    }

    [Test]
    public async Task CreateCloneAsync_LiveMode_DoesNotCreatePerBranchDirectory()
    {
        var sharedWorkspace = Path.Combine(_tempDir, "shared-workspace");
        Directory.CreateDirectory(sharedWorkspace);
        var service = BuildService(testWorkingDir: sharedWorkspace);

        var clonePath = await service.CreateCloneAsync(_repoPath, "feature/api-v2+ISSUE-006");

        Assert.That(clonePath, Is.EqualTo(sharedWorkspace), "live mode routes every clone to the shared workspace");
        Assert.That(Directory.Exists($"{_repoPath}-clones/feature-api-v2+ISSUE-006"), Is.False,
            "no per-branch directory should be created in live mode");
    }

    [Test]
    public async Task CreateCloneFromRemoteBranchAsync_DevMockMode_MaterialisesDirectory()
    {
        var service = BuildService(testWorkingDir: null);

        var clonePath = await service.CreateCloneFromRemoteBranchAsync(_repoPath, "feature/api-v2+ISSUE-006");

        Assert.That(Directory.Exists(clonePath!), Is.True);
        Assert.That(Directory.Exists(Path.Combine(clonePath!, "openspec")), Is.True);
    }

    private MockGitCloneService BuildService(string? testWorkingDir)
    {
        var options = Options.Create(new LiveClaudeTestOptions
        {
            TestWorkingDirectory = testWorkingDir ?? string.Empty
        });
        return new MockGitCloneService(NullLogger<MockGitCloneService>.Instance, options, _seeder);
    }
}
