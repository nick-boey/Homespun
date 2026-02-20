using Fleece.Core.Models;
using Homespun.Features.Fleece.Controllers;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Projects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ControllerCreateIssueRequest = Homespun.Features.Fleece.Controllers.CreateIssueRequest;
using ControllerUpdateIssueRequest = Homespun.Features.Fleece.Controllers.UpdateIssueRequest;

namespace Homespun.Tests.Features.Fleece;

/// <summary>
/// Unit tests for IssuesController SignalR broadcasting.
/// </summary>
[TestFixture]
public class IssuesControllerTests
{
    private Mock<IFleeceService> _fleeceServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IHubContext<NotificationHub>> _notificationHubMock = null!;
    private Mock<IHubClients> _clientsMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private Mock<IClientProxy> _groupClientsMock = null!;
    private IssuesController _controller = null!;

    private static readonly Project TestProject = new()
    {
        Id = "project-123",
        Name = "Test Project",
        LocalPath = "/path/to/project",
        DefaultBranch = "main"
    };

    private static Issue CreateTestIssue(string id, string title, IssueType type = IssueType.Task) => new()
    {
        Id = id,
        Title = title,
        Type = type,
        Status = IssueStatus.Open,
        LastUpdate = DateTimeOffset.UtcNow
    };

    [SetUp]
    public void SetUp()
    {
        _fleeceServiceMock = new Mock<IFleeceService>();
        _projectServiceMock = new Mock<IProjectService>();
        _notificationHubMock = new Mock<IHubContext<NotificationHub>>();
        _clientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();
        _groupClientsMock = new Mock<IClientProxy>();

        _notificationHubMock.Setup(x => x.Clients).Returns(_clientsMock.Object);
        _clientsMock.Setup(x => x.All).Returns(_allClientsMock.Object);
        _clientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(_groupClientsMock.Object);

        _controller = new IssuesController(
            _fleeceServiceMock.Object,
            _projectServiceMock.Object,
            _notificationHubMock.Object,
            NullLogger<IssuesController>.Instance);
    }

    #region Create Tests

    [Test]
    public async Task Create_BroadcastsIssuesChangedEvent_WithCreatedChangeType()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.CreateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IssueType>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>()))
            .ReturnsAsync(issue);

        var request = new ControllerCreateIssueRequest { ProjectId = TestProject.Id, Title = "Test Issue" };

        // Act
        await _controller.Create(request);

        // Assert
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Created &&
                    (string)args[2]! == issue.Id),
                default),
            Times.Once);
    }

    [Test]
    public async Task Create_ReturnsCreatedResult()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.CreateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IssueType>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>()))
            .ReturnsAsync(issue);

        var request = new ControllerCreateIssueRequest { ProjectId = TestProject.Id, Title = "Test Issue" };

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
    }

    [Test]
    public async Task Create_ProjectNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new ControllerCreateIssueRequest { ProjectId = "nonexistent", Title = "Test Issue" };

        // Act
        await _controller.Create(request);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    #endregion

    #region Update Tests

    [Test]
    public async Task Update_BroadcastsIssuesChangedEvent_WithUpdatedChangeType()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Updated Issue", IssueType.Bug);

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>()))
            .ReturnsAsync(issue);

        var request = new ControllerUpdateIssueRequest { ProjectId = TestProject.Id, Title = "Updated Issue" };

        // Act
        await _controller.Update(issueId, request);

        // Assert
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Updated &&
                    (string)args[2]! == issueId),
                default),
            Times.Once);
    }

    [Test]
    public async Task Update_IssueNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>()))
            .ReturnsAsync((Issue?)null);

        var request = new ControllerUpdateIssueRequest { ProjectId = TestProject.Id, Title = "Updated Issue" };

        // Act
        await _controller.Update("nonexistent", request);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    #endregion

    #region Delete Tests

    [Test]
    public async Task Delete_BroadcastsIssuesChangedEvent_WithDeletedChangeType()
    {
        // Arrange
        var issueId = "ABC123";

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.DeleteIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(true);

        // Act
        await _controller.Delete(issueId, TestProject.Id);

        // Assert
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Deleted &&
                    (string)args[2]! == issueId),
                default),
            Times.Once);
    }

    [Test]
    public async Task Delete_IssueNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.DeleteIssueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        await _controller.Delete("nonexistent", TestProject.Id);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    #endregion
}
