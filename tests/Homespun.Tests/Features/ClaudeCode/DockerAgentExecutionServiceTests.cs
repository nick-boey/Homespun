using System.Net;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Secrets;
using Homespun.Shared.Models.Sessions;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for <see cref="DockerAgentExecutionService"/> covering the
/// OTel environment injection (task 7.2) and the SIGTERM-friendly stop
/// flag (task 7.3) introduced by the <c>worker-otel</c> change, plus the
/// rewire onto <see cref="IPerSessionEventStream"/> (task 8 of the
/// <c>fix-post-result-events</c> plan).
/// </summary>
[TestFixture]
public class DockerAgentExecutionServiceTests
{
    private static DockerAgentExecutionService Build(
        DockerAgentExecutionOptions? options = null,
        IPerSessionEventStream? perSession = null)
    {
        return new DockerAgentExecutionService(
            Options.Create(options ?? new DockerAgentExecutionOptions
            {
                ServerOtlpProxyUrl = "http://host.docker.internal:5101/api/otlp/v1",
            }),
            NullLogger<DockerAgentExecutionService>.Instance,
            new Mock<ISecretsService>().Object,
            perSession ?? new Mock<IPerSessionEventStream>().Object);
    }

    private static DockerAgentExecutionService BuildWithHttp(
        HttpClient httpClient,
        IPerSessionEventStream perSession,
        DockerAgentExecutionOptions? options = null)
    {
        return new DockerAgentExecutionService(
            Options.Create(options ?? new DockerAgentExecutionOptions
            {
                ServerOtlpProxyUrl = "http://host.docker.internal:5101/api/otlp/v1",
            }),
            NullLogger<DockerAgentExecutionService>.Instance,
            new Mock<ISecretsService>().Object,
            perSession,
            httpClient);
    }

    [Test]
    public void Spawned_container_receives_OTLP_PROXY_URL_env()
    {
        var svc = Build();
        var args = svc.BuildContainerDockerArgs(
            containerName: "homespun-issue-proj-issue",
            workingDirectory: "/tmp/clone",
            useRm: false,
            claudePath: null,
            issueId: "issue-1",
            projectName: "demo",
            projectId: "proj-1");

        Assert.That(args, Does.Contain(
            "-e OTLP_PROXY_URL=http://host.docker.internal:5101/api/otlp/v1"),
            $"Expected OTLP_PROXY_URL env in args, got: {args}");
    }

    [Test]
    public void Spawned_container_receives_OTEL_SERVICE_NAME_env()
    {
        var svc = Build();
        var args = svc.BuildContainerDockerArgs(
            containerName: "homespun-issue-proj-issue",
            workingDirectory: "/tmp/clone",
            useRm: false,
            claudePath: null,
            issueId: "issue-1",
            projectName: "demo",
            projectId: "proj-1");

        Assert.That(args, Does.Contain("-e OTEL_SERVICE_NAME=homespun.worker"));
    }

    [Test]
    public void Spawned_container_receives_HOMESPUN_ISSUE_ID_and_PROJECT_NAME_env()
    {
        var svc = Build();
        var args = svc.BuildContainerDockerArgs(
            containerName: "homespun-issue-proj-issue",
            workingDirectory: "/tmp/clone",
            useRm: false,
            claudePath: null,
            issueId: "issue-1",
            projectName: "demo-project",
            projectId: "proj-1");

        Assert.That(args, Does.Contain("-e HOMESPUN_ISSUE_ID=issue-1"));
        Assert.That(args, Does.Contain("-e HOMESPUN_PROJECT_NAME=demo-project"));
    }

    [Test]
    public void Spawned_container_maps_host_docker_internal_to_host_gateway()
    {
        var svc = Build();
        var args = svc.BuildContainerDockerArgs(
            containerName: "homespun-issue-proj-issue",
            workingDirectory: "/tmp/clone",
            useRm: false,
            claudePath: null,
            issueId: "issue-1",
            projectName: "demo",
            projectId: "proj-1");

        Assert.That(args, Does.Contain("--add-host=host.docker.internal:host-gateway"),
            "Worker on a user-defined bridge network must resolve " +
            "host.docker.internal via the host-gateway keyword, otherwise " +
            "the OTLP proxy at http://host.docker.internal:5101 is unreachable.");
    }

