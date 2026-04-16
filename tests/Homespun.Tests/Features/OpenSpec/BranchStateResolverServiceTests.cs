using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.OpenSpec;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class BranchStateResolverServiceTests
{
    private FakeTimeProvider _time = null!;
    private BranchStateCacheService _cache = null!;
    private Mock<IChangeReconciliationService> _reconciliation = null!;
    private Mock<IDataStore> _dataStore = null!;
    private Mock<IGitCloneService> _cloneService = null!;
    private BranchStateResolverService _resolver = null!;
    private string _tempCloneDir = null!;

    private const string ProjectId = "proj-1";
    private const string Branch = "feat/foo+issue-123";
    private const string FleeceId = "issue-123";

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2026-04-16T12:00:00Z"));
        _cache = new BranchStateCacheService(_time);
        _reconciliation = new Mock<IChangeReconciliationService>();
        _dataStore = new Mock<IDataStore>();
        _cloneService = new Mock<IGitCloneService>();

        _tempCloneDir = Path.Combine(Path.GetTempPath(), $"resolver-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempCloneDir);

        _dataStore.Setup(d => d.GetProject(ProjectId))
            .Returns(new Project
            {
                Id = ProjectId,
                Name = "proj",
                LocalPath = "/tmp/repo",
                DefaultBranch = "main"
            });

        _cloneService.Setup(c => c.GetClonePathForBranchAsync("/tmp/repo", Branch))
            .ReturnsAsync(_tempCloneDir);

        _resolver = new BranchStateResolverService(
            _cache,
            _reconciliation.Object,
            _dataStore.Object,
            _cloneService.Object,
            _time,
            NullLogger<BranchStateResolverService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempCloneDir))
        {
            Directory.Delete(_tempCloneDir, recursive: true);
        }
    }

    [Test]
    public async Task GetOrScanAsync_ColdCache_InvokesReconcileAndCaches()
    {
        var scan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            LinkedChanges = new List<LinkedChangeInfo>
            {
                new()
                {
                    Name = "my-change",
                    Directory = "/tmp/my-change",
                    CreatedBy = "agent",
                    IsArchived = false,
                    TaskState = new TaskStateSummary { TasksDone = 2, TasksTotal = 5, NextIncomplete = "Next one" }
                }
            }
        };
        _reconciliation.Setup(r => r.ReconcileAsync(ProjectId, _tempCloneDir, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);

        var snapshot = await _resolver.GetOrScanAsync(ProjectId, Branch);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.ProjectId, Is.EqualTo(ProjectId));
        Assert.That(snapshot.FleeceId, Is.EqualTo(FleeceId));
        Assert.That(snapshot.Changes, Has.Count.EqualTo(1));
        Assert.That(snapshot.Changes[0].TasksDone, Is.EqualTo(2));
        Assert.That(snapshot.Changes[0].NextIncomplete, Is.EqualTo("Next one"));

        // Second call should hit the cache, not reconcile again.
        var cached = await _resolver.GetOrScanAsync(ProjectId, Branch);
        Assert.That(cached, Is.SameAs(snapshot));
        _reconciliation.Verify(r => r.ReconcileAsync(ProjectId, _tempCloneDir, FleeceId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GetOrScanAsync_CacheExpired_RescansAndReplaces()
    {
        _reconciliation.Setup(r => r.ReconcileAsync(ProjectId, _tempCloneDir, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchScanResult { BranchFleeceId = FleeceId });

        await _resolver.GetOrScanAsync(ProjectId, Branch);

        // Cross the 60-second TTL boundary.
        _time.Advance(TimeSpan.FromSeconds(61));

        await _resolver.GetOrScanAsync(ProjectId, Branch);

        _reconciliation.Verify(r => r.ReconcileAsync(ProjectId, _tempCloneDir, FleeceId, null, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task GetOrScanAsync_BranchWithoutFleeceId_ReturnsNull()
    {
        var snapshot = await _resolver.GetOrScanAsync(ProjectId, "feat/no-suffix");

        Assert.That(snapshot, Is.Null);
        _reconciliation.Verify(r => r.ReconcileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetOrScanAsync_UnknownProject_ReturnsNull()
    {
        _dataStore.Setup(d => d.GetProject("missing")).Returns((Project?)null);

        var snapshot = await _resolver.GetOrScanAsync("missing", Branch);

        Assert.That(snapshot, Is.Null);
    }

    [Test]
    public async Task GetOrScanAsync_NoCloneOnDisk_ReturnsNull()
    {
        _cloneService.Setup(c => c.GetClonePathForBranchAsync("/tmp/repo", Branch))
            .ReturnsAsync((string?)null);

        var snapshot = await _resolver.GetOrScanAsync(ProjectId, Branch);

        Assert.That(snapshot, Is.Null);
    }
}
