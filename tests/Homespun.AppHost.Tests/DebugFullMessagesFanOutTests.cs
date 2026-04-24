using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.AppHost.Tests;

/// <summary>
/// Exercises the HOMESPUN_DEBUG_FULL_MESSAGES umbrella fan-out. When the
/// AppHost process has the flag set, every tier it provisions (server,
/// worker, web) should receive the flag (and its per-tier implications)
/// in its environment — unless the caller set an explicit value first.
/// </summary>
[TestFixture]
public class DebugFullMessagesFanOutTests
{
    private static void SetEnv(
        string? debugFullMessages = null,
        string? agentMode = null,
        string? hostingMode = null,
        string? contentPreviewChars = null,
        string? sessionEventContentChars = null,
        string? debugAgentSdk = null)
    {
        Environment.SetEnvironmentVariable("HOMESPUN_DEBUG_FULL_MESSAGES", debugFullMessages);
        Environment.SetEnvironmentVariable("HOMESPUN_AGENT_MODE", agentMode);
        Environment.SetEnvironmentVariable("HOMESPUN_DEV_HOSTING_MODE", hostingMode);
        Environment.SetEnvironmentVariable("CONTENT_PREVIEW_CHARS", contentPreviewChars);
        Environment.SetEnvironmentVariable("SessionEventContent__ContentPreviewChars", sessionEventContentChars);
        Environment.SetEnvironmentVariable("DEBUG_AGENT_SDK", debugAgentSdk);
    }

    private static void ClearEnv()
    {
        SetEnv();
        Environment.SetEnvironmentVariable("HOMESPUN_PROFILE_KIND", null);
    }

