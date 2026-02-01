using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Factory for creating ClaudeAgentOptions based on session mode.
/// </summary>
public class SessionOptionsFactory
{
    /// <summary>
    /// Read-only tools available in Plan mode.
    /// </summary>
    private static readonly string[] PlanModeTools =
    [
        "Read",
        "Glob",
        "Grep",
        "WebFetch",
        "WebSearch",
        "Task",
        "AskUserQuestion"
    ];

    /// <summary>
    /// Creates ClaudeAgentOptions for the specified session mode.
    /// </summary>
    /// <param name="mode">The session mode (Plan or Build).</param>
    /// <param name="workingDirectory">The working directory for the session.</param>
    /// <param name="model">The Claude model to use.</param>
    /// <param name="systemPrompt">Optional system prompt to include.</param>
    /// <returns>Configured ClaudeAgentOptions.</returns>
    public ClaudeAgentOptions Create(SessionMode mode, string workingDirectory, string model, string? systemPrompt = null)
    {
        var options = new ClaudeAgentOptions
        {
            Cwd = workingDirectory,
            Model = model,
            SystemPrompt = systemPrompt,
            SettingSources = [SettingSource.User],  // Enable loading user-level plugins
            // Configure Playwright MCP server for browser automation
            // Use dictionary with lowercase keys to match Claude CLI's expected JSON format
            // Container-specific flags:
            // - --browser chromium: Use installed Chromium (Chrome not available)
            // - --no-sandbox: Required for container environments without sandbox permissions
            // - --isolated: Use temp directory for browser profile (avoids permission issues)
            // - PLAYWRIGHT_BROWSERS_PATH: Point to shared location accessible by non-root users
            McpServers = new Dictionary<string, object>
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
            }
        };

        if (mode == SessionMode.Plan)
        {
            // Plan mode: read-only tools only
            options.AllowedTools = PlanModeTools.ToList();
        }
        // Build mode: all tools allowed by default (don't set AllowedTools)

        return options;
    }
}
