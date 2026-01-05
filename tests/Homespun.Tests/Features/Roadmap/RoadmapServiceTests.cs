using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Roadmap;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Project = Homespun.Features.PullRequests.Data.Entities.Project;
using TrackedPullRequest = Homespun.Features.PullRequests.Data.Entities.PullRequest;

namespace Homespun.Tests.Features.Roadmap;

[TestFixture]
public class RoadmapServiceTests
{
    private TestDataStore _dataStore = null!;
    private Mock<ICommandRunner> _mockRunner = null!;
    private Mock<IGitWorktreeService> _mockWorktreeService = null!;
    private RoadmapService _service = null!;
    private string _tempDir = null!;
    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new TestDataStore();
        _mockRunner = new Mock<ICommandRunner>();
        _mockWorktreeService = new Mock<IGitWorktreeService>();

        // Create temp directory structure: tempDir/main (to mimic ~/.homespun/src/repo/main)
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _projectDir = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(_projectDir);

        _service = new RoadmapService(
            _dataStore, 
            _mockRunner.Object, 
            _mockWorktreeService.Object,
            NullLogger<RoadmapService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "Test Project",
            LocalPath = _projectDir, // Use the "main" subdirectory
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };

        await _dataStore.AddProjectAsync(project);
        return project;
    }

    private void CreateRoadmapFile(string content)
    {
        // Write to ROADMAP.local.json at the parent directory level (not in the project dir)
        File.WriteAllText(Path.Combine(_tempDir, "ROADMAP.local.json"), content);
    }

    private string GetLocalRoadmapPath()
    {
        return Path.Combine(_tempDir, "ROADMAP.local.json");
    }

    #region 3.1 Read and Display Future Changes

    [Test]
    public async Task FutureChanges_LoadFromRoadmap_DisplaysInList()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/feature-one",
                    "shortTitle": "feature-one",
                    "group": "core",
                    "type": "feature",
                    "title": "Feature One",
                    "parents": []
                },
                {
                    "id": "web/bug/bug-fix",
                    "shortTitle": "bug-fix",
                    "group": "web",
                    "type": "bug",
                    "title": "Bug Fix",
                    "parents": []
                }
            ]
        }
        """);

        // Act
        var result = await _service.GetFutureChangesAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Change.Id, Is.EqualTo("core/feature/feature-one"));
        Assert.That(result[1].Change.Id, Is.EqualTo("web/bug/bug-fix"));
    }

    [Test]
    public async Task FutureChanges_WithParents_DisplaysAll()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/parent",
                    "shortTitle": "parent",
                    "group": "core",
                    "type": "feature",
                    "title": "Parent",
                    "parents": []
                },
                {
                    "id": "core/feature/child-1",
                    "shortTitle": "child-1",
                    "group": "core",
                    "type": "feature",
                    "title": "Child 1",
                    "parents": ["core/feature/parent"]
                },
                {
                    "id": "core/feature/child-2",
                    "shortTitle": "child-2",
                    "group": "core",
                    "type": "feature",
                    "title": "Child 2",
                    "parents": ["core/feature/parent"]
                }
            ]
        }
        """);

        // Act
        var result = await _service.GetFutureChangesAsync(project.Id);

        // Assert - Should return flat list with all changes
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result.Any(r => r.Change.Id == "core/feature/parent"), Is.True);
        Assert.That(result.Any(r => r.Change.Id == "core/feature/child-1"), Is.True);
        Assert.That(result.Any(r => r.Change.Id == "core/feature/child-2"), Is.True);
    }

    [Test]
    public async Task FutureChanges_CalculatesTimeFromDependencyDepth()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/root",
                    "shortTitle": "root",
                    "group": "core",
                    "type": "feature",
                    "title": "Root",
                    "parents": []
                },
                {
                    "id": "core/feature/child",
                    "shortTitle": "child",
                    "group": "core",
                    "type": "feature",
                    "title": "Child",
                    "parents": ["core/feature/root"]
                },
                {
                    "id": "core/feature/grandchild",
                    "shortTitle": "grandchild",
                    "group": "core",
                    "type": "feature",
                    "title": "Grandchild",
                    "parents": ["core/feature/child"]
                }
            ]
        }
        """);

        // Act
        var result = await _service.GetFutureChangesAsync(project.Id);

        // Assert - Root at depth 0 -> t=2, child at depth 1 -> t=3, grandchild at depth 2 -> t=4
        Assert.That(result.First(r => r.Change.ShortTitle == "root").Time, Is.EqualTo(2));
        Assert.That(result.First(r => r.Change.ShortTitle == "child").Time, Is.EqualTo(3));
        Assert.That(result.First(r => r.Change.ShortTitle == "grandchild").Time, Is.EqualTo(4));
    }

    [Test]
    public async Task FutureChanges_GroupsDisplayedCorrectly()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/core-feature",
                    "shortTitle": "core-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Core Feature",
                    "parents": []
                },
                {
                    "id": "web/feature/web-feature",
                    "shortTitle": "web-feature",
                    "group": "web",
                    "type": "feature",
                    "title": "Web Feature",
                    "parents": []
                },
                {
                    "id": "api/feature/api-feature",
                    "shortTitle": "api-feature",
                    "group": "api",
                    "type": "feature",
                    "title": "API Feature",
                    "parents": []
                }
            ]
        }
        """);

        // Act
        var result = await _service.GetFutureChangesAsync(project.Id);
        var byGroup = await _service.GetFutureChangesByGroupAsync(project.Id);

        // Assert
        Assert.That(byGroup.Keys, Does.Contain("core"));
        Assert.That(byGroup.Keys, Does.Contain("web"));
        Assert.That(byGroup.Keys, Does.Contain("api"));
        Assert.That(byGroup["core"], Has.Count.EqualTo(1));
        Assert.That(byGroup["web"], Has.Count.EqualTo(1));
        Assert.That(byGroup["api"], Has.Count.EqualTo(1));
    }

    #endregion

    #region 3.2 Promote Future Change to Current PR

    [Test]
    public async Task PromoteChange_CreatesWorktree_WithIdAsBranchName()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/new-feature",
                    "shortTitle": "new-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "New Feature",
                    "parents": []
                }
            ]
        }
        """);

        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(
            project.LocalPath,
            "core/feature/new-feature",
            It.IsAny<bool>(),
            It.IsAny<string?>()))
            .ReturnsAsync("/worktrees/core-feature-new-feature");

        // Act
        var result = await _service.PromoteChangeAsync(project.Id, "core/feature/new-feature");

        // Assert
        Assert.That(result, Is.Not.Null);
        _mockWorktreeService.Verify(w => w.CreateWorktreeAsync(
            project.LocalPath,
            "core/feature/new-feature",
            It.IsAny<bool>(),
            It.IsAny<string?>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task PromoteChange_CreatesFeature_WithChangeDetails()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/new-feature",
                    "shortTitle": "new-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "New Feature Title",
                    "description": "Detailed description of the feature",
                    "parents": []
                }
            ]
        }
        """);

        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string?>()))
            .ReturnsAsync("/worktrees/new-feature");

        // Act
        var result = await _service.PromoteChangeAsync(project.Id, "core/feature/new-feature");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Title, Is.EqualTo("New Feature Title"));
        Assert.That(result.Description, Is.EqualTo("Detailed description of the feature"));
        Assert.That(result.BranchName, Is.EqualTo("core/feature/new-feature"));
        Assert.That(result.Status, Is.EqualTo(OpenPullRequestStatus.InDevelopment));
    }

    [Test]
    public async Task PromoteChange_RemovesFromRoadmap_AndUpdatesParentReferences()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/feature-to-promote",
                    "shortTitle": "feature-to-promote",
                    "group": "core",
                    "type": "feature",
                    "title": "Feature To Promote",
                    "parents": []
                },
                {
                    "id": "core/feature/dependent-feature",
                    "shortTitle": "dependent-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Dependent Feature",
                    "parents": ["core/feature/feature-to-promote"]
                }
            ]
        }
        """);

        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string?>()))
            .ReturnsAsync("/worktrees/feature");

        // Act
        await _service.PromoteChangeAsync(project.Id, "core/feature/feature-to-promote");

        // Assert - Verify the roadmap was updated
        var roadmapPath = GetLocalRoadmapPath();
        var updatedRoadmap = await RoadmapParser.LoadAsync(roadmapPath);

        // Promoted change should be removed
        Assert.That(updatedRoadmap.Changes, Has.Count.EqualTo(1));
        Assert.That(updatedRoadmap.Changes[0].Id, Is.EqualTo("core/feature/dependent-feature"));
        
        // Parent reference should be removed from dependent feature
        Assert.That(updatedRoadmap.Changes[0].Parents, Is.Empty);
    }

    #endregion

    #region 3.3 Plan Update PRs

    [Test]
    public async Task PlanUpdate_OnlyRoadmapChanges_DetectedCorrectly()
    {
        // Arrange
        var project = await CreateTestProject();
        var pullRequest = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Update Roadmap",
            BranchName = "plan-update/add-features",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(pullRequest);

        // Mock git diff showing only ROADMAP.json changed
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("diff") && s.Contains("--name-only")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true, Output = "ROADMAP.json" });

        // Act
        var result = await _service.IsPlanUpdateOnlyAsync(pullRequest.Id);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task PlanUpdate_MixedChanges_NotPlanUpdateOnly()
    {
        // Arrange
        var project = await CreateTestProject();
        var pullRequest = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Mixed Changes",
            BranchName = "core/feature/mixed",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(pullRequest);

        // Mock git diff showing multiple files changed
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("diff") && s.Contains("--name-only")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true, Output = "ROADMAP.json\nsrc/SomeCode.cs" });

        // Act
        var result = await _service.IsPlanUpdateOnlyAsync(pullRequest.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PlanUpdate_ValidatesSchema_BeforePromotion()
    {
        // Arrange
        var project = await CreateTestProject();
        // Create invalid ROADMAP.json
        CreateRoadmapFile("""{ "version": "1.1", "changes": [{ "invalid": true }] }""");

        // Act & Assert
        var ex = Assert.ThrowsAsync<RoadmapValidationException>(
            async () => await _service.GetFutureChangesAsync(project.Id));
        Assert.That(ex!.Message, Does.Contain("id").Or.Contain("required").Or.Contain("shortTitle"));
    }

    [Test]
    public void PlanUpdate_UsesPlanUpdateGroup_InBranchNaming()
    {
        // Act
        var branchName = _service.GeneratePlanUpdateBranchName("add-new-features");

        // Assert
        Assert.That(branchName, Does.StartWith("plan-update/"));
        Assert.That(branchName, Is.EqualTo("plan-update/chore/add-new-features"));
    }

    #endregion

    #region 3.4 Add New Change

    [Test]
    public async Task AddChange_AppendsToExistingRoadmap()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/existing-feature",
                    "shortTitle": "existing-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Existing Feature",
                    "parents": []
                }
            ]
        }
        """);

        var newChange = new FutureChange
        {
            Id = "web/feature/new-feature",
            ShortTitle = "new-feature",
            Group = "web",
            Type = ChangeType.Feature,
            Title = "New Feature"
        };

        // Act
        var result = await _service.AddChangeAsync(project.Id, newChange);

        // Assert
        Assert.That(result, Is.True);
        var roadmapPath = GetLocalRoadmapPath();
        var updatedRoadmap = await RoadmapParser.LoadAsync(roadmapPath);
        Assert.That(updatedRoadmap.Changes, Has.Count.EqualTo(2));
        Assert.That(updatedRoadmap.Changes[0].Id, Is.EqualTo("core/feature/existing-feature"));
        Assert.That(updatedRoadmap.Changes[1].Id, Is.EqualTo("web/feature/new-feature"));
    }

    [Test]
    public async Task AddChange_CreatesRoadmapIfNotExists()
    {
        // Arrange
        var project = await CreateTestProject();
        var roadmapPath = GetLocalRoadmapPath();
        Assert.That(File.Exists(roadmapPath), Is.False);

        var newChange = new FutureChange
        {
            Id = "core/feature/first-feature",
            ShortTitle = "first-feature",
            Group = "core",
            Type = ChangeType.Feature,
            Title = "First Feature"
        };

        // Act
        var result = await _service.AddChangeAsync(project.Id, newChange);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(File.Exists(roadmapPath), Is.True);
        var createdRoadmap = await RoadmapParser.LoadAsync(roadmapPath);
        Assert.That(createdRoadmap.Version, Is.EqualTo("1.1"));
        Assert.That(createdRoadmap.Changes, Has.Count.EqualTo(1));
        Assert.That(createdRoadmap.Changes[0].Id, Is.EqualTo("core/feature/first-feature"));
    }

    [Test]
    public async Task AddChange_PreservesOptionalFields()
    {
        // Arrange
        var project = await CreateTestProject();

        var newChange = new FutureChange
        {
            Id = "backend/bug/detailed-feature",
            ShortTitle = "detailed-feature",
            Group = "backend",
            Type = ChangeType.Bug,
            Title = "Fix Critical Bug",
            Description = "This is a critical bug fix",
            Instructions = "Step 1: Find the bug\nStep 2: Fix it",
            Priority = Priority.High,
            EstimatedComplexity = Complexity.Large
        };

        // Act
        var result = await _service.AddChangeAsync(project.Id, newChange);

        // Assert
        Assert.That(result, Is.True);
        var roadmapPath = GetLocalRoadmapPath();
        var createdRoadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var savedChange = createdRoadmap.Changes[0];
        
        Assert.That(savedChange.Description, Is.EqualTo("This is a critical bug fix"));
        Assert.That(savedChange.Instructions, Is.EqualTo("Step 1: Find the bug\nStep 2: Fix it"));
        Assert.That(savedChange.Priority, Is.EqualTo(Priority.High));
        Assert.That(savedChange.EstimatedComplexity, Is.EqualTo(Complexity.Large));
    }

    [Test]
    public async Task AddChange_ReturnsFalseIfProjectNotFound()
    {
        // Arrange
        var newChange = new FutureChange
        {
            Id = "core/feature/some-feature",
            ShortTitle = "some-feature",
            Group = "core",
            Type = ChangeType.Feature,
            Title = "Some Feature"
        };

        // Act
        var result = await _service.AddChangeAsync("non-existent-project", newChange);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region 3.5 Update Change Status

    [Test]
    public async Task UpdateChangeStatus_UpdatesStatusInRoadmap()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/test-feature",
                    "shortTitle": "test-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Test Feature",
                    "parents": [],
                    "status": "pending"
                }
            ]
        }
        """);

        // Act
        var result = await _service.UpdateChangeStatusAsync(project.Id, "core/feature/test-feature", FutureChangeStatus.InProgress);

        // Assert
        Assert.That(result, Is.True);
        var roadmapPath = GetLocalRoadmapPath();
        var updatedRoadmap = await RoadmapParser.LoadAsync(roadmapPath);
        Assert.That(updatedRoadmap.Changes[0].Status, Is.EqualTo(FutureChangeStatus.InProgress));
    }

    [Test]
    public async Task RemoveParentReference_RemovesFromAllChanges()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/parent",
                    "shortTitle": "parent",
                    "group": "core",
                    "type": "feature",
                    "title": "Parent",
                    "parents": []
                },
                {
                    "id": "core/feature/child-1",
                    "shortTitle": "child-1",
                    "group": "core",
                    "type": "feature",
                    "title": "Child 1",
                    "parents": ["core/feature/parent"]
                },
                {
                    "id": "core/feature/child-2",
                    "shortTitle": "child-2",
                    "group": "core",
                    "type": "feature",
                    "title": "Child 2",
                    "parents": ["core/feature/parent"]
                }
            ]
        }
        """);

        // Act
        var result = await _service.RemoveParentReferenceAsync(project.Id, "core/feature/parent");

        // Assert
        Assert.That(result, Is.True);
        var roadmapPath = GetLocalRoadmapPath();
        var updatedRoadmap = await RoadmapParser.LoadAsync(roadmapPath);
        
        Assert.That(updatedRoadmap.Changes[1].Parents, Is.Empty);
        Assert.That(updatedRoadmap.Changes[2].Parents, Is.Empty);
    }

    #endregion
}