    [Test]
    public void Spawned_container_uses_configured_OTLP_proxy_URL()
    {
        var svc = Build(new DockerAgentExecutionOptions
        {
            ServerOtlpProxyUrl = "http://server:8080/api/otlp/v1",
        });
        var args = svc.BuildContainerDockerArgs(
            containerName: "homespun-issue-proj-issue",
            workingDirectory: "/tmp/clone",
            useRm: false,
            claudePath: null,
            issueId: "issue-1",
            projectName: "demo",
            projectId: "proj-1");

        Assert.That(args, Does.Contain(
            "-e OTLP_PROXY_URL=http://server:8080/api/otlp/v1"));
    }

    [Test]
    public void StopContainerAsync_uses_docker_stop_not_kill()
    {
        var stopArgs = DockerAgentExecutionService.BuildStopContainerArgs("abc123");

        Assert.That(stopArgs, Does.StartWith("stop "),
            "Stop must use `docker stop` (SIGTERM) so the worker's OTel SDK " +
            "can flush its log batch before the container dies.");
        Assert.That(stopArgs, Does.Not.Contain("kill"));
    }

    [Test]
    public void StopContainerAsync_grants_3_second_grace_before_SIGKILL()
    {
        var stopArgs = DockerAgentExecutionService.BuildStopContainerArgs("abc123");

        Assert.That(stopArgs, Does.Contain("--time 3"),
            "The 3-second grace lets the worker's BatchLogRecordProcessor " +
            "(scheduledDelayMillis = 1000) flush before SIGKILL.");
        Assert.That(stopArgs, Does.EndWith("abc123"));
    }

    [Test]
    public void Spawned_container_propagates_HOMESPUN_DEBUG_FULL_MESSAGES_when_set_on_server()
    {
        var prevUmbrella = Environment.GetEnvironmentVariable("HOMESPUN_DEBUG_FULL_MESSAGES");
        var prevPreview = Environment.GetEnvironmentVariable("CONTENT_PREVIEW_CHARS");
        try
        {
            Environment.SetEnvironmentVariable("HOMESPUN_DEBUG_FULL_MESSAGES", "true");
            Environment.SetEnvironmentVariable("CONTENT_PREVIEW_CHARS", null);

            var svc = Build();
            var args = svc.BuildContainerDockerArgs(
                containerName: "homespun-issue-proj-issue",
                workingDirectory: "/tmp/clone",
                useRm: false,
                claudePath: null,
                issueId: "issue-1",
                projectName: "demo",
                projectId: "proj-1");

            Assert.That(args, Does.Contain("-e HOMESPUN_DEBUG_FULL_MESSAGES=true"));
            Assert.That(args, Does.Contain("-e CONTENT_PREVIEW_CHARS=-1"),
                "umbrella ON should derive the no-truncation sentinel for the worker");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOMESPUN_DEBUG_FULL_MESSAGES", prevUmbrella);
            Environment.SetEnvironmentVariable("CONTENT_PREVIEW_CHARS", prevPreview);
        }
    }

