using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Live integration tests for the worker container.
/// These tests require Docker running, homespun-worker:local image built,
/// and CLAUDE_CODE_OAUTH_TOKEN set in the environment.
/// </summary>
[TestFixture]
[Category("Live")]
public class DockerAgentExecutionServiceLiveTests
{
    private string _containerName = null!;
    private string _workerUrl = null!;
    private string _tempDir = null!;
    private string _claudeDir = null!;
    private int _port;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    [SetUp]
    public async Task SetUp()
    {
        var token = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Assert.Ignore("CLAUDE_CODE_OAUTH_TOKEN not set — skipping live test");
        }

        _tempDir = Path.Combine(Path.GetTempPath(), $"homespun-live-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _claudeDir = Path.Combine(_tempDir, ".claude");
        Directory.CreateDirectory(_claudeDir);

        _port = FindAvailablePort();
        _containerName = $"homespun-live-test-{Guid.NewGuid().ToString("N")[..8]}";

        var dockerArgs = new StringBuilder();
        dockerArgs.Append("run -d ");
        dockerArgs.Append($"--name {_containerName} ");
        dockerArgs.Append($"-p {_port}:8080 ");
        dockerArgs.Append($"-v {_tempDir}:/workdir ");
        dockerArgs.Append($"-v {_claudeDir}:/home/homespun/.claude ");
        dockerArgs.Append("-e WORKING_DIRECTORY=/workdir ");
        dockerArgs.Append($"-e CLAUDE_CODE_OAUTH_TOKEN={token} ");
        dockerArgs.Append("homespun-worker:local");

        var (exitCode, stdout, stderr) = await RunDockerAsync(dockerArgs.ToString());
        if (exitCode != 0)
            throw new Exception($"Failed to start container: {stderr}");

        _workerUrl = $"http://localhost:{_port}";
        TestContext.WriteLine($"Container: {_containerName}, URL: {_workerUrl}");

        await WaitForHealthyAsync(TimeSpan.FromSeconds(60));
    }

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            var logs = (await RunDockerAsync($"logs {_containerName}")).stdout;
            TestContext.WriteLine($"Container logs:\n{logs}");
        }
        catch { /* best effort */ }

        try { await RunDockerAsync($"stop {_containerName}"); } catch { }
        try { await RunDockerAsync($"rm {_containerName}"); } catch { }

        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        _httpClient.Dispose();
    }

    [Test]
    [CancelAfter(600_000)]
    public async Task FollowUpPrompts_SameSession_BothComplete()
    {
        // First prompt
        var events1 = await SendPromptAndWaitForCompletion(
            "/api/sessions",
            JsonSerializer.Serialize(new { prompt = "Say 'hello, world'", mode = "Build", model = "claude-sonnet-4-20250514" }));

        AssertHasCompletedStatus(events1, "first prompt");

        var agentTexts1 = FindAgentText(events1);
        TestContext.WriteLine($"First prompt response: {string.Join(" ", agentTexts1)}");
        Assert.That(string.Join(" ", agentTexts1), Does.Contain("hello, world").IgnoreCase,
            "Expected assistant to say 'hello, world'");

        // Extract session ID
        var sessionId = ExtractSessionId(events1);
        Assert.That(sessionId, Is.Not.Null.And.Not.Empty, "Session ID should be present");
        TestContext.WriteLine($"Session ID: {sessionId}");

        // Follow-up prompt
        var events2 = await SendPromptAndWaitForCompletion(
            $"/api/sessions/{sessionId}/message",
            JsonSerializer.Serialize(new { message = "Say 'goodbye, world'" }));

        AssertHasCompletedStatus(events2, "follow-up prompt");

        var agentTexts2 = FindAgentText(events2);
        TestContext.WriteLine($"Follow-up response: {string.Join(" ", agentTexts2)}");
        Assert.That(string.Join(" ", agentTexts2), Does.Contain("goodbye, world").IgnoreCase,
            "Expected assistant to say 'goodbye, world'");
    }

    #region Helpers

    private async Task<List<SseEvent>> SendPromptAndWaitForCompletion(string path, string jsonBody)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_workerUrl}{path}");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var events = new List<SseEvent>();
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var buffer = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrEmpty(line))
            {
                var evt = ParseSseBlock(buffer.ToString());
                if (evt != null)
                {
                    events.Add(evt);
                    TestContext.WriteLine($"  Event: {evt.EventType} {evt.Data.ToString()![..Math.Min(200, evt.Data.ToString()!.Length)]}");

                    if (evt.EventType == "status-update" &&
                        evt.Data.TryGetProperty("final", out var finalProp) && finalProp.GetBoolean())
                    {
                        break;
                    }
                }
                buffer.Clear();
            }
            else
            {
                buffer.AppendLine(line);
            }
        }

        return events;
    }

    private static SseEvent? ParseSseBlock(string block)
    {
        if (string.IsNullOrWhiteSpace(block)) return null;

        string? eventType = null;
        string? data = null;

        foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("event: "))
                eventType = line["event: ".Length..];
            else if (line.StartsWith("data: "))
                data = line["data: ".Length..];
        }

        if (eventType == null || data == null) return null;

        try
        {
            return new SseEvent(eventType, JsonDocument.Parse(data).RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractSessionId(List<SseEvent> events)
    {
        var taskEvent = events.FirstOrDefault(e => e.EventType == "task");
        if (taskEvent != null && taskEvent.Data.TryGetProperty("id", out var id))
            return id.GetString();
        return null;
    }

    private static List<string> FindAgentText(List<SseEvent> events)
    {
        var texts = new List<string>();
        foreach (var evt in events.Where(e => e.EventType == "message"))
        {
            if (!evt.Data.TryGetProperty("role", out var role) || role.GetString() != "agent")
                continue;
            if (!evt.Data.TryGetProperty("parts", out var parts))
                continue;
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("kind", out var kind) && kind.GetString() == "text" &&
                    part.TryGetProperty("text", out var text))
                {
                    var t = text.GetString();
                    if (!string.IsNullOrEmpty(t))
                        texts.Add(t);
                }
            }
        }
        return texts;
    }

    private static void AssertHasCompletedStatus(List<SseEvent> events, string context)
    {
        var completed = events.Any(e =>
            e.EventType == "status-update" &&
            e.Data.TryGetProperty("final", out var f) && f.GetBoolean());
        Assert.That(completed, Is.True, $"Expected completed status-update for {context}");
    }

    private async Task WaitForHealthyAsync(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_workerUrl}/api/health");
                if (response.IsSuccessStatusCode) return;
            }
            catch { /* not ready yet */ }
            await Task.Delay(1000);
        }
        throw new Exception($"Container {_containerName} did not become healthy within {timeout}");
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunDockerAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new Exception($"Failed to start docker process: {args}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }

    private record SseEvent(string EventType, JsonElement Data);

    #endregion
}
