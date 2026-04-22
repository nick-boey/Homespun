using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Features.Notifications;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    private IServiceProvider _services = null!;

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

        // No snapshot store / refresher registered — the helper must tolerate missing deps.
        _services = new ServiceCollection().BuildServiceProvider();
    }

    [Test]
    public async Task BroadcastIssueTopologyChanged_SendsToProjectGroup()
    {
        // Arrange
        var projectId = "project-123";
        var changeType = IssueChangeType.Created;
        var issueId = "ABC123";

        // Act
        await _hubContextMock.Object.BroadcastIssueTopologyChanged(_services, projectId, changeType, issueId);

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
    public async Task BroadcastIssueTopologyChanged_SendsToAllClients()
    {
        // Arrange
        var projectId = "project-123";
        var changeType = IssueChangeType.Updated;
        var issueId = "XYZ789";

        // Act
        await _hubContextMock.Object.BroadcastIssueTopologyChanged(_services, projectId, changeType, issueId);

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
    public async Task BroadcastIssueTopologyChanged_Deleted_BroadcastsCorrectChangeType()
    {
        // Arrange
        var projectId = "project-456";
        var changeType = IssueChangeType.Deleted;
        var issueId = "DEL001";

        // Act
        await _hubContextMock.Object.BroadcastIssueTopologyChanged(_services, projectId, changeType, issueId);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    (IssueChangeType)args[1]! == IssueChangeType.Deleted),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastIssueFieldsPatched_EmitsIssueFieldsPatched_WhenPatchPushEnabled()
    {
        // Arrange
        var projectId = "project-789";
        var issueId = "PATCH01";
        var patch = new IssueFieldPatch { Title = "new" };
        var services = BuildServicesWithPatchPush(enabled: true);

        // Act
        await _hubContextMock.Object.BroadcastIssueFieldsPatched(services, projectId, issueId, patch);

        // Assert — Delta 3 default: emits the dedicated IssueFieldsPatched event.
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssueFieldsPatched",
                It.Is<object?[]>(args =>
                    args.Length == 3 &&
                    (string)args[0]! == projectId &&
                    (string)args[1]! == issueId &&
                    ReferenceEquals(args[2], patch)),
                default),
            Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssueFieldsPatched", It.IsAny<object?[]>(), default),
            Times.Once);
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    [Test]
    public async Task BroadcastIssueFieldsPatched_FallsBackToIssuesChanged_WhenPatchPushDisabled()
    {
        // Arrange
        var projectId = "project-789";
        var issueId = "PATCH01";
        var patch = new IssueFieldPatch { Title = "new" };
        var services = BuildServicesWithPatchPush(enabled: false);

        // Act
        await _hubContextMock.Object.BroadcastIssueFieldsPatched(services, projectId, issueId, patch);

        // Assert — kill switch: falls back to IssuesChanged so clients refetch.
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Once);
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssueFieldsPatched", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    [Test]
    public async Task BroadcastIssueFieldsPatched_DefaultsToEnabled_WhenOptionsMissing()
    {
        // Arrange — service provider has no IOptionsMonitor<TaskGraphPatchPushOptions>
        var projectId = "project-789";
        var issueId = "PATCH01";
        var patch = new IssueFieldPatch { Title = "new" };

        // Act
        await _hubContextMock.Object.BroadcastIssueFieldsPatched(_services, projectId, issueId, patch);

        // Assert — missing options resolves to Enabled=true default.
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssueFieldsPatched", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    private static IServiceProvider BuildServicesWithPatchPush(bool enabled)
    {
        var services = new ServiceCollection();
        services.Configure<TaskGraphPatchPushOptions>(o => o.Enabled = enabled);
        return services.BuildServiceProvider();
    }
}
