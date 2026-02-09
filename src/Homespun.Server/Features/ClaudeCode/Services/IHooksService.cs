using Homespun.Features.ClaudeCode.Settings;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for loading and executing hooks from .claude/settings.json files.
/// </summary>
public interface IHooksService
{
    /// <summary>
    /// Loads hooks configuration from the settings file in the working directory.
    /// </summary>
    /// <param name="workingDirectory">Project working directory containing .claude/settings.json.</param>
    /// <returns>Parsed settings, or empty settings if file doesn't exist or is invalid.</returns>
    ClaudeSettings LoadSettings(string workingDirectory);

    /// <summary>
    /// Executes all SessionStart hooks defined in the settings.
    /// </summary>
    /// <param name="sessionId">Session ID for logging and tracking.</param>
    /// <param name="workingDirectory">Working directory for command execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of execution results for each hook command.</returns>
    Task<IReadOnlyList<HookExecutionResult>> ExecuteSessionStartHooksAsync(
        string sessionId,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
