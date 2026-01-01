using TreeAgent.Web.Data.Entities;

namespace TreeAgent.Web.Services;

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
        _claudeCodePath = claudeCodePath ?? GetDefaultClaudeCodePath();
    }

    private static string GetDefaultClaudeCodePath()
    {
        var envPath = Environment.GetEnvironmentVariable("CLAUDE_CODE_PATH");
        return !string.IsNullOrEmpty(envPath) ? envPath : "claude";
    }

    public IClaudeCodeProcess Create(string agentId, string workingDirectory, string? systemPrompt = null)
    {
        return new ClaudeCodeProcess(agentId, _claudeCodePath, workingDirectory, systemPrompt);
    }
}