    private static async Task<DistributedApplicationModel> BuildModelAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Homespun_AppHost>();
        var app = await appHost.BuildAsync();
        return app.Services.GetRequiredService<DistributedApplicationModel>();
    }

    private static async Task<Dictionary<string, string>> ResolveEnvAsync(IResource resource)
    {
        var envResource = (IResourceWithEnvironment)resource;
        var values = await envResource.GetEnvironmentVariableValuesAsync();
        return new Dictionary<string, string>(values, StringComparer.Ordinal);
    }

    [Test]
    public async Task DebugFullMessagesUnset_DefaultsToOn()
    {
        SetEnv(debugFullMessages: null);
        try
        {
            var model = await BuildModelAsync();
            var server = model.Resources.Single(r => r.Name == "server");
            var env = await ResolveEnvAsync(server);

            Assert.That(env, Does.ContainKey("HOMESPUN_DEBUG_FULL_MESSAGES"),
                "umbrella flag defaults to on in every launch profile");
            Assert.That(env["HOMESPUN_DEBUG_FULL_MESSAGES"], Is.EqualTo("true"));
            Assert.That(env, Does.ContainKey("SessionEventContent__ContentPreviewChars"),
                "derived -1 sentinel must be set when umbrella flag is on");
            Assert.That(env["SessionEventContent__ContentPreviewChars"], Is.EqualTo("-1"));
        }
        finally { ClearEnv(); }
    }

    [Test]
    public async Task DebugFullMessagesFalse_NoFanOutEnvVarsPresent()
    {
        SetEnv(debugFullMessages: "false");
        try
        {
            var model = await BuildModelAsync();
            var server = model.Resources.Single(r => r.Name == "server");
            var env = await ResolveEnvAsync(server);

            Assert.That(env.ContainsKey("HOMESPUN_DEBUG_FULL_MESSAGES"), Is.False,
                "umbrella flag must not be propagated when explicitly opted out");
            Assert.That(env.ContainsKey("SessionEventContent__ContentPreviewChars"), Is.False,
                "derived -1 sentinel must not be set when umbrella flag is off");
        }
        finally { ClearEnv(); }
    }

    [Test]
    public async Task DebugFullMessagesTrue_ServerReceivesFlagAndSentinel()
    {
        SetEnv(debugFullMessages: "true");
        try
        {
            var model = await BuildModelAsync();
            var server = model.Resources.Single(r => r.Name == "server");
            var env = await ResolveEnvAsync(server);

            Assert.That(env, Does.ContainKey("HOMESPUN_DEBUG_FULL_MESSAGES"));
            Assert.That(env["HOMESPUN_DEBUG_FULL_MESSAGES"], Is.EqualTo("true"));
            Assert.That(env, Does.ContainKey("SessionEventContent__ContentPreviewChars"));
            Assert.That(env["SessionEventContent__ContentPreviewChars"], Is.EqualTo("-1"));
        }
        finally { ClearEnv(); }
    }

    [Test]
    public async Task DebugFullMessagesTrue_ExplicitSessionEventContentCharsIsNotOverridden()
    {
        SetEnv(debugFullMessages: "true", sessionEventContentChars: "240");
        try
        {
            var model = await BuildModelAsync();
            var server = model.Resources.Single(r => r.Name == "server");
            var env = await ResolveEnvAsync(server);

            Assert.That(env, Does.ContainKey("HOMESPUN_DEBUG_FULL_MESSAGES"));
            Assert.That(env.ContainsKey("SessionEventContent__ContentPreviewChars"), Is.False,
                "fan-out must not inject the sentinel when caller set an explicit value");
        }
        finally { ClearEnv(); }
    }

    [Test]
    public async Task DebugFullMessagesTrue_SingleContainerWorkerReceivesAllFlags()
    {
        SetEnv(debugFullMessages: "true", agentMode: "SingleContainer");
        try
        {
            var model = await BuildModelAsync();
            var worker = model.Resources.Single(r => r.Name == "worker");
            var env = await ResolveEnvAsync(worker);

            Assert.That(env, Does.ContainKey("HOMESPUN_DEBUG_FULL_MESSAGES"));
            Assert.That(env["HOMESPUN_DEBUG_FULL_MESSAGES"], Is.EqualTo("true"));
            Assert.That(env, Does.ContainKey("DEBUG_AGENT_SDK"));
            Assert.That(env["DEBUG_AGENT_SDK"], Is.EqualTo("true"));
            Assert.That(env, Does.ContainKey("CONTENT_PREVIEW_CHARS"));
            Assert.That(env["CONTENT_PREVIEW_CHARS"], Is.EqualTo("-1"));
        }
        finally { ClearEnv(); }
    }

    [Test]
    public async Task DebugFullMessagesTrue_HostMode_ViteAppReceivesViteFlag()
    {
        SetEnv(debugFullMessages: "true");
        try
        {
            var model = await BuildModelAsync();
            var web = model.Resources.Single(r => r.Name == "web");
            var env = await ResolveEnvAsync(web);

            Assert.That(env, Does.ContainKey("VITE_HOMESPUN_DEBUG_FULL_MESSAGES"));
            Assert.That(env["VITE_HOMESPUN_DEBUG_FULL_MESSAGES"], Is.EqualTo("true"));
        }
        finally { ClearEnv(); }
    }

    [Test]
    public async Task ProdProfile_ServerRunsProductionWithoutMockEnvAndBindsDataFolder()
    {
        SetProdEnv();
        try
        {
            var model = await BuildModelAsync();
            var server = model.Resources.Single(r => r.Name == "server");
            var env = await ResolveEnvAsync(server);

            Assert.Multiple(() =>
            {
                Assert.That(env, Does.ContainKey("ASPNETCORE_ENVIRONMENT"));
                Assert.That(env["ASPNETCORE_ENVIRONMENT"], Is.EqualTo("Production"));
                Assert.That(env.ContainsKey("HOMESPUN_MOCK_MODE"), Is.False,
                    "prod profile must not inject HOMESPUN_MOCK_MODE");
                Assert.That(env.Keys.Any(k => k.StartsWith("MockMode__", StringComparison.Ordinal)), Is.False,
                    "prod profile must not inject any MockMode__* env vars");
                Assert.That(env, Does.ContainKey("HOMESPUN_DATA_PATH"));
                Assert.That(env["HOMESPUN_DATA_PATH"], Is.EqualTo("/data/.homespun/homespun-data.json"));
            });

            var bindMounts = AnnotationSnapshot.Of(server)
                .OfType<ContainerMountAnnotation>()
                .Where(m => m.Target == "/data")
                .ToList();
            Assert.That(bindMounts, Has.Count.EqualTo(1),
                "prod profile must bind-mount the data folder to /data");
            var expectedHostPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".homespun-container",
                "data");
            Assert.That(bindMounts[0].Source, Is.EqualTo(expectedHostPath));
        }
        finally { ClearEnv(); }
    }

    private static void SetProdEnv()
    {
        Environment.SetEnvironmentVariable("HOMESPUN_DEV_HOSTING_MODE", "container");
        Environment.SetEnvironmentVariable("HOMESPUN_PROFILE_KIND", "prod");
    }
}
