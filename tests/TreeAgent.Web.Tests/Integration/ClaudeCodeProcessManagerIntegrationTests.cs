using TreeAgent.Web.Data.Entities;
using TreeAgent.Web.Services;
using TreeAgent.Web.Tests.Integration.Fixtures;

namespace TreeAgent.Web.Tests.Integration;

/// <summary>
/// Integration tests for ClaudeCodeProcessManager that test multiple agent management.
/// These tests require Claude Code to be installed and available on the PATH.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "ClaudeCode")]
public class ClaudeCodeProcessManagerIntegrationTests : IDisposable
{
    private readonly ClaudeCodeTestFixture _fixture;
    private readonly ClaudeCodeProcessManager _manager;

    public ClaudeCodeProcessManagerIntegrationTests()
    {
        _fixture = new ClaudeCodeTestFixture();
        _manager = new ClaudeCodeProcessManager(new ClaudeCodeProcessFactory(_fixture.ClaudeCodePath));
    }

    [SkippableFact]
    public async Task StartAgent_SingleAgent_StartsSuccessfully()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var agentId = "test-agent-1";
        var statusChanges = new List<(string AgentId, AgentStatus Status)>();
        _manager.OnStatusChanged += (id, status) => statusChanges.Add((id, status));

        // Act
        var result = await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(2000); // Allow time for process to start

        // Assert
        Assert.True(result);
        Assert.True(_manager.IsAgentRunning(agentId));
        Assert.Equal(AgentStatus.Running, _manager.GetAgentStatus(agentId));
    }

    [SkippableFact]
    public async Task StartAgent_DuplicateAgentId_ReturnsFalse()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var agentId = "test-duplicate";
        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(1000);

        // Act
        var result = await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);

        // Assert
        Assert.False(result);
    }

    [SkippableFact]
    public async Task StartAgent_MultipleAgents_AllRunConcurrently()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var agentIds = new[] { "agent-1", "agent-2", "agent-3" };

        // Act
        foreach (var id in agentIds)
        {
            await _manager.StartAgentAsync(id, _fixture.WorkingDirectory);
        }
        await Task.Delay(3000); // Allow time for all processes to start

        // Assert
        Assert.Equal(3, _manager.GetRunningAgentCount());
        foreach (var id in agentIds)
        {
            Assert.True(_manager.IsAgentRunning(id));
        }
    }

    [SkippableFact]
    public async Task StopAgent_RunningAgent_StopsSuccessfully()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var agentId = "test-stop-manager";
        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(2000);
        Assert.True(_manager.IsAgentRunning(agentId));

        // Act
        var result = await _manager.StopAgentAsync(agentId);

        // Assert
        Assert.True(result);
        Assert.False(_manager.IsAgentRunning(agentId));
        Assert.Equal(AgentStatus.Stopped, _manager.GetAgentStatus(agentId));
    }

    [SkippableFact]
    public async Task StopAgent_NonExistentAgent_ReturnsFalse()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Act
        var result = await _manager.StopAgentAsync("non-existent");

        // Assert
        Assert.False(result);
    }

    [SkippableFact]
    public async Task SendMessage_RunningAgent_SendsSuccessfully()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var agentId = "test-send-message";
        var messagesReceived = new List<string>();
        _manager.OnMessageReceived += (id, message) =>
        {
            if (id == agentId) messagesReceived.Add(message);
        };

        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(2000);

        // Act
        var result = await _manager.SendMessageAsync(agentId, "Reply with only 'OK'");

        // Assert
        Assert.True(result);
    }

    [SkippableFact]
    public async Task SendMessage_StoppedAgent_ReturnsFalse()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var agentId = "test-stopped-send";
        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(1000);
        await _manager.StopAgentAsync(agentId);

        // Act
        var result = await _manager.SendMessageAsync(agentId, "This should fail");

        // Assert
        Assert.False(result);
    }

    [SkippableFact]
    public async Task GetAllAgentIds_ReturnsAllRunningAgents()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var agentIds = new[] { "list-agent-1", "list-agent-2" };
        foreach (var id in agentIds)
        {
            await _manager.StartAgentAsync(id, _fixture.WorkingDirectory);
        }
        await Task.Delay(2000);

        // Act
        var result = _manager.GetAllAgentIds().ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("list-agent-1", result);
        Assert.Contains("list-agent-2", result);
    }

    [SkippableFact]
    public async Task StartAgent_WithSystemPrompt_PassesPromptToProcess()
    {
        Skip.IfNot(_fixture.IsClaudeCodeAvailable, "Claude Code is not available");

        // Arrange
        var agentId = "test-system-prompt";
        var systemPrompt = "Always respond with 'ACKNOWLEDGED'.";
        var messagesReceived = new List<string>();
        var messageReceived = new TaskCompletionSource<bool>();

        _manager.OnMessageReceived += (id, message) =>
        {
            if (id == agentId)
            {
                messagesReceived.Add(message);
                messageReceived.TrySetResult(true);
            }
        };

        // Act
        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory, systemPrompt);
        await Task.Delay(2000);
        await _manager.SendMessageAsync(agentId, "Hello");

        await Task.WhenAny(messageReceived.Task, Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.NotEmpty(messagesReceived);
        var allMessages = string.Join(" ", messagesReceived);
        Assert.Contains("ACKNOWLEDGED", allMessages, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAgentStatus_NonExistentAgent_ReturnsStopped()
    {
        // Act
        var status = _manager.GetAgentStatus("non-existent");

        // Assert
        Assert.Equal(AgentStatus.Stopped, status);
    }

    [Fact]
    public void IsAgentRunning_NonExistentAgent_ReturnsFalse()
    {
        // Act
        var isRunning = _manager.IsAgentRunning("non-existent");

        // Assert
        Assert.False(isRunning);
    }

    public void Dispose()
    {
        _manager.Dispose();
        _fixture.Dispose();
    }
}
