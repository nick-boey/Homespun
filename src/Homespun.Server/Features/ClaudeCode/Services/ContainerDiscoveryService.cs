using System.Diagnostics;
using System.Text.Json;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for discovering Homespun worker containers after server restart.
/// Uses Docker labels to identify containers that were managed by this server.
/// </summary>
public class ContainerDiscoveryService : IContainerDiscoveryService
{
    private readonly ILogger<ContainerDiscoveryService> _logger;
    private readonly HttpClient _httpClient;

    public ContainerDiscoveryService(ILogger<ContainerDiscoveryService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5) // Short timeout for health checks
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredContainer>> DiscoverHomespunContainersAsync(CancellationToken ct)
    {
        var containers = new List<DiscoveredContainer>();

        try
        {
            // List all running containers with homespun.managed=true label
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "ps --filter \"label=homespun.managed=true\" --format \"{{json .}}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("Failed to start docker ps process for container discovery");
                return containers;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                _logger.LogWarning("docker ps exited with code {ExitCode} during container discovery: {Stderr}",
                    process.ExitCode, stderr);
                return containers;
            }

            // Parse each line as a separate JSON object
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parsed = ParseDockerPsJson(line);
                if (parsed == null)
                    continue;

                // Get container IP address
                var workerUrl = await GetContainerWorkerUrlAsync(parsed.Value.containerId, ct);
                if (string.IsNullOrEmpty(workerUrl))
                {
                    _logger.LogWarning("Could not get worker URL for container {ContainerId}, skipping",
                        parsed.Value.containerId);
                    continue;
                }

                // Health check the container
                if (!await IsContainerHealthyAsync(workerUrl, ct))
                {
                    _logger.LogWarning("Container {ContainerId} at {WorkerUrl} failed health check, skipping",
                        parsed.Value.containerId, workerUrl);
                    continue;
                }

                var container = new DiscoveredContainer(
                    parsed.Value.containerId,
                    parsed.Value.containerName,
                    workerUrl,
                    parsed.Value.projectId,
                    parsed.Value.issueId,
                    parsed.Value.workingDirectory,
                    parsed.Value.createdAt);

                containers.Add(container);

                _logger.LogInformation(
                    "Discovered Homespun container {ContainerName} ({ContainerId}) at {WorkerUrl} for {WorkingDirectory}",
                    container.ContainerName, container.ContainerId, container.WorkerUrl, container.WorkingDirectory);
            }

            _logger.LogInformation("Container discovery found {Count} healthy Homespun containers", containers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during container discovery");
        }

        return containers;
    }

    /// <summary>
    /// Parses a single line of JSON output from docker ps --format json.
    /// Returns null if the line is invalid or missing required labels.
    /// </summary>
    internal static (
        string containerId,
        string containerName,
        string workingDirectory,
        string? projectId,
        string? issueId,
        DateTime createdAt)? ParseDockerPsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Get container ID and name
            if (!root.TryGetProperty("ID", out var idProp) ||
                !root.TryGetProperty("Names", out var namesProp))
                return null;

            var containerId = idProp.GetString();
            var containerName = namesProp.GetString();

            if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(containerName))
                return null;

            // Get labels string
            if (!root.TryGetProperty("Labels", out var labelsProp))
                return null;

            var labels = labelsProp.GetString() ?? "";

            // Verify this is a Homespun-managed container
            var managed = ParseLabelValue(labels, "homespun.managed");
            if (managed != "true")
                return null;

            // Get required working directory
            var workingDirectory = ParseLabelValue(labels, "homespun.working.directory");
            if (string.IsNullOrEmpty(workingDirectory))
                return null;

            // Get optional project and issue IDs
            var projectId = ParseLabelValue(labels, "homespun.project.id");
            var issueId = ParseLabelValue(labels, "homespun.issue.id");

            // Get creation timestamp
            var createdAtStr = ParseLabelValue(labels, "homespun.created.at");
            var createdAt = DateTime.MinValue;
            if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, out var parsed))
            {
                createdAt = parsed;
            }

            return (containerId, containerName, workingDirectory, projectId, issueId, createdAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a label value from a comma-separated label string.
    /// Format: "label1=value1,label2=value2"
    /// </summary>
    internal static string? ParseLabelValue(string labels, string labelName)
    {
        if (string.IsNullOrEmpty(labels))
            return null;

        var prefix = $"{labelName}=";
        foreach (var part in labels.Split(','))
        {
            if (part.StartsWith(prefix, StringComparison.Ordinal))
            {
                return part[prefix.Length..];
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the worker URL for a container by inspecting its IP address.
    /// </summary>
    private async Task<string?> GetContainerWorkerUrlAsync(string containerId, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"inspect -f \"{{{{range.NetworkSettings.Networks}}}}{{{{.IPAddress}}}}{{{{end}}}}\" {containerId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
                return null;

            var ipAddress = stdout.Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(ipAddress) || ipAddress.StartsWith('<'))
                return null;

            return $"http://{ipAddress}:8080";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if a container's worker is healthy.
    /// </summary>
    private async Task<bool> IsContainerHealthyAsync(string workerUrl, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{workerUrl}/api/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
