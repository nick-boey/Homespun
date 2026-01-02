using TreeAgent.Web.Data.Entities;
using TreeAgent.Web.Services;
using TreeAgent.Web.Tests.Integration.Fixtures;

namespace TreeAgent.Web.Tests.Integration;

/// <summary>
/// Integration tests for ClaudeCodeProcess that test against a real Claude Code installation.
/// These tests require Claude Code to be installed and available on the PATH.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "ClaudeCode")]
public class ClaudeCodeProcessIntegrationTests : IDisposable
{
    private readonly ClaudeCodeTestFixture _fixture;
    private readonly List<IClaudeCodeProcess> _processesToCleanup = [];

    public ClaudeCodeProcessIntegrationTests()
    {
        _fixture = new ClaudeCodeTestFixture();
    }

    [SkippableFact]
    public async Task ClaudeCodeProcess_Start_StartsSuccessfully()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-start");
        var statusChanges = new List<AgentStatus>();
        process.OnStatusChanged += status => statusChanges.Add(status);

        // Act
        await process.StartAsync();

        // Allow time for process to start
        await Task.Delay(2000);

        // Assert
        Assert.True(process.IsRunning);
        Assert.Contains(AgentStatus.Running, statusChanges);
    }

    [SkippableFact]
    public async Task ClaudeCodeProcess_SendMessage_ReceivesResponse()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-message");
        var messagesReceived = new List<string>();
        var messageReceivedEvent = new TaskCompletionSource<bool>();

        process.OnMessageReceived += message =>
        {
            messagesReceived.Add(message);
            if (messagesReceived.Count >= 1)
            {
                messageReceivedEvent.TrySetResult(true);
            }
        };

        await process.StartAsync();
        await Task.Delay(2000); // Wait for startup

        // Act
        await process.SendMessageAsync("Reply with only the word 'hello'");

        // Wait for response with timeout
        var completed = await Task.WhenAny(
            messageReceivedEvent.Task,
            Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.NotEmpty(messagesReceived);
    }

    [SkippableFact]
    public async Task ClaudeCodeProcess_Stop_StopsGracefully()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-stop");
        var statusChanges = new List<AgentStatus>();
        process.OnStatusChanged += status => statusChanges.Add(status);

        await process.StartAsync();
        await Task.Delay(2000); // Wait for startup
        Assert.True(process.IsRunning);

        // Act
        await process.StopAsync();

        // Assert
        Assert.False(process.IsRunning);
        Assert.Equal(AgentStatus.Stopped, process.Status);
    }

    [SkippableFact]
    public async Task ClaudeCodeProcess_WithSystemPrompt_UsesSystemPrompt()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var systemPrompt = "You are a helpful assistant that always responds with 'PONG' when asked 'PING'.";
        var process = CreateProcess("test-system-prompt", systemPrompt);
        var messagesReceived = new List<string>();
        var messageReceivedEvent = new TaskCompletionSource<bool>();

        process.OnMessageReceived += message =>
        {
            messagesReceived.Add(message);
            messageReceivedEvent.TrySetResult(true);
        };

        await process.StartAsync();
        await Task.Delay(2000);

        // Act
        await process.SendMessageAsync("PING");

        await Task.WhenAny(
            messageReceivedEvent.Task,
            Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.NotEmpty(messagesReceived);
        // The response should contain PONG if the system prompt was applied
        var allMessages = string.Join(" ", messagesReceived);
        Assert.Contains("PONG", allMessages, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task ClaudeCodeProcess_MultipleMessages_MaintainsContext()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-context");
        var messagesReceived = new List<string>();
        var messageCount = 0;
        var secondMessageReceived = new TaskCompletionSource<bool>();

        process.OnMessageReceived += message =>
        {
            messagesReceived.Add(message);
            messageCount++;
            if (messageCount >= 2)
            {
                secondMessageReceived.TrySetResult(true);
            }
        };

        await process.StartAsync();
        await Task.Delay(2000);

        // Act - Send two related messages
        await process.SendMessageAsync("Remember this number: 42");
        await Task.Delay(5000);

        await process.SendMessageAsync("What number did I ask you to remember?");

        await Task.WhenAny(
            secondMessageReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.True(messagesReceived.Count >= 2);
        var allMessages = string.Join(" ", messagesReceived);
        Assert.Contains("42", allMessages);
    }

    [SkippableFact]
    public async Task ClaudeCodeProcess_JsonOutputFormat_ReturnsJson()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-json");
        var messagesReceived = new List<string>();
        var messageReceivedEvent = new TaskCompletionSource<bool>();

        process.OnMessageReceived += message =>
        {
            messagesReceived.Add(message);
            messageReceivedEvent.TrySetResult(true);
        };

        await process.StartAsync();
        await Task.Delay(2000);

        // Act
        await process.SendMessageAsync("Say hello");

        await Task.WhenAny(
            messageReceivedEvent.Task,
            Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert - Should receive JSON formatted output
        Assert.NotEmpty(messagesReceived);
        // JSON output typically starts with { or contains json-like structure
        var firstMessage = messagesReceived.First();
        Assert.True(
            firstMessage.Contains("{") || firstMessage.Contains("\""),
            $"Expected JSON-like output but got: {firstMessage}");
    }

    [Fact]
    public void ClaudeCodeAvailability_ReportsCorrectly()
    {
        // This test always runs and documents the Claude Code availability
        var version = _fixture.GetClaudeCodeVersion();

        if (_fixture.IsClaudeCodeAvailable)
        {
            Assert.NotNull(version);
            Assert.NotEmpty(version);
        }
        else
        {
            Assert.Null(version);
        }
    }

    private IClaudeCodeProcess CreateProcess(string agentId, string? systemPrompt = null)
    {
        var factory = new ClaudeCodeProcessFactory(_fixture.ClaudeCodePath);
        var process = factory.Create(agentId, _fixture.WorkingDirectory, systemPrompt);
        _processesToCleanup.Add(process);
        return process;
    }

    public void Dispose()
    {
        foreach (var process in _processesToCleanup)
        {
            try
            {
                process.Dispose();
            }
            catch
            {
                // Best effort cleanup
            }
        }

        _fixture.Dispose();
    }
}
