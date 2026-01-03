using TreeAgent.Web.Features.Agents.Data;

namespace TreeAgent.Web.Features.Agents.Services;

public interface IClaudeCodeProcess : IDisposable
{
    bool IsRunning { get; }
    AgentStatus Status { get; }
    event Action<string>? OnMessageReceived;
    event Action<AgentStatus>? OnStatusChanged;
    Task StartAsync();
    Task StopAsync();
    Task SendMessageAsync(string message);
}

public interface IClaudeCodeProcessFactory
{
    IClaudeCodeProcess Create(string agentId, string workingDirectory, string? systemPrompt = null);
}

public class ClaudeCodeProcessFactory : IClaudeCodeProcessFactory
{
    private readonly string _claudeCodePath;

    public ClaudeCodeProcessFactory(string? claudeCodePath = null)
    {
        _claudeCodePath = claudeCodePath ?? new ClaudeCodePathResolver().Resolve();
    }

    public IClaudeCodeProcess Create(string agentId, string workingDirectory, string? systemPrompt = null)
    {
        return new ClaudeCodeProcess(agentId, _claudeCodePath, workingDirectory, systemPrompt);
    }
}
