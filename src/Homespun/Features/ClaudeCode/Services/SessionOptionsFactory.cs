using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Factory for creating ClaudeAgentOptions based on session mode.
/// </summary>
public class SessionOptionsFactory
{
    /// <summary>
    /// Maximum buffer size for JSON message streaming.
    /// Set to 10MB to accommodate large Playwright MCP responses (page snapshots, base64-encoded screenshots).
    /// </summary>
    private const int DefaultMaxBufferSize = 10 * 1024 * 1024; // 10MB

    private readonly ILogger<SessionOptionsFactory> _logger;

    public SessionOptionsFactory(ILogger<SessionOptionsFactory> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Read-only tools available in Plan mode.
    /// Uses the custom MCP tool name instead of built-in AskUserQuestion.
    /// </summary>
    private static readonly string[] PlanModeTools =
    [
        "Read",
        "Glob",
        "Grep",
        "WebFetch",
        "WebSearch",
        "Task",
        "mcp__homespun__ask_user",
        "ExitPlanMode"
    ];

    /// <summary>
    /// Creates ClaudeAgentOptions for the specified session mode.
    /// </summary>
    /// <param name="mode">The session mode (Plan or Build).</param>
    /// <param name="workingDirectory">The working directory for the session.</param>
    /// <param name="model">The Claude model to use.</param>
    /// <param name="systemPrompt">Optional system prompt to include.</param>
    /// <param name="askUserFunction">Optional AIFunction for the custom ask_user MCP tool.
    /// When provided, registers a "homespun" MCP server and disallows the built-in AskUserQuestion.</param>
    /// <returns>Configured ClaudeAgentOptions.</returns>
    public ClaudeAgentOptions Create(SessionMode mode, string workingDirectory, string model,
        string? systemPrompt = null, AIFunction? askUserFunction = null)
    {
        var mcpServers = new Dictionary<string, object>
        {
            ["playwright"] = new Dictionary<string, object>
            {
                ["type"] = "stdio",
                ["command"] = "npx",
                ["args"] = new[] { "@playwright/mcp@latest", "--headless", "--browser", "chromium", "--no-sandbox", "--isolated" },
                ["env"] = new Dictionary<string, string>
                {
                    ["PLAYWRIGHT_BROWSERS_PATH"] = "/opt/playwright-browsers"
                }
            }
        };

        var options = new ClaudeAgentOptions
        {
            Cwd = workingDirectory,
            Model = model,
            SystemPrompt = systemPrompt,
            MaxBufferSize = DefaultMaxBufferSize,  // 10MB for Playwright MCP workloads
            BufferOverflowBehavior = BufferOverflowBehavior.SkipMessage,  // Gracefully skip large messages
            OnBufferOverflow = (messageType, actualSize, maxSize) =>
            {
                _logger.LogWarning(
                    "Buffer overflow detected: message type={MessageType}, size={ActualSize} bytes exceeds max={MaxSize} bytes. Message was skipped.",
                    messageType ?? "unknown", actualSize, maxSize);
            },
            SettingSources = [SettingSource.User],  // Enable loading user-level plugins
            McpServers = mcpServers
        };

        // Register the custom ask_user MCP tool and disallow the built-in AskUserQuestion
        if (askUserFunction != null)
        {
            var converter = new AIFunctionMcpConverter([askUserFunction], "homespun");
            mcpServers["homespun"] = converter.CreateMcpServerConfig();
            options.DisallowedTools = ["AskUserQuestion"];
        }

        if (mode == SessionMode.Plan)
        {
            // Plan mode: read-only tools only
            options.AllowedTools = PlanModeTools.ToList();
        }
        // Build mode: all tools allowed by default (don't set AllowedTools)

        return options;
    }
}
