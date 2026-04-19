using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Observability;
using Homespun.Features.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for <see cref="DockerAgentExecutionService"/> covering the
/// OTel environment injection (task 7.2) and the SIGTERM-friendly stop
/// flag (task 7.3) introduced by the <c>worker-otel</c> change.
/// </summary>
[TestFixture]
public class DockerAgentExecutionServiceTests
{
    private static DockerAgentExecutionService Build(
        DockerAgentExecutionOptions? options = null)
    {
        return new DockerAgentExecutionService(
            Options.Create(options ?? new DockerAgentExecutionOptions
            {
                ServerOtlpProxyUrl = "http://host.docker.internal:5101/api/otlp/v1",
            }),
            NullLogger<DockerAgentExecutionService>.Instance,
            new Mock<ISecretsService>().Object,
            new Mock<Homespun.Features.ClaudeCode.Services.ISessionEventIngestor>().Object,
            Options.Create(new SessionEventLogOptions()));
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
}
