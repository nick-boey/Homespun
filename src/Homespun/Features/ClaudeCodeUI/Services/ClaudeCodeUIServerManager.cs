using System.Collections.Concurrent;
using System.Diagnostics;
using Homespun.Features.ClaudeCodeUI.Models;
using Homespun.Features.OpenCode.Services;
using Microsoft.Extensions.Options;

namespace Homespun.Features.ClaudeCodeUI.Services;

/// <summary>
/// Manages Claude Code UI server instances.
/// </summary>
public class ClaudeCodeUIServerManager : IDisposable
{
    private readonly ClaudeCodeUIOptions _options;
    private readonly IClaudeCodeUIClient _client;
    private readonly IPortAllocationService _portService;
    private readonly ILogger<ClaudeCodeUIServerManager> _logger;
    private readonly ConcurrentDictionary<string, ClaudeCodeUIServer> _servers = new();
    private bool _disposed;

    public ClaudeCodeUIServerManager(
        IOptions<ClaudeCodeUIOptions> options,
        IClaudeCodeUIClient client,
        IPortAllocationService portService,
        ILogger<ClaudeCodeUIServerManager> logger)
    {
        _options = options.Value;
        _client = client;
        _portService = portService;
        _logger = logger;
    }

    /// <summary>
    /// Starts a new Claude Code UI server for an entity.
    /// </summary>
    public async Task<ClaudeCodeUIServer> StartServerAsync(
        string entityId,
        string workingDirectory,
        CancellationToken ct = default)
    {
        // Check if already running
        if (_servers.TryGetValue(entityId, out var existing) &&
            existing.Status == ClaudeCodeUIServerStatus.Running)
        {
            return existing;
        }

        var port = _portService.AllocatePort();
        var server = new ClaudeCodeUIServer
        {
            EntityId = entityId,
            WorkingDirectory = workingDirectory,
            Port = port,
            ExternalHostname = _options.ExternalHostname
        };

        _servers[entityId] = server;

        try
        {
            // Find executable
            var executablePath = await FindExecutableAsync(ct);

            // Start process
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--port {port}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Add environment variables
            startInfo.Environment["ANTHROPIC_API_KEY"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";

            // GitHub token for gh CLI
            var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrEmpty(githubToken))
            {
                startInfo.Environment["GITHUB_TOKEN"] = githubToken;
                startInfo.Environment["GH_TOKEN"] = githubToken;
            }

            _logger.LogInformation(
                "Starting Claude Code UI server for {EntityId} on port {Port}, cwd: {WorkingDirectory}",
                entityId, port, workingDirectory);

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Claude Code UI process");
            }

            server.Process = process;

            // Wait for server to be healthy
            var healthy = await WaitForHealthyAsync(server, ct);
            if (!healthy)
            {
                await StopServerAsync(entityId, ct);
                throw new InvalidOperationException(
                    $"Claude Code UI server failed to become healthy within {_options.ServerStartTimeoutMs}ms");
            }

            server.Status = ClaudeCodeUIServerStatus.Running;
            _logger.LogInformation(
                "Claude Code UI server started for {EntityId} on port {Port}",
                entityId, port);

            return server;
        }
        catch (Exception ex)
        {
            server.Status = ClaudeCodeUIServerStatus.Failed;
            _portService.ReleasePort(port);
            _servers.TryRemove(entityId, out _);
            _logger.LogError(ex, "Failed to start Claude Code UI server for {EntityId}", entityId);
            throw;
        }
    }

    /// <summary>
    /// Stops a running server.
    /// </summary>
    public async Task StopServerAsync(string entityId, CancellationToken ct = default)
    {
        if (!_servers.TryRemove(entityId, out var server))
        {
            return;
        }

        try
        {
            if (server.Process != null && !server.Process.HasExited)
            {
                server.Process.Kill(entireProcessTree: true);
                await server.Process.WaitForExitAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping Claude Code UI server for {EntityId}", entityId);
        }
        finally
        {
            _portService.ReleasePort(server.Port);
            server.Status = ClaudeCodeUIServerStatus.Stopped;
            _logger.LogInformation("Claude Code UI server stopped for {EntityId}", entityId);
        }
    }

    /// <summary>
    /// Gets a server for an entity.
    /// </summary>
    public ClaudeCodeUIServer? GetServerForEntity(string entityId)
    {
        _servers.TryGetValue(entityId, out var server);
        return server;
    }

    /// <summary>
    /// Gets all running servers.
    /// </summary>
    public IReadOnlyList<ClaudeCodeUIServer> GetRunningServers()
    {
        return _servers.Values
            .Where(s => s.Status == ClaudeCodeUIServerStatus.Running)
            .ToList();
    }

    /// <summary>
    /// Checks if a server is healthy.
    /// </summary>
    public async Task<bool> IsHealthyAsync(ClaudeCodeUIServer server, CancellationToken ct = default)
    {
        return await _client.IsHealthyAsync(server.BaseUrl, ct);
    }

    private async Task<bool> WaitForHealthyAsync(ClaudeCodeUIServer server, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(_options.ServerStartTimeoutMs);
        var interval = TimeSpan.FromMilliseconds(_options.HealthCheckIntervalMs);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout && !ct.IsCancellationRequested)
        {
            if (server.Process?.HasExited == true)
            {
                _logger.LogWarning("Claude Code UI process exited unexpectedly");
                return false;
            }

            if (await _client.IsHealthyAsync(server.BaseUrl, ct))
            {
                return true;
            }

            await Task.Delay(interval, ct);
        }

        return false;
    }

    private async Task<string> FindExecutableAsync(CancellationToken ct)
    {
        var executablePath = _options.ExecutablePath;

        // Check if it's an absolute path
        if (Path.IsPathRooted(executablePath) && File.Exists(executablePath))
        {
            return executablePath;
        }

        // Try to find in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, executablePath);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            // Try with .exe on Windows
            if (OperatingSystem.IsWindows())
            {
                var exePath = fullPath + ".exe";
                if (File.Exists(exePath))
                {
                    return exePath;
                }

                var cmdPath = fullPath + ".cmd";
                if (File.Exists(cmdPath))
                {
                    return cmdPath;
                }
            }
        }

        // Check npm global directory
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var npmPath = Path.Combine(appData, "npm", $"{executablePath}.cmd");
            if (File.Exists(npmPath))
            {
                return npmPath;
            }
        }

        // Return the original path and let the system try to resolve it
        return executablePath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var server in _servers.Values)
        {
            try
            {
                if (server.Process != null && !server.Process.HasExited)
                {
                    server.Process.Kill(entireProcessTree: true);
                }
                _portService.ReleasePort(server.Port);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing server for {EntityId}", server.EntityId);
            }
        }

        _servers.Clear();
    }
}
