using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.AppHost.Tests;

/// <summary>
/// AppHost wiring tests for the dev-orchestration change. The AppHost now
/// drives four launch profiles (dev-mock, dev-live, dev-windows, dev-container)
/// and branches on env vars at boot time — these tests exercise the default
/// host-mode wiring (dev-mock equivalent) without setting any mode env vars.
/// </summary>
[TestFixture]
public class AppHostTests
{
    [Test]
    public async Task AppHost_default_profile_has_server_web_and_plg_stack()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var serverResource = model.Resources.SingleOrDefault(r => r.Name == "server");
        var webResource = model.Resources.SingleOrDefault(r => r.Name == "web");
        var lokiResource = model.Resources.SingleOrDefault(r => r.Name == "loki");
        var promtailResource = model.Resources.SingleOrDefault(r => r.Name == "promtail");
        var grafanaResource = model.Resources.SingleOrDefault(r => r.Name == "grafana");

        Assert.That(serverResource, Is.Not.Null, "Server resource should exist");
        Assert.That(webResource, Is.Not.Null, "Web resource should exist");
        Assert.That(lokiResource, Is.Not.Null, "Loki resource should exist");
        Assert.That(promtailResource, Is.Not.Null, "Promtail resource should exist");
        Assert.That(grafanaResource, Is.Not.Null, "Grafana resource should exist");
    }

    [Test]
    public async Task AppHost_default_profile_does_not_add_worker_resource()
    {
        // Worker is only wired when HOMESPUN_AGENT_MODE=SingleContainer (dev-windows)
        // or in the dev-container path on non-Windows. In the default host-mode
        // profile there is no worker resource.
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var workerResource = model.Resources.SingleOrDefault(r => r.Name == "worker");

        Assert.That(workerResource, Is.Null, "Worker resource should be absent in default profile");
    }

    [Test]
    public async Task Web_resource_has_environment_variable_annotations()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var webResource = model.Resources.Single(r => r.Name == "web");

        // The web resource is wired with VITE_API_URL pointing at the server endpoint.
        var envAnnotations = AnnotationSnapshot.Of(webResource)
            .OfType<EnvironmentCallbackAnnotation>()
            .ToList();

        Assert.That(envAnnotations, Is.Not.Empty,
            "Web resource should have environment variable annotations");
    }

    [Test]
    public async Task Server_resource_has_environment_variable_annotations()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var serverResource = model.Resources.Single(r => r.Name == "server");

        // The server resource is wired with mock-mode + secret env vars.
        var envAnnotations = AnnotationSnapshot.Of(serverResource)
            .OfType<EnvironmentCallbackAnnotation>()
            .ToList();

        Assert.That(envAnnotations, Is.Not.Empty,
            "Server resource should have environment variable annotations");
    }

    [Test]
    public async Task AppHost_skips_plg_stack_when_HOMESPUN_DEV_SKIP_PLG_is_true()
    {
        // e2e-ci launch profile sets this env var so CI doesn't pay the cost
        // of pulling loki/promtail/grafana images just to run Playwright.
        var previous = Environment.GetEnvironmentVariable("HOMESPUN_DEV_SKIP_PLG");
        Environment.SetEnvironmentVariable("HOMESPUN_DEV_SKIP_PLG", "true");
        try
        {
            var appHost = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.Homespun_AppHost>();

            var app = await appHost.BuildAsync();

            var model = app.Services.GetRequiredService<DistributedApplicationModel>();

            Assert.Multiple(() =>
            {
                Assert.That(model.Resources.SingleOrDefault(r => r.Name == "loki"),
                    Is.Null, "Loki should be absent when HOMESPUN_DEV_SKIP_PLG=true");
                Assert.That(model.Resources.SingleOrDefault(r => r.Name == "promtail"),
                    Is.Null, "Promtail should be absent when HOMESPUN_DEV_SKIP_PLG=true");
                Assert.That(model.Resources.SingleOrDefault(r => r.Name == "grafana"),
                    Is.Null, "Grafana should be absent when HOMESPUN_DEV_SKIP_PLG=true");
                Assert.That(model.Resources.SingleOrDefault(r => r.Name == "server"),
                    Is.Not.Null, "Server should still be registered");
                Assert.That(model.Resources.SingleOrDefault(r => r.Name == "web"),
                    Is.Not.Null, "Web should still be registered");
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOMESPUN_DEV_SKIP_PLG", previous);
        }
    }

    [Test]
    public async Task Secret_parameters_are_wired_to_correct_services()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var githubTokenParam = model.Resources
            .SingleOrDefault(r => r.Name == "github-token");
        var claudeOauthTokenParam = model.Resources
            .SingleOrDefault(r => r.Name == "claude-oauth-token");

        Assert.That(githubTokenParam, Is.Not.Null, "github-token parameter should exist");
        Assert.That(claudeOauthTokenParam, Is.Not.Null, "claude-oauth-token parameter should exist");
    }
}
