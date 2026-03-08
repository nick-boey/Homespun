using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Homespun.Server.Features.ClaudeCode.Services;
using Homespun.Server.Features.Gitgraph.Services;
using Homespun.Server.Features.Testing.Services;
using Homespun.Server.Features.Testing;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Api.Tests;

[TestFixture]
public class GraphServiceAgentStatusTests
{
    private IServiceProvider _services = null!;
    private IGraphService _graphService = null!;
    private Mock<IClaudeSessionStore> _sessionStoreMock = null!;
    private Mock<ILogger<GraphService>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();

        // Add logging
        _loggerMock = new Mock<ILogger<GraphService>>();
        services.AddSingleton(_loggerMock.Object);

        // Add required services
        services.AddSingleton<IGraphService, GraphService>();
        services.AddSingleton<IFleeceService, MockFleeceService>();
        services.AddSingleton<IPRCacheService, MockPRCacheService>();
        services.AddSingleton<IGitService, MockGitService>();
        services.AddSingleton<IDataStore, MockDataStore>();
        services.AddSingleton<IProjectStore, MockProjectStore>();

        // Mock session store
        _sessionStoreMock = new Mock<IClaudeSessionStore>();
        services.AddSingleton(_sessionStoreMock.Object);

        _services = services.BuildServiceProvider();
        _graphService = _services.GetRequiredService<IGraphService>();
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_ShouldIncludeActiveAgentStatuses()
    {
        // Arrange
        var projectId = "test-project";
        var issueId = "ABC123";
        var sessionId = "session-123";

        // Mock project
        var projectStore = (MockProjectStore)_services.GetRequiredService<IProjectStore>();
        projectStore.AddProject(projectId, "Test Project", "owner", "repo", "/path");

        // Mock fleece service to return an issue
        var fleeceService = (MockFleeceService)_services.GetRequiredService<IFleeceService>();
        fleeceService.SetTaskGraphResult(new TaskGraph
        {
            Nodes = new[]
            {
                new TaskGraphNode
                {
                    Issue = new Issue
                    {
                        Id = issueId,
                        Title = "Test Issue",
                        Type = IssueType.Task,
                        Status = IssueStatus.Progress
                    },
                    Lane = 1,
                    Row = 1,
                    IsActionable = true
                }
            },
            Edges = Array.Empty<TaskGraphEdge>()
        });

        // Mock active session
        var session = new ClaudeSession
        {
            Id = sessionId,
            EntityId = issueId,
            ProjectId = projectId,
            Status = ClaudeSessionStatus.Running,
            LastActivityAt = DateTime.UtcNow
        };

        _sessionStoreMock
            .Setup(x => x.GetByProjectId(projectId))
            .Returns(new[] { session });

        // Act
        var result = await _graphService.BuildEnhancedTaskGraphAsync(projectId, 10);

        // Assert
        result.Should().NotBeNull();
        result!.AgentStatuses.Should().ContainKey(issueId);

        var agentStatus = result.AgentStatuses[issueId];
        agentStatus.IsActive.Should().BeTrue();
        agentStatus.Status.Should().Be("Running");
        agentStatus.SessionId.Should().Be(sessionId);
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_ShouldHandleCaseInsensitiveEntityIdMatching()
    {
        // Arrange
        var projectId = "test-project";
        var issueIdUpperCase = "ABC123";
        var issueIdLowerCase = "abc123";
        var sessionId = "session-123";

        // Mock project
        var projectStore = (MockProjectStore)_services.GetRequiredService<IProjectStore>();
        projectStore.AddProject(projectId, "Test Project", "owner", "repo", "/path");

        // Mock fleece service to return an issue with uppercase ID
        var fleeceService = (MockFleeceService)_services.GetRequiredService<IFleeceService>();
        fleeceService.SetTaskGraphResult(new TaskGraph
        {
            Nodes = new[]
            {
                new TaskGraphNode
                {
                    Issue = new Issue
                    {
                        Id = issueIdUpperCase,
                        Title = "Test Issue",
                        Type = IssueType.Task,
                        Status = IssueStatus.Progress
                    },
                    Lane = 1,
                    Row = 1,
                    IsActionable = true
                }
            },
            Edges = Array.Empty<TaskGraphEdge>()
        });

        // Mock session with lowercase entity ID
        var session = new ClaudeSession
        {
            Id = sessionId,
            EntityId = issueIdLowerCase,
            ProjectId = projectId,
            Status = ClaudeSessionStatus.WaitingForInput,
            LastActivityAt = DateTime.UtcNow
        };

        _sessionStoreMock
            .Setup(x => x.GetByProjectId(projectId))
            .Returns(new[] { session });

        // Act
        var result = await _graphService.BuildEnhancedTaskGraphAsync(projectId, 10);

        // Assert
        result.Should().NotBeNull();
        result!.AgentStatuses.Should().ContainKey(issueIdUpperCase);

        var agentStatus = result.AgentStatuses[issueIdUpperCase];
        agentStatus.IsActive.Should().BeTrue();
        agentStatus.Status.Should().Be("WaitingForInput");
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_ShouldIgnoreSessionsWithNullOrEmptyEntityId()
    {
        // Arrange
        var projectId = "test-project";
        var issueId = "ABC123";

        // Mock project
        var projectStore = (MockProjectStore)_services.GetRequiredService<IProjectStore>();
        projectStore.AddProject(projectId, "Test Project", "owner", "repo", "/path");

        // Mock fleece service
        var fleeceService = (MockFleeceService)_services.GetRequiredService<IFleeceService>();
        fleeceService.SetTaskGraphResult(new TaskGraph
        {
            Nodes = new[]
            {
                new TaskGraphNode
                {
                    Issue = new Issue
                    {
                        Id = issueId,
                        Title = "Test Issue",
                        Type = IssueType.Task,
                        Status = IssueStatus.Progress
                    },
                    Lane = 1,
                    Row = 1,
                    IsActionable = true
                }
            },
            Edges = Array.Empty<TaskGraphEdge>()
        });

        // Mock sessions with invalid entity IDs
        var sessions = new[]
        {
            new ClaudeSession
            {
                Id = "session-1",
                EntityId = null!,
                ProjectId = projectId,
                Status = ClaudeSessionStatus.Running,
                LastActivityAt = DateTime.UtcNow
            },
            new ClaudeSession
            {
                Id = "session-2",
                EntityId = "",
                ProjectId = projectId,
                Status = ClaudeSessionStatus.Running,
                LastActivityAt = DateTime.UtcNow
            },
            new ClaudeSession
            {
                Id = "session-3",
                EntityId = "   ",
                ProjectId = projectId,
                Status = ClaudeSessionStatus.Running,
                LastActivityAt = DateTime.UtcNow
            }
        };

        _sessionStoreMock
            .Setup(x => x.GetByProjectId(projectId))
            .Returns(sessions);

        // Act
        var result = await _graphService.BuildEnhancedTaskGraphAsync(projectId, 10);

        // Assert
        result.Should().NotBeNull();
        result!.AgentStatuses.Should().BeEmpty();

        // Verify logging
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("excluded 3 with null/empty EntityId")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_ShouldUseLatestSessionWhenMultipleExistForSameEntity()
    {
        // Arrange
        var projectId = "test-project";
        var issueId = "ABC123";

        // Mock project
        var projectStore = (MockProjectStore)_services.GetRequiredService<IProjectStore>();
        projectStore.AddProject(projectId, "Test Project", "owner", "repo", "/path");

        // Mock fleece service
        var fleeceService = (MockFleeceService)_services.GetRequiredService<IFleeceService>();
        fleeceService.SetTaskGraphResult(new TaskGraph
        {
            Nodes = new[]
            {
                new TaskGraphNode
                {
                    Issue = new Issue
                    {
                        Id = issueId,
                        Title = "Test Issue",
                        Type = IssueType.Task,
                        Status = IssueStatus.Progress
                    },
                    Lane = 1,
                    Row = 1,
                    IsActionable = true
                }
            },
            Edges = Array.Empty<TaskGraphEdge>()
        });

        // Mock multiple sessions for same entity with different timestamps
        var oldSession = new ClaudeSession
        {
            Id = "old-session",
            EntityId = issueId,
            ProjectId = projectId,
            Status = ClaudeSessionStatus.Stopped,
            LastActivityAt = DateTime.UtcNow.AddHours(-2)
        };

        var newSession = new ClaudeSession
        {
            Id = "new-session",
            EntityId = issueId,
            ProjectId = projectId,
            Status = ClaudeSessionStatus.Running,
            LastActivityAt = DateTime.UtcNow
        };

        _sessionStoreMock
            .Setup(x => x.GetByProjectId(projectId))
            .Returns(new[] { oldSession, newSession });

        // Act
        var result = await _graphService.BuildEnhancedTaskGraphAsync(projectId, 10);

        // Assert
        result.Should().NotBeNull();
        result!.AgentStatuses.Should().ContainKey(issueId);

        var agentStatus = result.AgentStatuses[issueId];
        agentStatus.SessionId.Should().Be("new-session");
        agentStatus.Status.Should().Be("Running");
        agentStatus.IsActive.Should().BeTrue();
    }

    [TestCase(ClaudeSessionStatus.Starting, true)]
    [TestCase(ClaudeSessionStatus.RunningHooks, true)]
    [TestCase(ClaudeSessionStatus.Running, true)]
    [TestCase(ClaudeSessionStatus.WaitingForInput, true)]
    [TestCase(ClaudeSessionStatus.WaitingForQuestionAnswer, true)]
    [TestCase(ClaudeSessionStatus.WaitingForPlanExecution, true)]
    [TestCase(ClaudeSessionStatus.Stopped, false)]
    [TestCase(ClaudeSessionStatus.Error, false)]
    public async Task BuildEnhancedTaskGraphAsync_ShouldSetIsActiveBasedOnSessionStatus(
        ClaudeSessionStatus status, bool expectedIsActive)
    {
        // Arrange
        var projectId = "test-project";
        var issueId = "ABC123";
        var sessionId = "session-123";

        // Mock project
        var projectStore = (MockProjectStore)_services.GetRequiredService<IProjectStore>();
        projectStore.AddProject(projectId, "Test Project", "owner", "repo", "/path");

        // Mock fleece service
        var fleeceService = (MockFleeceService)_services.GetRequiredService<IFleeceService>();
        fleeceService.SetTaskGraphResult(new TaskGraph
        {
            Nodes = new[]
            {
                new TaskGraphNode
                {
                    Issue = new Issue
                    {
                        Id = issueId,
                        Title = "Test Issue",
                        Type = IssueType.Task,
                        Status = IssueStatus.Progress
                    },
                    Lane = 1,
                    Row = 1,
                    IsActionable = true
                }
            },
            Edges = Array.Empty<TaskGraphEdge>()
        });

        // Mock session with specified status
        var session = new ClaudeSession
        {
            Id = sessionId,
            EntityId = issueId,
            ProjectId = projectId,
            Status = status,
            LastActivityAt = DateTime.UtcNow
        };

        _sessionStoreMock
            .Setup(x => x.GetByProjectId(projectId))
            .Returns(new[] { session });

        // Act
        var result = await _graphService.BuildEnhancedTaskGraphAsync(projectId, 10);

        // Assert
        result.Should().NotBeNull();

        if (expectedIsActive || status == ClaudeSessionStatus.Error)
        {
            // Active statuses and Error should be included
            result!.AgentStatuses.Should().ContainKey(issueId);
            var agentStatus = result.AgentStatuses[issueId];
            agentStatus.IsActive.Should().Be(expectedIsActive);
            agentStatus.Status.Should().Be(status.ToString());
        }
        else
        {
            // Stopped status might still be included but marked as not active
            if (result!.AgentStatuses.ContainsKey(issueId))
            {
                var agentStatus = result.AgentStatuses[issueId];
                agentStatus.IsActive.Should().BeFalse();
                agentStatus.Status.Should().Be(status.ToString());
            }
        }
    }
}