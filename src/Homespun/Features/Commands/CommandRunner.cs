using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Commands;

public class CommandRunner : ICommandRunner
{
    private readonly ILogger<CommandRunner> _logger;

    // Container mount point for data directory
    private const string ContainerDataPath = "/data";

    // Environment variable containing the host path that maps to /data in the container
    private static readonly string? HostDataPath = Environment.GetEnvironmentVariable("HSP_HOST_DATA_PATH");

    public CommandRunner(ILogger<CommandRunner> logger)
    {
        _logger = logger;
    }

    public async Task<CommandResult> RunAsync(string command, string arguments, string workingDirectory)
    {
        var stopwatch = Stopwatch.StartNew();

        // Translate container paths to host paths for beads commands on Linux hosts.
        // On Windows hosts, paths don't need translation (no host daemon or different path handling).
        var effectiveWorkingDirectory = IsLinuxHostPath(HostDataPath)
            ? TranslatePathForBeads(command, workingDirectory)
            : workingDirectory;

        // Add --no-daemon flag for beads commands to bypass daemon socket communication.
        // TODO: Make --no-daemon configurable via BeadsService options
        var effectiveArguments = AddBeadsFlags(command, arguments);

        _logger.LogInformation(
            "Executing command: {Command} {Arguments} in {WorkingDirectory}",
            command, effectiveArguments, effectiveWorkingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = effectiveArguments,
            WorkingDirectory = effectiveWorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            stopwatch.Stop();

            var result = new CommandResult
            {
                Success = process.ExitCode == 0,
                Output = output,
                Error = error,
                ExitCode = process.ExitCode
            };

            if (result.Success)
            {
                _logger.LogInformation(
                    "Command completed: {Command} {Arguments} | ExitCode={ExitCode} | Duration={Duration}ms",
                    command, arguments, result.ExitCode, stopwatch.ElapsedMilliseconds);
                
                // Log output at debug level for successful commands
                if (!string.IsNullOrWhiteSpace(output))
                {
                    _logger.LogDebug("Command output: {Output}", TruncateOutput(output));
                }
            }
            else
            {
                _logger.LogWarning(
                    "Command failed: {Command} {Arguments} | ExitCode={ExitCode} | Duration={Duration}ms | Error={Error}",
                    command, arguments, result.ExitCode, stopwatch.ElapsedMilliseconds, 
                    TruncateOutput(error));
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(
                ex,
                "Command exception: {Command} {Arguments} in {WorkingDirectory} | Duration={Duration}ms",
                command, arguments, workingDirectory, stopwatch.ElapsedMilliseconds);

            return new CommandResult
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    /// <summary>
    /// Truncates output to a reasonable length for logging.
    /// Full output is available in the CommandResult.
    /// </summary>
    private static string TruncateOutput(string output, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(output))
            return string.Empty;

        var trimmed = output.Trim();
        if (trimmed.Length <= maxLength)
            return trimmed;

        return trimmed[..maxLength] + "... [truncated]";
    }

    /// <summary>
    /// Determines if the host data path looks like a Linux path.
    /// Windows paths typically contain drive letters (C:/ or /c/) while Linux paths
    /// start with /home/, /data/, /var/, etc.
    /// </summary>
    private static bool IsLinuxHostPath(string? hostPath)
    {
        if (string.IsNullOrEmpty(hostPath))
            return false;

        // Windows paths in Docker typically look like:
        // - C:/Users/... or C:\Users\...
        // - /c/Users/... (Git Bash / MSYS style)
        // - /mnt/c/Users/... (WSL style)

        // Check for Windows drive letter patterns
        if (hostPath.Length >= 2 && char.IsLetter(hostPath[0]) && hostPath[1] == ':')
            return false;

        if (hostPath.StartsWith("/mnt/", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check for /c/ style paths (single letter after leading slash)
        if (hostPath.Length >= 3 && hostPath[0] == '/' && char.IsLetter(hostPath[1]) && hostPath[2] == '/')
            return false;

        // Assume Linux if starts with typical Linux paths
        return hostPath.StartsWith("/home/", StringComparison.Ordinal) ||
               hostPath.StartsWith("/var/", StringComparison.Ordinal) ||
               hostPath.StartsWith("/opt/", StringComparison.Ordinal) ||
               hostPath.StartsWith("/srv/", StringComparison.Ordinal) ||
               hostPath.StartsWith("/root/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Adds flags to beads (bd) commands.
    /// Currently adds --no-daemon to bypass socket communication with the host daemon.
    /// </summary>
    private static string AddBeadsFlags(string command, string arguments)
    {
        if (!command.Equals("bd", StringComparison.OrdinalIgnoreCase))
            return arguments;

        // Prepend --no-daemon to bypass daemon socket communication
        return string.IsNullOrEmpty(arguments)
            ? "--no-daemon"
            : $"--no-daemon {arguments}";
    }

    /// <summary>
    /// Translates container paths to host paths for beads commands.
    /// The beads daemon runs on the host and communicates via a socket,
    /// so it needs to see host paths rather than container paths.
    /// </summary>
    /// <param name="command">The command being executed</param>
    /// <param name="path">The path to translate</param>
    /// <returns>The translated path for beads commands, or the original path for other commands</returns>
    private string TranslatePathForBeads(string command, string path)
    {
        // Only translate for beads (bd) commands
        if (!command.Equals("bd", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        // Only translate if we have a host data path configured
        if (string.IsNullOrEmpty(HostDataPath))
        {
            return path;
        }

        // Only translate paths that start with the container data path
        if (!path.StartsWith(ContainerDataPath, StringComparison.Ordinal))
        {
            return path;
        }

        // Replace /data prefix with the host data path
        var translatedPath = HostDataPath + path[ContainerDataPath.Length..];

        _logger.LogDebug(
            "Translated beads path: {ContainerPath} -> {HostPath}",
            path, translatedPath);

        return translatedPath;
    }
}