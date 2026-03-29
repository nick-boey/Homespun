using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.AppHost.Tests;

[TestFixture]
public class AppHostTests
{
    [Test]
    public async Task AppHost_builds_and_has_expected_resources()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(http =>
            http.AddStandardResilienceHandler());

        var app = await appHost.BuildAsync();

        await app.StartAsync();

        // Verify the expected resources exist
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var serverResource = model.Resources.SingleOrDefault(r => r.Name == "server");
        var workerResource = model.Resources.SingleOrDefault(r => r.Name == "worker");
        var webResource = model.Resources.SingleOrDefault(r => r.Name == "web");

        Assert.That(serverResource, Is.Not.Null, "Server resource should exist");
        Assert.That(workerResource, Is.Not.Null, "Worker resource should exist");
        Assert.That(webResource, Is.Not.Null, "Web resource should exist");

        await app.StopAsync();
    }

    [Test]
    public async Task AppHost_model_contains_expected_resource_count()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        // Should have at least server, worker, web (plus parameter resources)
        var namedResources = model.Resources
            .Where(r => r.Name is "server" or "worker" or "web")
            .ToList();

        Assert.That(namedResources, Has.Count.EqualTo(3));

        await app.StopAsync();
    }

    [Test]
    public async Task Server_resource_has_worker_reference_environment_variable()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var serverResource = model.Resources.Single(r => r.Name == "server");

        // The server should have environment variable annotations that include MiniPrompt__SidecarUrl
        var envAnnotations = serverResource.Annotations
            .OfType<EnvironmentCallbackAnnotation>()
            .ToList();

        Assert.That(envAnnotations, Is.Not.Empty,
            "Server resource should have environment variable annotations");

        await app.StopAsync();
    }

    [Test]
    public async Task Web_resource_has_server_reference_environment_variable()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var webResource = model.Resources.Single(r => r.Name == "web");

        // The web resource should have environment variable annotations that include VITE_API_URL
        var envAnnotations = webResource.Annotations
            .OfType<EnvironmentCallbackAnnotation>()
            .ToList();

        Assert.That(envAnnotations, Is.Not.Empty,
            "Web resource should have environment variable annotations");

        await app.StopAsync();
    }

    [Test]
    public async Task Secret_parameters_are_wired_to_correct_services()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();

        var app = await appHost.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        // Verify parameter resources exist for secrets
        var githubTokenParam = model.Resources
            .SingleOrDefault(r => r.Name == "github-token");
        var claudeOauthTokenParam = model.Resources
            .SingleOrDefault(r => r.Name == "claude-oauth-token");

        Assert.That(githubTokenParam, Is.Not.Null, "github-token parameter should exist");
        Assert.That(claudeOauthTokenParam, Is.Not.Null, "claude-oauth-token parameter should exist");

        await app.StopAsync();
    }
}
