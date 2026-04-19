using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.AppHost.Tests;

/// <summary>
/// Invariants for aspire-dev-logging-local-worker: no AppHost resource may
/// reference a GHCR image string for the worker; every profile that needs a
/// worker must build it locally from src/Homespun.Worker/Dockerfile; Docker
/// agent-mode profiles must inject the locally-built image tag into the
/// server environment so DooD sibling spawns skip GHCR.
/// </summary>
[TestFixture]
public class WorkerImageAppHostTests
{
    private const string LocalWorkerImageTag = "worker:dev";

    private static void SetEnv(string? agentMode, string? hostingMode)
    {
        Environment.SetEnvironmentVariable("HOMESPUN_AGENT_MODE", agentMode);
        Environment.SetEnvironmentVariable("HOMESPUN_DEV_HOSTING_MODE", hostingMode);
    }

    private static async Task<DistributedApplicationModel> BuildModelAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();
        var app = await appHost.BuildAsync();
        return app.Services.GetRequiredService<DistributedApplicationModel>();
    }

    private static IReadOnlyList<object> SnapshotAnnotations(IResource resource) =>
        AnnotationSnapshot.Of(resource);

    [Test]
    public async Task AppHost_no_worker_resource_references_ghcr_image()
    {
        SetEnv(agentMode: null, hostingMode: null);
        try
        {
            var model = await BuildModelAsync();
            var imageAnnotations = model.Resources
                .SelectMany(r => SnapshotAnnotations(r).Select(a => new { r.Name, Annotation = a }))
                .Where(x => x.Annotation is ContainerImageAnnotation)
                .ToList();

            foreach (var entry in imageAnnotations)
            {
                var img = (ContainerImageAnnotation)entry.Annotation;
                var full = $"{img.Registry}/{img.Image}:{img.Tag}".ToLowerInvariant();
                Assert.That(full, Does.Not.Contain("ghcr.io"),
                    $"Resource '{entry.Name}' references GHCR image: {full}");
            }
        }
        finally { SetEnv(null, null); }
    }

    [Test]
    public async Task DevLive_server_resource_receives_local_worker_image_env()
    {
        SetEnv(agentMode: "Docker", hostingMode: "host");
        try
        {
            var model = await BuildModelAsync();
            var server = model.Resources.Single(r => r.Name == "server");
            var envValues = await ResolveEnvironmentAsync(server);

            Assert.That(envValues, Does.ContainKey("AgentExecution__Docker__WorkerImage"));
            var workerImage = envValues["AgentExecution__Docker__WorkerImage"];
            Assert.That(workerImage, Is.Not.Null.And.Not.Empty);
            Assert.That(workerImage, Does.Not.StartWith("ghcr.io/"));
            Assert.That(workerImage, Is.EqualTo(LocalWorkerImageTag));
        }
        finally { SetEnv(null, null); }
    }

    [Test]
    public async Task DevWindows_worker_resource_is_built_from_dockerfile()
    {
        SetEnv(agentMode: "SingleContainer", hostingMode: "host");
        try
        {
            var model = await BuildModelAsync();
            var worker = model.Resources.Single(r => r.Name == "worker");

            var annotations = SnapshotAnnotations(worker);
            var hasDockerfileBuild = annotations.Any(a => a.GetType().Name == "DockerfileBuildAnnotation");
            Assert.That(hasDockerfileBuild, Is.True,
                "Worker resource should carry a DockerfileBuildAnnotation (built via AddDockerfile).");

            var imageAnnotations = annotations.OfType<ContainerImageAnnotation>().ToList();
            foreach (var img in imageAnnotations)
            {
                var full = $"{img.Registry}/{img.Image}:{img.Tag}".ToLowerInvariant();
                Assert.That(full, Does.Not.Contain("ghcr.io"),
                    $"Worker ContainerImageAnnotation unexpectedly references GHCR: {full}");
            }
        }
        finally { SetEnv(null, null); }
    }

    [Test]
    public async Task DevContainer_non_windows_injects_local_worker_image_env()
    {
        SetEnv(agentMode: "Docker", hostingMode: "container");
        try
        {
            var model = await BuildModelAsync();
            var server = model.Resources.Single(r => r.Name == "server");
            var envValues = await ResolveEnvironmentAsync(server);
            Assert.That(envValues, Does.ContainKey("AgentExecution__Docker__WorkerImage"));
            Assert.That(envValues["AgentExecution__Docker__WorkerImage"], Is.EqualTo(LocalWorkerImageTag));

            var worker = model.Resources.Single(r => r.Name == "worker");
            Assert.That(
                SnapshotAnnotations(worker).Any(a => a.GetType().Name == "DockerfileBuildAnnotation"),
                Is.True);
        }
        finally { SetEnv(null, null); }
    }

    private static async Task<Dictionary<string, string>> ResolveEnvironmentAsync(IResource resource)
    {
        // `GetEnvironmentVariableValuesAsync` is the Aspire-provided helper that
        // walks every EnvironmentCallbackAnnotation on the resource and resolves
        // each value (including referenced endpoints/parameters) to a concrete
        // string, which is what the AppHost will set on the launched process.
        var envResource = (IResourceWithEnvironment)resource;
        var values = await envResource.GetEnvironmentVariableValuesAsync();
        return new Dictionary<string, string>(values, StringComparer.Ordinal);
    }
}
