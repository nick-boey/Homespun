using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Homespun.Api.Tests.Features;

/// <summary>
/// Startup tests for the <c>AgentExecution:Mode</c> gate introduced by the
/// session-event-telemetry change. Production + SingleContainer must throw at
/// startup; Development + SingleContainer with a valid WorkerUrl must register
/// <see cref="SingleContainerAgentExecutionService"/> as
/// <see cref="IAgentExecutionService"/>.
/// </summary>
[TestFixture]
public class AgentExecutionModeStartupTests
{
    private sealed class ModeFactory : WebApplicationFactory<Program>
    {
        public required string Environment { get; init; }
        public required string Mode { get; init; }
        public string? WorkerUrl { get; init; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environment);
            // Do not enable mock mode so the agent-execution registration branch runs.
            builder.UseSetting("MockMode:Enabled", "false");
            builder.UseSetting("AgentExecution:Mode", Mode);
            if (WorkerUrl is not null)
            {
                builder.UseSetting("AgentExecution:SingleContainer:WorkerUrl", WorkerUrl);
            }
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Disable scope validation so we can resolve IAgentExecutionService
            // without also booting every downstream service graph.
            builder.UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = false;
                o.ValidateOnBuild = false;
            });
            return base.CreateHost(builder);
        }
    }

    [Test]
    public void Production_SingleContainer_ThrowsAtStartup()
    {
        using var factory = new ModeFactory
        {
            Environment = "Production",
            Mode = "SingleContainer",
            WorkerUrl = "http://localhost:8081",
        };

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Test]
    public void SingleContainer_MissingWorkerUrl_ThrowsAtStartup()
    {
        using var factory = new ModeFactory
        {
            Environment = "Development",
            Mode = "SingleContainer",
            WorkerUrl = null,
        };

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Test]
    public void Development_SingleContainer_RegistersShim()
    {
        using var factory = new ModeFactory
        {
            Environment = "Development",
            Mode = "SingleContainer",
            WorkerUrl = "http://localhost:8081",
        };

        // Trigger app build by resolving services.
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAgentExecutionService>();
        Assert.That(service, Is.InstanceOf<SingleContainerAgentExecutionService>());
    }
}