    [Test]
    public void Spawned_container_omits_debug_env_when_umbrella_unset()
    {
        var prevUmbrella = Environment.GetEnvironmentVariable("HOMESPUN_DEBUG_FULL_MESSAGES");
        var prevPreview = Environment.GetEnvironmentVariable("CONTENT_PREVIEW_CHARS");
        try
        {
            Environment.SetEnvironmentVariable("HOMESPUN_DEBUG_FULL_MESSAGES", null);
            Environment.SetEnvironmentVariable("CONTENT_PREVIEW_CHARS", null);

            var svc = Build();
            var args = svc.BuildContainerDockerArgs(
                containerName: "homespun-issue-proj-issue",
                workingDirectory: "/tmp/clone",
                useRm: false,
                claudePath: null,
                issueId: "issue-1",
                projectName: "demo",
                projectId: "proj-1");

            Assert.That(args, Does.Not.Contain("HOMESPUN_DEBUG_FULL_MESSAGES"));
            Assert.That(args, Does.Not.Contain("-e CONTENT_PREVIEW_CHARS"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOMESPUN_DEBUG_FULL_MESSAGES", prevUmbrella);
            Environment.SetEnvironmentVariable("CONTENT_PREVIEW_CHARS", prevPreview);
        }
    }

    [Test]
    public void ResolveWorkerUserFlag_prefers_explicit_option()
    {
        var svc = Build(new DockerAgentExecutionOptions
        {
            ServerOtlpProxyUrl = "http://server:8080/api/otlp/v1",
            WorkerUser = "1000:1000",
        });

        Assert.That(svc.ResolveWorkerUserFlag(), Is.EqualTo("1000:1000"));
    }

    [Test]
    public void ParseUserFlag_accepts_valid_and_rejects_invalid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DockerAgentExecutionService.ParseUserFlag("1655:1655"), Is.EqualTo((1655, 1655)));
            Assert.That(DockerAgentExecutionService.ParseUserFlag("0:0"), Is.EqualTo((0, 0)));
            Assert.That(DockerAgentExecutionService.ParseUserFlag(null), Is.Null);
            Assert.That(DockerAgentExecutionService.ParseUserFlag(""), Is.Null);
            Assert.That(DockerAgentExecutionService.ParseUserFlag("homespun"), Is.Null);
            Assert.That(DockerAgentExecutionService.ParseUserFlag("1655"), Is.Null);
        });
    }

    [Test]
    public void RedactSecretsInDockerArgs_masks_known_secret_env_vars()
    {
        var raw =
            "run -d --name foo -e ISSUE_ID=3sqISO " +
            "-e CLAUDE_CODE_OAUTH_TOKEN=\"sk-ant-oat01-abc\" " +
            "-e GITHUB_TOKEN=\"ghp_xyz\" " +
            "-e ANTHROPIC_API_KEY=abc " +
            "-e MY_DB_PASSWORD=\"p@ss\" " +
            "-e WEBHOOK_SECRET=\"shh\" " +
            "--label homespun.managed=true worker:dev";

        var redacted = DockerAgentExecutionService.RedactSecretsInDockerArgs(raw);

        Assert.That(redacted, Does.Not.Contain("sk-ant-oat01-abc"));
        Assert.That(redacted, Does.Not.Contain("ghp_xyz"));
        Assert.That(redacted, Does.Not.Contain("p@ss"));
        Assert.That(redacted, Does.Not.Contain("shh"));
        Assert.That(redacted, Does.Contain("CLAUDE_CODE_OAUTH_TOKEN=\"[REDACTED]\""));
        Assert.That(redacted, Does.Contain("GITHUB_TOKEN=\"[REDACTED]\""));
        Assert.That(redacted, Does.Contain("ANTHROPIC_API_KEY=\"[REDACTED]\""));
        Assert.That(redacted, Does.Contain("MY_DB_PASSWORD=\"[REDACTED]\""));
        Assert.That(redacted, Does.Contain("WEBHOOK_SECRET=\"[REDACTED]\""));
    }

    [Test]
    public void RedactSecretsInDockerArgs_leaves_non_secret_env_vars_intact()
    {
        var raw =
            "run -d -e WORKING_DIRECTORY=/workdir -e DEBUG_LOGGING=true " +
            "-e OTLP_PROXY_URL=http://server:8080/api/otlp/v1 " +
            "-e ISSUE_ID=abc worker:dev";

        var redacted = DockerAgentExecutionService.RedactSecretsInDockerArgs(raw);

        Assert.That(redacted, Is.EqualTo(raw));
    }

    // -------------------------------------------------------------------
    // Task 8: PerSessionEventStream rewire
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

    [Test]
    public async Task SendMessageAsync_posts_json_then_subscribes_to_per_session_stream()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith("/message", new { ok = true });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        var perSession = new Mock<IPerSessionEventStream>();
        perSession
            .Setup(p => p.SubscribeTurnAsync("outer-s1", It.IsAny<CancellationToken>()))
            .Returns(ResultOnly("outer-s1"));

        var svc = BuildWithHttp(httpClient, perSession.Object);
        svc.RegisterSessionForTesting(
            homespunSessionId: "outer-s1",
            workerSessionId: "worker-s1",
            workerUrl: "http://fake",
            projectId: "p1");

        var request = new AgentMessageRequest(
            SessionId: "outer-s1",
            Message: "hello",
            Mode: SessionMode.Build,
            Model: "sonnet");

        var yielded = new List<SdkMessage>();
        await foreach (var msg in svc.SendMessageAsync(request, CancellationToken.None))
        {
            yielded.Add(msg);
        }

        Assert.Multiple(() =>
        {
            Assert.That(yielded, Has.Count.EqualTo(1), "expected exactly the single SdkResultMessage");
            Assert.That(yielded[0], Is.InstanceOf<SdkResultMessage>());
            Assert.That(((SdkResultMessage)yielded[0]).SessionId, Is.EqualTo("outer-s1"),
                "RemapSessionId should have rewritten the result's session id");
        });

        var messagePost = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.Contains("/api/sessions/worker-s1/message"));
        Assert.That(messagePost, Is.Not.Null, "expected a POST to /api/sessions/{workerSessionId}/message");
        // Body is JSON; verify it round-trips and contains the message text.
        var body = messagePost!.Body;
        Assert.That(body, Is.Not.Null.And.Contains("\"message\""));
        Assert.That(body, Does.Contain("hello"));

        perSession.Verify(
            p => p.SubscribeTurnAsync("outer-s1", It.IsAny<CancellationToken>()),
            Times.Once,
            "SendMessageAsync must subscribe to the per-session stream exactly once for the turn");
    }

    [Test]
    public async Task StopSessionAsync_stops_the_per_session_event_stream()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWithStatus("/api/sessions/worker-s1", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        var perSession = new Mock<IPerSessionEventStream>();
        perSession.Setup(p => p.StopAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var svc = BuildWithHttp(httpClient, perSession.Object);
        svc.RegisterSessionForTesting(
            homespunSessionId: "outer-s1",
            workerSessionId: "worker-s1",
            workerUrl: "http://fake",
            projectId: "p1");

        await svc.StopSessionAsync("outer-s1", forceStopContainer: false, CancellationToken.None);

        perSession.Verify(
            p => p.StopAsync("outer-s1"),
            Times.Once,
            "StopSessionAsync must halt the per-session reader before teardown");
    }

    [Test]
    public async Task StartSessionAsync_starts_per_session_event_stream_and_drains_turn()
    {
        // This test exercises only the HTTP + per-session-stream wiring, bypassing
        // container startup by pre-registering the session through the test seam
        // and invoking the internal runner directly.
        var handler = new MockHttpMessageHandler();
        handler.RespondWith("/api/sessions", new { sessionId = "worker-s1", conversationId = (string?)null });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        var perSession = new Mock<IPerSessionEventStream>();
        perSession
            .Setup(p => p.StartAsync(
                "outer-s1", "http://fake", "worker-s1", "p1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        perSession
            .Setup(p => p.SubscribeTurnAsync("outer-s1", It.IsAny<CancellationToken>()))
            .Returns(ResultOnly("outer-s1"));

        var svc = BuildWithHttp(httpClient, perSession.Object);
        svc.RegisterSessionForTesting(
            homespunSessionId: "outer-s1",
            workerSessionId: null,
            workerUrl: "http://fake",
            projectId: "p1");

        var request = new AgentStartRequest(
            WorkingDirectory: "/tmp/clone",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "go",
            IssueId: "i1",
            ProjectId: "p1",
            HomespunSessionId: "outer-s1");

        var yielded = await svc.RunStartSessionWorkerLoopForTestingAsync(
            "outer-s1", "http://fake", request, CancellationToken.None);

        perSession.Verify(
            p => p.StartAsync("outer-s1", "http://fake", "worker-s1", "p1", It.IsAny<CancellationToken>()),
            Times.Once,
            "StartSessionAsync must start the per-session reader with the parsed worker session id");

        perSession.Verify(
            p => p.SubscribeTurnAsync("outer-s1", It.IsAny<CancellationToken>()),
            Times.Once,
            "StartSessionAsync must drain the first turn through the per-session subscription");

        var startPost = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.EndsWith("/api/sessions"));
        Assert.That(startPost, Is.Not.Null, "expected a POST to /api/sessions");

        Assert.That(yielded, Has.Some.InstanceOf<SdkResultMessage>(),
            "the turn drain should surface the result message");
    }
}
