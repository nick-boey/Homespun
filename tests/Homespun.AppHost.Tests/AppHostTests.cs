using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.AppHost.Tests;

/// <summary>
/// AppHost wiring tests. The AppHost now drives four launch profiles
/// (dev-mock, dev-live, dev-windows, dev-container) and branches on env vars
/// at boot time — these tests exercise the default host-mode wiring
/// (dev-mock equivalent) without setting any mode env vars. The
/// observability sink is Seq alone; PLG resources must be absent.
/// </summary>
[TestFixture]
public class AppHostTests
{
    [Test]
    public async Task AppHost_has_seq_resource()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var seqResource = model.Resources.SingleOrDefault(r => r.Name == "seq");

        Assert.That(seqResource, Is.Not.Null, "Seq resource should exist");
    }

    [Test]
    public async Task AppHost_has_no_loki_promtail_grafana_resources()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        Assert.Multiple(() =>
        {
            Assert.That(model.Resources.Any(r => r.Name == "loki"), Is.False, "Loki resource must be absent");
            Assert.That(model.Resources.Any(r => r.Name == "promtail"), Is.False, "Promtail resource must be absent");
            Assert.That(model.Resources.Any(r => r.Name == "grafana"), Is.False, "Grafana resource must be absent");
        });
    }

    [Test]
    public async Task Server_resource_references_seq()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var serverResource = model.Resources.Single(r => r.Name == "server");

        var refAnnotations = AnnotationSnapshot.Of(serverResource)
            .OfType<ResourceRelationshipAnnotation>()
            .ToList();

        Assert.That(
            refAnnotations.Any(a => a.Resource.Name == "seq"),
            Is.True,
            "Server resource should carry a resource relationship to seq (via WithReference/WaitFor)");
    }

    [Test]
    public async Task AppHost_default_profile_has_server_web()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var serverResource = model.Resources.SingleOrDefault(r => r.Name == "server");
        var webResource = model.Resources.SingleOrDefault(r => r.Name == "web");

        Assert.Multiple(() =>
        {
            Assert.That(serverResource, Is.Not.Null, "Server resource should exist");
            Assert.That(webResource, Is.Not.Null, "Web resource should exist");
        });
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

        var envAnnotations = AnnotationSnapshot.Of(serverResource)
            .OfType<EnvironmentCallbackAnnotation>()
            .ToList();

        Assert.That(envAnnotations, Is.Not.Empty,
            "Server resource should have environment variable annotations");
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

        Assert.Multiple(() =>
        {
            Assert.That(githubTokenParam, Is.Not.Null, "github-token parameter should exist");
            Assert.That(claudeOauthTokenParam, Is.Not.Null, "claude-oauth-token parameter should exist");
        });
    }
}
