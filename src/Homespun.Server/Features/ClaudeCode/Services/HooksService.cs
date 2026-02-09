using System.Diagnostics;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Settings;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for loading and executing hooks from .claude/settings.json files.
/// </summary>
public class HooksService : IHooksService
{
    private readonly ICommandRunner _commandRunner;
    private readonly ILogger<HooksService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HooksService(
        ICommandRunner commandRunner,
        ILogger<HooksService> logger)
    {
        _commandRunner = commandRunner;
        _logger = logger;
    }

    /// <inheritdoc />
    public ClaudeSettings LoadSettings(string workingDirectory)
    {
        var settingsPath = Path.Combine(workingDirectory, ".claude", "settings.json");

        if (!File.Exists(settingsPath))
        {
            _logger.LogDebug("No settings file found at {Path}", settingsPath);
            return new ClaudeSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<ClaudeSettings>(json, JsonOptions);

            if (settings == null)
            {
                _logger.LogWarning("Failed to deserialize settings from {Path}", settingsPath);
                return new ClaudeSettings();
            }

            _logger.LogDebug("Loaded settings from {Path}", settingsPath);
            return settings;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse settings JSON from {Path}", settingsPath);
            return new ClaudeSettings();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read settings file from {Path}", settingsPath);
            return new ClaudeSettings();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HookExecutionResult>> ExecuteSessionStartHooksAsync(
        string sessionId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings(workingDirectory);
        var results = new List<HookExecutionResult>();

        if (settings.Hooks == null ||
            !settings.Hooks.TryGetValue("SessionStart", out var hookGroups))
        {
            _logger.LogDebug("No SessionStart hooks configured for session {SessionId}", sessionId);
            return results;
        }

        _logger.LogInformation("Executing SessionStart hooks for session {SessionId}", sessionId);

        foreach (var group in hookGroups)
        {
            foreach (var hook in group.Hooks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Hook execution cancelled for session {SessionId}", sessionId);
                    break;
                }

                if (hook.Type != "command" || string.IsNullOrEmpty(hook.Command))
                {
                    _logger.LogDebug("Skipping non-command hook or hook with empty command");
                    continue;
                }

                var result = await ExecuteHookCommandAsync(
                    "SessionStart", hook, workingDirectory, cancellationToken);
                results.Add(result);
            }
        }

        _logger.LogInformation(
            "Completed {Count} SessionStart hook(s) for session {SessionId}",
            results.Count, sessionId);

        return results;
    }

    private async Task<HookExecutionResult> ExecuteHookCommandAsync(
        string hookType,
        HookDefinition hook,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = hook.Command!;

        _logger.LogInformation(
            "Executing {HookType} hook: {Command} in {WorkingDirectory}",
            hookType, command, workingDirectory);

        try
        {
            // Parse command into executable and arguments
            var (executable, arguments) = ParseCommand(command);

            var commandResult = await _commandRunner.RunAsync(
                executable, arguments, workingDirectory);

            stopwatch.Stop();

            var result = new HookExecutionResult
            {
                HookType = hookType,
                Command = command,
                Success = commandResult.Success,
                Output = commandResult.Output,
                Error = commandResult.Error,
                ExitCode = commandResult.ExitCode,
                Duration = stopwatch.Elapsed
            };

            if (result.Success)
            {
                _logger.LogInformation(
                    "Hook completed successfully: {Command} ({Duration}ms)",
                    command, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "Hook failed: {Command} (exit code {ExitCode}) in {WorkingDirectory}: {Error}",
                    command, result.ExitCode, workingDirectory, TruncateForLog(result.Error));
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Exception executing hook: {Command}", command);

            return new HookExecutionResult
            {
                HookType = hookType,
                Command = command,
                Success = false,
                Error = ex.Message,
                ExitCode = -1,
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Parses a command string into executable and arguments.
    /// </summary>
    private static (string Executable, string Arguments) ParseCommand(string command)
    {
        // Simple parsing: first token is executable, rest are arguments
        var trimmed = command.Trim();
        var spaceIndex = trimmed.IndexOf(' ');

        if (spaceIndex < 0)
        {
            return (trimmed, "");
        }

        return (trimmed[..spaceIndex], trimmed[(spaceIndex + 1)..]);
    }

    private static string? TruncateForLog(string? text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (text.Length <= maxLength)
            return text;

        return text[..maxLength] + "...";
    }
}
