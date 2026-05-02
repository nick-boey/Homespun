using Homespun.Features.Notifications;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Homespun.Tests.Features.Notifications;

/// <summary>
/// Unit tests for <see cref="NotificationHubExtensions.BroadcastIssueChanged"/>,
/// the unified per-issue mutation event that replaces the legacy
/// topology / field-patch split.
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
    public async Task BroadcastIssueChanged_Created_CarriesIssueBody()
    {
        var projectId = "project-123";
        var issueId = "ABC123";
        var issue = new IssueResponse { Id = issueId, Title = "Created issue" };

        await _hubContextMock.Object.BroadcastIssueChanged(
            projectId, IssueChangeType.Created, issueId, issue);

        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssueChanged",
                It.Is<object?[]>(args =>
                    args.Length == 4 &&
                    (string)args[0]! == projectId &&
                    (IssueChangeType)args[1]! == IssueChangeType.Created &&
                    (string)args[2]! == issueId &&
                    ReferenceEquals(args[3], issue)),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastIssueChanged_Updated_CarriesIssueBody()
    {
        var projectId = "project-123";
        var issueId = "XYZ789";
        var issue = new IssueResponse { Id = issueId, Title = "Updated" };

        await _hubContextMock.Object.BroadcastIssueChanged(
            projectId, IssueChangeType.Updated, issueId, issue);

        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssueChanged",
                It.Is<object?[]>(args =>
                    (IssueChangeType)args[1]! == IssueChangeType.Updated &&
                    ReferenceEquals(args[3], issue)),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastIssueChanged_Deleted_CarriesNullIssue()
    {
        var projectId = "project-456";
        var issueId = "DEL001";

        await _hubContextMock.Object.BroadcastIssueChanged(
            projectId, IssueChangeType.Deleted, issueId, issue: null);

        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssueChanged",
                It.Is<object?[]>(args =>
                    (IssueChangeType)args[1]! == IssueChangeType.Deleted &&
                    args[3] == null),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastIssueChanged_BulkEvent_AcceptsNullIssueId()
    {
        var projectId = "project-789";

        await _hubContextMock.Object.BroadcastIssueChanged(
            projectId, IssueChangeType.Updated, issueId: null, issue: null);

        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssueChanged",
                It.Is<object?[]>(args =>
                    args.Length == 4 &&
                    (string)args[0]! == projectId &&
                    args[2] == null &&
                    args[3] == null),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastIssueChanged_SendsToBothAllClientsAndProjectGroup()
    {
        var projectId = "project-abc";
        var issue = new IssueResponse { Id = "id", Title = "x" };

        await _hubContextMock.Object.BroadcastIssueChanged(
            projectId, IssueChangeType.Updated, "id", issue);

        _allClientsMock.Verify(x => x.SendCoreAsync("IssueChanged", It.IsAny<object?[]>(), default), Times.Once);
        _clientsMock.Verify(x => x.Group($"project-{projectId}"), Times.Once);
        _groupClientsMock.Verify(x => x.SendCoreAsync("IssueChanged", It.IsAny<object?[]>(), default), Times.Once);
    }
}
