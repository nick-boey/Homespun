using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Verifies FI-6 wiring — <see cref="SessionLifecycleService.StopSessionAsync"/> and
/// <see cref="SessionLifecycleService.RestartSessionAsync"/> hand the session id to
/// <see cref="IPlanArtefactStore.RemoveForSessionAsync"/> so the plan files written
/// during the session are reclaimed.
/// </summary>
[TestFixture]
public class SessionLifecyclePlanArtefactTests
{
    private static SessionLifecycleService Build(
        out Mock<IPlanArtefactStore> planArtefacts,
        out IClaudeSessionStore sessionStore,
        out Mock<ISessionStateManager> stateManager,
        out Mock<IAgentExecutionService> agentExecution)
    {
        planArtefacts = new Mock<IPlanArtefactStore>();
        sessionStore = new ClaudeSessionStore();
        stateManager = new Mock<ISessionStateManager>();
        agentExecution = new Mock<IAgentExecutionService>();

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hubContext = new Mock<IHubContext<ClaudeCodeHub>>();
        hubContext.SetupGet(h => h.Clients).Returns(hubClients.Object);

        return new SessionLifecycleService(
            sessionStore,
            NullLogger<SessionLifecycleService>.Instance,
            hubContext.Object,
            new Mock<IClaudeSessionDiscovery>().Object,
            new Mock<ISessionMetadataStore>().Object,
            new Mock<IHooksService>().Object,
            agentExecution.Object,
            new Mock<IAGUIEventService>().Object,
            stateManager.Object,
            planArtefacts.Object,
            new Lazy<IMessageProcessingService>(() => new Mock<IMessageProcessingService>().Object));
    }

    [Test]
    public async Task StopSessionAsync_invokes_RemoveForSessionAsync_for_the_session()
    {
        var service = Build(
            out var planArtefacts,
            out var sessionStore,
            out var stateManager,
            out var agentExecution);

        var session = new ClaudeSession
        {
            Id = "sess-stop-1",
            EntityId = "e1",
            ProjectId = "p1",
            WorkingDirectory = "/tmp",
            Mode = SessionMode.Plan,
            Model = "opus",
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow,
            PlanFilePath = "/tmp/.claude/plans/plan.md"
        };
        sessionStore.Add(session);

        string? agentId = "agent-1";
        stateManager.Setup(s => s.TryRemoveAgentSessionId("sess-stop-1", out agentId)).Returns(true);

        await service.StopSessionAsync("sess-stop-1");

        planArtefacts.Verify(p => p.RemoveForSessionAsync("sess-stop-1", It.IsAny<CancellationToken>()), Times.Once);
        agentExecution.Verify(
            a => a.StopSessionAsync("agent-1", true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RestartSessionAsync_invokes_RemoveForSessionAsync_and_clears_session_plan_state()
    {
        var service = Build(
            out var planArtefacts,
            out var sessionStore,
            out var stateManager,
            out var agentExecution);

        var session = new ClaudeSession
        {
            Id = "sess-restart-1",
            EntityId = "e1",
            ProjectId = "p1",
            WorkingDirectory = "/tmp",
            Mode = SessionMode.Build,
            Model = "sonnet",
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow,
            PlanFilePath = "/tmp/.claude/plans/plan.md",
            PlanContent = "# plan",
            HasPendingPlanApproval = true
        };
        sessionStore.Add(session);

        stateManager.Setup(s => s.GetAgentSessionId("sess-restart-1")).Returns("agent-1");
        agentExecution
            .Setup(a => a.RestartContainerAsync("agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerRestartResult(
                WorkingDirectory: "/tmp",
                ConversationId: "conv-1",
                ProjectId: "p1",
                IssueId: "e1",
                NewContainerId: "container-new",
                NewWorkerUrl: "http://localhost"));

        var result = await service.RestartSessionAsync("sess-restart-1");

        Assert.That(result, Is.Not.Null);
        Assert.That(session.PlanFilePath, Is.Null);
        Assert.That(session.PlanContent, Is.Null);
        Assert.That(session.HasPendingPlanApproval, Is.False);

        planArtefacts.Verify(p => p.RemoveForSessionAsync("sess-restart-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
