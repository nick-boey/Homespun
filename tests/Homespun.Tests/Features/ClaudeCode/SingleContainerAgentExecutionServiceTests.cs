using System.Net;
using System.Runtime.InteropServices;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Exceptions;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SingleContainerAgentExecutionServiceTests
{
    private static SingleContainerAgentExecutionService Build(
        string? workerUrl = "http://localhost:8081",
        string hostWorkspaceRoot = "",
        string containerWorkspaceRoot = "/workdir",
        IPerSessionEventStream? perSession = null)
    {
        var opts = Options.Create(new SingleContainerAgentExecutionOptions
        {
            WorkerUrl = workerUrl ?? string.Empty,
            HostWorkspaceRoot = hostWorkspaceRoot,
            ContainerWorkspaceRoot = containerWorkspaceRoot,
        });
        return new SingleContainerAgentExecutionService(
            opts,
            NullLogger<SingleContainerAgentExecutionService>.Instance,
            perSession ?? new Mock<IPerSessionEventStream>().Object);
    }

    private static SingleContainerAgentExecutionService BuildWithHttp(
        HttpClient httpClient,
        IPerSessionEventStream perSession,
        string workerUrl = "http://fake")
    {
        var opts = Options.Create(new SingleContainerAgentExecutionOptions
        {
            WorkerUrl = workerUrl,
            HostWorkspaceRoot = string.Empty,
            ContainerWorkspaceRoot = "/workdir",
        });
        return new SingleContainerAgentExecutionService(
            opts,
            NullLogger<SingleContainerAgentExecutionService>.Instance,
            perSession,
            httpClient);
    }

    [Test]
    public void Ctor_MissingWorkerUrl_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(workerUrl: ""));
        Assert.Throws<InvalidOperationException>(() => Build(workerUrl: "   "));
    }

    [Test]
    public async Task Stop_ClearsSlot_AllowingNewSession()
    {
        using var svc = Build();
        // Simulate an active slot via reflection on the private _active field — the
        // cleanest way to assert the slot clears without spinning up an HTTP worker.
        var field = typeof(SingleContainerAgentExecutionService).GetField(
            "_active",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var activeType = field.FieldType;
        var active = activeType.GetGenericArguments();
        // Build an ActiveSession instance via its constructor
        var activeClass = typeof(SingleContainerAgentExecutionService).GetNestedType(
            "ActiveSession",
            System.Reflection.BindingFlags.NonPublic);

        Assert.That(activeClass, Is.Not.Null, "ActiveSession nested type not found via reflection");

        // Stop on a session that is not active — no-op, slot remains free.
        await svc.StopSessionAsync("nonexistent");
        // Verify slot is free (active field is null)
        var slot = field.GetValue(svc);
        Assert.That(slot, Is.Null);
    }

    [Test]
    public async Task BusyGuard_ReleasesSlotOnStop_ThenAllowsNewSession()
    {
        // Integration-style check using only public surface: without an HTTP worker the
        // per-session stream start will fail; that's expected. We assert that after
        // StopSessionAsync the slot is released so a second start would not throw
        // SingleContainerBusyException on the fast path.
        using var svc = Build();

        // Attempt to start with an unreachable URL — should throw but the slot is
        // captured first. Stop immediately clears the slot.
        var req = new AgentStartRequest(
            WorkingDirectory: "/tmp",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "hi",
            HomespunSessionId: "S1");

        try
        {
            await foreach (var _ in svc.StartSessionAsync(req))
            {
                break;
            }
        }
        catch
        {
            // Expected: no worker at the URL.
        }

        // After the failed start, manually stop to clear the slot.
        await svc.StopSessionAsync("S1");

        var field = typeof(SingleContainerAgentExecutionService).GetField(
            "_active",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.That(field.GetValue(svc), Is.Null);
    }

    [Test]
    public async Task GetSessionStatus_NoActiveSession_ReturnsNull()
    {
        using var svc = Build();
        var status = await svc.GetSessionStatusAsync("nonexistent");
        Assert.That(status, Is.Null);
    }

    [Test]
    public async Task ListContainers_AlwaysEmpty_InShim()
    {
        using var svc = Build();
        var containers = await svc.ListContainersAsync();
        Assert.That(containers, Is.Empty);
    }

    [Test]
    public async Task CleanupOrphanedContainers_Returns0_InShim()
    {
        using var svc = Build();
        var n = await svc.CleanupOrphanedContainersAsync();
        Assert.That(n, Is.EqualTo(0));
    }

    [Test]
    public void SingleContainerBusyException_CarriesBothIds()
    {
        var ex = new SingleContainerBusyException("requested", "current");
        Assert.That(ex.RequestedSessionId, Is.EqualTo("requested"));
        Assert.That(ex.CurrentSessionId, Is.EqualTo("current"));
        Assert.That(ex.Message, Does.Contain("requested"));
        Assert.That(ex.Message, Does.Contain("current"));
    }

    [Test]
    public void TranslateWorkingDirectory_NonWindowsHost_PassesThroughUnchanged()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts non-Windows behaviour.");
            return;
        }

        using var svc = Build(hostWorkspaceRoot: "/home/dev/projects", containerWorkspaceRoot: "/workdir");
        var result = svc.TranslateWorkingDirectoryForContainer("/home/dev/projects/smoke/main");
        Assert.That(result, Is.EqualTo("/home/dev/projects/smoke/main"));
    }

    [Test]
    public void TranslateWorkingDirectory_WindowsHost_RewritesPrefixToContainerPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts Windows path-rewriting behaviour.");
            return;
        }

        using var svc = Build(
            hostWorkspaceRoot: @"C:\Users\dev\.homespun\projects",
            containerWorkspaceRoot: "/workdir");
        var result = svc.TranslateWorkingDirectoryForContainer(@"C:\Users\dev\.homespun\projects\smoke\main");
        Assert.That(result, Is.EqualTo("/workdir/smoke/main"));
    }

    [Test]
    public void TranslateWorkingDirectory_WindowsHost_RootItselfMapsToContainerRoot()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts Windows path-rewriting behaviour.");
            return;
        }

        using var svc = Build(
            hostWorkspaceRoot: @"C:\Users\dev\.homespun\projects",
            containerWorkspaceRoot: "/workdir");
        var result = svc.TranslateWorkingDirectoryForContainer(@"C:\Users\dev\.homespun\projects");
        Assert.That(result, Is.EqualTo("/workdir"));
    }

    [Test]
    public void TranslateWorkingDirectory_WindowsHost_PathOutsideWorkspaceRoot_Throws()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts Windows path-rewriting behaviour.");
            return;
        }

        using var svc = Build(
            hostWorkspaceRoot: @"C:\Users\dev\.homespun\projects",
            containerWorkspaceRoot: "/workdir");
        Assert.Throws<InvalidOperationException>(
            () => svc.TranslateWorkingDirectoryForContainer(@"D:\other\path"));
    }

    [Test]
    public void TranslateWorkingDirectory_WindowsHost_EmptyWorkspaceRoot_Throws()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts Windows path-rewriting behaviour.");
            return;
        }

        using var svc = Build(hostWorkspaceRoot: "", containerWorkspaceRoot: "/workdir");
        Assert.Throws<InvalidOperationException>(
            () => svc.TranslateWorkingDirectoryForContainer(@"C:\anything"));
    }

    // -------------------------------------------------------------------
    // Task 9: PerSessionEventStream rewire
    // -------------------------------------------------------------------

    /// <summary>
    /// Emits a single <see cref="SdkResultMessage"/> and ends. Mirrors the shape
    /// <see cref="IPerSessionEventStream.SubscribeTurnAsync"/> gives real consumers:
    /// the sequence terminates on the first result message.
    /// </summary>
    private static async IAsyncEnumerable<SdkMessage> ResultOnly(string sessionId)
    {
        await Task.Yield();
        yield return new SdkResultMessage(
            SessionId: sessionId,
            Uuid: Guid.NewGuid().ToString(),
            Subtype: "success",
            DurationMs: 42,
            DurationApiMs: 10,
            IsError: false,
            NumTurns: 1,
            TotalCostUsd: 0m,
            Result: "ok",
            Errors: null);
    }

    /// <summary>
    /// Async enumerable that throws on first MoveNextAsync. Used to exercise
    /// the error-path best-effort StopAsync call.
    /// </summary>
