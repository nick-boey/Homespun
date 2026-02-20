using Homespun.Features.Notifications;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Homespun.Tests.Features.Notifications;

/// <summary>
/// Unit tests for NotificationHub extension methods.
/// </summary>
[TestFixture]
public class NotificationHubExtensionsTests
{
    private Mock<IHubContext<NotificationHub>> _hubContextMock = null!;
    private Mock<IHubClients> _clientsMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private Mock<IClientProxy> _groupClientsMock = null!;

    [SetUp]
    public void SetUp()
    {
        _hubContextMock = new Mock<IHubContext<NotificationHub>>();
        _clientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();
        _groupClientsMock = new Mock<IClientProxy>();

        _hubContextMock.Setup(x => x.Clients).Returns(_clientsMock.Object);
        _clientsMock.Setup(x => x.All).Returns(_allClientsMock.Object);
        _clientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(_groupClientsMock.Object);
    }

    [Test]
    public async Task BroadcastIssuesChanged_SendsToProjectGroup()
    {
        // Arrange
        var projectId = "project-123";
        var changeType = IssueChangeType.Created;
        var issueId = "ABC123";

        // Act
        await _hubContextMock.Object.BroadcastIssuesChanged(projectId, changeType, issueId);

        // Assert
        _clientsMock.Verify(x => x.Group($"project-{projectId}"), Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    args.Length == 3 &&
                    (string)args[0]! == projectId &&
                    (IssueChangeType)args[1]! == changeType &&
                    (string)args[2]! == issueId),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastIssuesChanged_SendsToAllClients()
    {
        // Arrange
        var projectId = "project-123";
        var changeType = IssueChangeType.Updated;
        var issueId = "XYZ789";

        // Act
        await _hubContextMock.Object.BroadcastIssuesChanged(projectId, changeType, issueId);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    args.Length == 3 &&
                    (string)args[0]! == projectId &&
                    (IssueChangeType)args[1]! == changeType &&
                    (string)args[2]! == issueId),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastIssuesChanged_Deleted_BroadcastsCorrectChangeType()
    {
        // Arrange
        var projectId = "project-456";
        var changeType = IssueChangeType.Deleted;
        var issueId = "DEL001";

        // Act
        await _hubContextMock.Object.BroadcastIssuesChanged(projectId, changeType, issueId);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    (IssueChangeType)args[1]! == IssueChangeType.Deleted),
                default),
            Times.Once);
    }
}
