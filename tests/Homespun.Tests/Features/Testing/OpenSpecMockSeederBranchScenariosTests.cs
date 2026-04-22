using Homespun.Features.Commands;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing.Services;
using Homespun.Shared.Models.Commands;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Testing;

/// <summary>
/// End-to-end test: the OpenSpecMockSeeder's per-branch fixtures surface correctly through
/// <see cref="ChangeScannerService"/> (which is what
/// <see cref="BranchStateResolverService.GetOrScanAsync"/> delegates to) and through the
/// <see cref="IssueGraphOpenSpecEnricher"/>.
/// </summary>
[TestFixture]
public class OpenSpecMockSeederBranchScenariosTests
{
    private string _tempDir = null!;
    private OpenSpecMockSeeder _seeder = null!;
    private ChangeScannerService _scanner = null!;
    private Mock<ICommandRunner> _commandRunner = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openspec-branch-scenarios-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _seeder = new OpenSpecMockSeeder(
            new Mock<ITempDataFolderService>().Object,
            NullLogger<OpenSpecMockSeeder>.Instance);

        _commandRunner = new Mock<ICommandRunner>();
        _commandRunner
            .Setup(c => c.RunAsync("openspec", It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                ExitCode = 0,
                Output = """{"changeName":"x","schemaName":"spec-driven","isComplete":false}""",
                Error = string.Empty
            });

        _scanner = new ChangeScannerService(
            new SidecarService(NullLogger<SidecarService>.Instance),
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
    public async Task SeedBranch_Issue006_ProducesOneLinkedInProgressChange()
    {
        var clonePath = await SeedBranchAsync("ISSUE-006");

        var scan = await _scanner.ScanBranchAsync(clonePath, "ISSUE-006");

        Assert.That(scan.LinkedChanges, Has.Count.EqualTo(1));
        Assert.That(scan.LinkedChanges[0].Name, Is.EqualTo("api-v2-impl"));
        Assert.That(scan.LinkedChanges[0].IsArchived, Is.False);
        Assert.That(scan.OrphanChanges, Is.Empty);
    }

    [Test]
    public async Task SeedBranch_Issue002_ProducesTwoOrphans()
    {
        var clonePath = await SeedBranchAsync("ISSUE-002");

        var scan = await _scanner.ScanBranchAsync(clonePath, "ISSUE-002");

        Assert.That(scan.OrphanChanges, Has.Count.EqualTo(2));
        Assert.That(scan.LinkedChanges, Is.Empty);
    }

    [Test]
    public async Task SeedBranch_Issue001_ProducesInheritedChange()
    {
        var clonePath = await SeedBranchAsync("ISSUE-001");

        var scan = await _scanner.ScanBranchAsync(clonePath, "ISSUE-001");

        Assert.That(scan.InheritedChangeNames, Does.Contain("inherited-from-main"));
        Assert.That(scan.LinkedChanges, Is.Empty);
        Assert.That(scan.OrphanChanges, Is.Empty);
    }

    [Test]
    public async Task SeedBranch_Issue003_ProducesNoOpenspecAtAll()
    {
        var clonePath = await SeedBranchAsync("ISSUE-003");

        Assert.That(Directory.Exists(Path.Combine(clonePath, "openspec")), Is.False);
    }

    [Test]
    public async Task EnrichAsync_Issue006_ReportsWithChangeAndPhases()
    {
        var clonePath = await SeedBranchAsync("ISSUE-006");
        var enricher = BuildEnricher(clonePath, branch: "feature/api-v2+ISSUE-006", issueId: "ISSUE-006");

        var response = new TaskGraphResponse
        {
            Nodes = new List<TaskGraphNodeResponse>
            {
                new() { Issue = new IssueResponse { Id = "ISSUE-006" } },
            }
        };

        await enricher.EnrichAsync("proj", response);

        var state = response.OpenSpecStates["ISSUE-006"];
        Assert.That(state.BranchState, Is.EqualTo(BranchPresence.WithChange));
        Assert.That(state.ChangeName, Is.EqualTo("api-v2-impl"));
        Assert.That(state.Phases, Is.Not.Empty);
    }

    [Test]
    public async Task EnrichAsync_Issue002_ReportsExistsWithMultipleOrphans()
    {
        var clonePath = await SeedBranchAsync("ISSUE-002");
        var enricher = BuildEnricher(clonePath, branch: "feature/dark-mode+ISSUE-002", issueId: "ISSUE-002");

        var response = new TaskGraphResponse
        {
            Nodes = new List<TaskGraphNodeResponse>
            {
                new() { Issue = new IssueResponse { Id = "ISSUE-002" } }
            }
        };

        await enricher.EnrichAsync("proj", response);

        var state = response.OpenSpecStates["ISSUE-002"];
        Assert.That(state.BranchState, Is.EqualTo(BranchPresence.Exists));
        Assert.That(state.ChangeState, Is.EqualTo(ChangePhase.None));
        Assert.That(state.Orphans, Has.Count.EqualTo(2));
    }

    private async Task<string> SeedBranchAsync(string branchFleeceId)
    {
        var clonePath = Path.Combine(_tempDir, $"clone-{branchFleeceId}");
        Directory.CreateDirectory(Path.Combine(clonePath, "openspec", "changes"));
        await _seeder.SeedBranchAsync(clonePath, $"feat/x+{branchFleeceId}", branchFleeceId);
        return clonePath;
    }

    private IssueGraphOpenSpecEnricher BuildEnricher(string clonePath, string branch, string issueId)
    {
        var dataStore = new Mock<IDataStore>();
        dataStore.Setup(d => d.GetProject("proj"))
            .Returns(new Project
            {
                Id = "proj",
                Name = "Demo",
                LocalPath = _tempDir,
                DefaultBranch = "main",
            });
        dataStore.Setup(d => d.GetPullRequestsByProject("proj"))
            .Returns(new List<Homespun.Shared.Models.PullRequests.PullRequest>());

        var cloneService = new Mock<IGitCloneService>();
        cloneService.Setup(c => c.GetClonePathForBranchAsync(_tempDir, branch))
            .ReturnsAsync(clonePath);
        cloneService.Setup(c => c.ListClonesAsync(_tempDir))
            .ReturnsAsync(new List<Homespun.Shared.Models.Git.CloneInfo>());

        var branchResolver = new Mock<IIssueBranchResolverService>();
        branchResolver.Setup(b => b.ResolveIssueBranchAsync("proj", issueId, It.IsAny<BranchResolutionContext>()))
            .ReturnsAsync(branch);

        var transitionService = new Mock<IFleeceIssueTransitionService>();
        var reconciliation = new ChangeReconciliationService(
            _scanner,
            transitionService.Object,
            NullLogger<ChangeReconciliationService>.Instance);

        var stateResolver = new BranchStateResolverService(
            new BranchStateCacheService(TimeProvider.System),
            reconciliation,
            dataStore.Object,
            cloneService.Object,
            TimeProvider.System,
            NullLogger<BranchStateResolverService>.Instance);

        return new IssueGraphOpenSpecEnricher(
            branchResolver.Object,
            stateResolver,
            dataStore.Object,
            cloneService.Object,
            _scanner,
            NullLogger<IssueGraphOpenSpecEnricher>.Instance);
    }
}