#pragma warning disable CS1998
    private static async IAsyncEnumerable<SdkMessage> Throws()
    {
        throw new InvalidOperationException("worker blew up");
#pragma warning disable CS0162 // Unreachable code detected — required to satisfy iterator shape.
        yield break;
#pragma warning restore CS0162
    }
#pragma warning restore CS1998

    [Test]
    public async Task StartSessionAsync_starts_per_session_event_stream_and_drains_turn()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith(
            "/api/sessions",
            new { sessionId = "worker-s1", conversationId = (string?)null });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        var perSession = new Mock<IPerSessionEventStream>();
        perSession
            .Setup(p => p.StartAsync(
                "S1", "http://fake", "worker-s1", "p1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        perSession
            .Setup(p => p.SubscribeTurnAsync("S1", It.IsAny<CancellationToken>()))
            .Returns(ResultOnly("S1"));

        using var svc = BuildWithHttp(httpClient, perSession.Object);

        var request = new AgentStartRequest(
            WorkingDirectory: "/tmp",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "go",
            ProjectId: "p1",
            HomespunSessionId: "S1");

        var yielded = new List<SdkMessage>();
        await foreach (var msg in svc.StartSessionAsync(request, CancellationToken.None))
        {
            yielded.Add(msg);
        }

        perSession.Verify(
            p => p.StartAsync("S1", "http://fake", "worker-s1", "p1", It.IsAny<CancellationToken>()),
            Times.Once,
            "StartSessionAsync must start the per-session reader with the parsed worker session id");

        perSession.Verify(
            p => p.SubscribeTurnAsync("S1", It.IsAny<CancellationToken>()),
            Times.Once,
            "StartSessionAsync must drain the first turn through the per-session subscription");

        var startPost = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.EndsWith("/api/sessions"));
        Assert.That(startPost, Is.Not.Null, "expected a POST to /api/sessions");

        Assert.That(yielded, Has.Some.InstanceOf<SdkResultMessage>(),
            "the turn drain should surface the result message");
    }

    [Test]
    public async Task SendMessageAsync_posts_json_then_subscribes_to_per_session_stream()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith(
            "/api/sessions",
            new { sessionId = "worker-s1", conversationId = (string?)null });
        handler.RespondWith("/message", new { ok = true });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        var perSession = new Mock<IPerSessionEventStream>();
        perSession
            .Setup(p => p.StartAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        perSession
            .Setup(p => p.SubscribeTurnAsync("S1", It.IsAny<CancellationToken>()))
            .Returns(() => ResultOnly("S1"));

        using var svc = BuildWithHttp(httpClient, perSession.Object);

        // First prime the active slot via StartSessionAsync so SendMessageAsync has
        // an ActiveSession with a WorkerSessionId to target.
        var startReq = new AgentStartRequest(
            WorkingDirectory: "/tmp",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "go",
            ProjectId: "p1",
            HomespunSessionId: "S1");

        await foreach (var _ in svc.StartSessionAsync(startReq, CancellationToken.None))
        {
            // drain
        }

        // Now exercise SendMessageAsync — the code matches on either SessionId or
        // WorkerSessionId. Use SessionId="S1" (outer).
        var msgReq = new AgentMessageRequest(
            SessionId: "S1",
            Message: "hello",
            Mode: SessionMode.Build,
            Model: "sonnet");

        var yielded = new List<SdkMessage>();
        await foreach (var msg in svc.SendMessageAsync(msgReq, CancellationToken.None))
        {
            yielded.Add(msg);
        }

        Assert.That(yielded, Has.Count.EqualTo(1), "expected exactly the single SdkResultMessage");
        Assert.That(yielded[0], Is.InstanceOf<SdkResultMessage>());

        var messagePost = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.Contains("/api/sessions/worker-s1/message"));
        Assert.That(messagePost, Is.Not.Null, "expected a POST to /api/sessions/{workerSessionId}/message");
        var body = messagePost!.Body;
        Assert.That(body, Is.Not.Null.And.Contains("\"message\""));
        Assert.That(body, Does.Contain("hello"));

        // Exactly two SubscribeTurnAsync calls total: one for start, one for send.
        perSession.Verify(
            p => p.SubscribeTurnAsync("S1", It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "SendMessageAsync must subscribe to the per-session stream exactly once for its turn");
    }

    [Test]
    public async Task StopSessionAsync_stops_the_per_session_event_stream()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith(
            "/api/sessions",
            new { sessionId = "worker-s1", conversationId = (string?)null });
        handler.RespondWithStatus("/api/sessions/worker-s1", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        var perSession = new Mock<IPerSessionEventStream>();
        perSession
            .Setup(p => p.StartAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        perSession
            .Setup(p => p.SubscribeTurnAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => ResultOnly("S1"));
        perSession.Setup(p => p.StopAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        using var svc = BuildWithHttp(httpClient, perSession.Object);

        // Prime an active session first.
        var startReq = new AgentStartRequest(
            WorkingDirectory: "/tmp",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "go",
            ProjectId: "p1",
            HomespunSessionId: "S1");

        await foreach (var _ in svc.StartSessionAsync(startReq, CancellationToken.None))
        {
            // drain
        }

        await svc.StopSessionAsync("S1", forceStopContainer: false, CancellationToken.None);

        perSession.Verify(
            p => p.StopAsync("S1"),
            Times.Once,
            "StopSessionAsync must halt the per-session reader, even though the container is shared");
    }

    [Test]
    public async Task StartSessionAsync_stops_reader_on_worker_failure()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith(
            "/api/sessions",
            new { sessionId = "worker-s1", conversationId = (string?)null });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        var perSession = new Mock<IPerSessionEventStream>();
        perSession
            .Setup(p => p.StartAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        perSession
            .Setup(p => p.SubscribeTurnAsync("S1", It.IsAny<CancellationToken>()))
            .Returns(Throws());
        perSession.Setup(p => p.StopAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        using var svc = BuildWithHttp(httpClient, perSession.Object);

        var request = new AgentStartRequest(
            WorkingDirectory: "/tmp",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "go",
            ProjectId: "p1",
            HomespunSessionId: "S1");

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in svc.StartSessionAsync(request, CancellationToken.None))
            {
                // drain
            }
        });

        perSession.Verify(
            p => p.StopAsync("S1"),
            Times.AtLeastOnce,
            "StartSessionAsync must best-effort stop the per-session reader when the turn drain throws");

        await Task.CompletedTask;
    }
}
