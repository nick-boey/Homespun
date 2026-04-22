using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Homespun.Features.Notifications;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.Gitgraph;

/// <summary>
/// Delta 3: a patchable-field mutation emits <c>IssueFieldsPatched</c> when
/// <c>TaskGraphSnapshot:PatchPush:Enabled=true</c> (default) and falls back
/// to <c>IssuesChanged</c> when the kill switch is flipped off. Topology
/// mutations always emit <c>IssuesChanged</c> regardless of the flag.
/// </summary>
[TestFixture]
public class PatchPushEventTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Test]
    public async Task TitleOnlyMutation_EmitsIssueFieldsPatched_WhenFlagEnabled()
    {
        using var factory = new RecordingHubFactory(patchPushEnabled: true);
        using var client = factory.CreateClient();

        var projectId = await CreateProject(client);
        var issue = await CreateIssue(client, projectId, "before");
        factory.Recorder.Clear();

        var updateRequest = new UpdateIssueRequest { ProjectId = projectId, Title = "after" };
        var updateResponse = await client.PutAsJsonAsync($"/api/issues/{issue.Id}", updateRequest, JsonOptions);
        updateResponse.EnsureSuccessStatusCode();

        var methods = factory.Recorder.Methods();
        Assert.Multiple(() =>
        {
            Assert.That(methods, Does.Contain("IssueFieldsPatched"),
                "title-only edit with patch-push enabled must emit IssueFieldsPatched");
            Assert.That(methods, Does.Not.Contain("IssuesChanged"),
                "IssuesChanged is the fallback; it must not fire when IssueFieldsPatched did");
        });
    }

    [Test]
    public async Task TitleOnlyMutation_FallsBackToIssuesChanged_WhenFlagDisabled()
    {
        using var factory = new RecordingHubFactory(patchPushEnabled: false);
        using var client = factory.CreateClient();

        var projectId = await CreateProject(client);
        var issue = await CreateIssue(client, projectId, "before");
        factory.Recorder.Clear();

        var updateRequest = new UpdateIssueRequest { ProjectId = projectId, Title = "after" };
        var updateResponse = await client.PutAsJsonAsync($"/api/issues/{issue.Id}", updateRequest, JsonOptions);
        updateResponse.EnsureSuccessStatusCode();

        var methods = factory.Recorder.Methods();
        Assert.Multiple(() =>
        {
            Assert.That(methods, Does.Contain("IssuesChanged"),
                "kill switch off — patch path must fall back to IssuesChanged so clients refetch");
            Assert.That(methods, Does.Not.Contain("IssueFieldsPatched"),
                "IssueFieldsPatched must not fire when the flag is disabled");
        });
    }

    [Test]
    public async Task StatusMutation_AlwaysEmitsIssuesChanged_RegardlessOfFlag()
    {
        using var factory = new RecordingHubFactory(patchPushEnabled: true);
        using var client = factory.CreateClient();

        var projectId = await CreateProject(client);
        var issue = await CreateIssue(client, projectId, "status-target");
        factory.Recorder.Clear();

        var updateRequest = new UpdateIssueRequest { ProjectId = projectId, Status = IssueStatus.Progress };
        var updateResponse = await client.PutAsJsonAsync($"/api/issues/{issue.Id}", updateRequest, JsonOptions);
        updateResponse.EnsureSuccessStatusCode();

        var methods = factory.Recorder.Methods();
        Assert.Multiple(() =>
        {
            Assert.That(methods, Does.Contain("IssuesChanged"),
                "topology-class mutation (Status) must emit IssuesChanged — patch-push flag does not apply");
            Assert.That(methods, Does.Not.Contain("IssueFieldsPatched"),
                "topology-class mutation must not emit IssueFieldsPatched");
        });
    }

    private static async Task<string> CreateProject(HttpClient client)
    {
        var request = new { Name = "PatchPush-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var response = await client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!.Id;
    }

    private static async Task<IssueResponse> CreateIssue(HttpClient client, string projectId, string title)
    {
        var request = new CreateIssueRequest
        {
            ProjectId = projectId,
            Title = title,
            Type = IssueType.Task,
        };
        var response = await client.PostAsJsonAsync("/api/issues", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;
    }

    /// <summary>
    /// Records every <c>SendCoreAsync</c> invocation on the NotificationHub so
    /// tests can assert which SignalR event the broadcast helper dispatched.
    /// </summary>
    private sealed class HubInvocationRecorder
    {
        private readonly ConcurrentQueue<string> _methods = new();

        public void Record(string method) => _methods.Enqueue(method);

        public void Clear() { while (_methods.TryDequeue(out _)) { } }

        public string[] Methods() => _methods.ToArray();
    }

    private sealed class RecordingHubFactory(bool patchPushEnabled) : HomespunWebApplicationFactory
    {
        public HubInvocationRecorder Recorder { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting(
                "TaskGraphSnapshot:PatchPush:Enabled",
                patchPushEnabled ? "true" : "false");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(Recorder);
                services.AddSingleton<IHubContext<NotificationHub>>(sp =>
                    new RecordingHubContext(sp.GetRequiredService<HubInvocationRecorder>()));
            });
        }
    }

    private sealed class RecordingHubContext(HubInvocationRecorder recorder) : IHubContext<NotificationHub>
    {
        public IHubClients Clients { get; } = new RecordingHubClients(recorder);
        public IGroupManager Groups { get; } = new NoopGroupManager();
    }

    private sealed class RecordingHubClients(HubInvocationRecorder recorder) : IHubClients
    {
        private readonly RecordingClientProxy _proxy = new(recorder);

        public IClientProxy All => _proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Client(string connectionId) => _proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
        public IClientProxy Group(string groupName) => _proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
        public IClientProxy User(string userId) => _proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
    }

    private sealed class RecordingClientProxy(HubInvocationRecorder recorder) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            recorder.Record(method);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
