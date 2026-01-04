using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Service for allocating available ports for OpenCode servers.
/// Ensures ports are actually available before allocating them.
/// </summary>
public interface IPortAllocationService
{
    /// <summary>
    /// Allocates an available port within the configured range.
    /// </summary>
    /// <returns>An available port number.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when maximum concurrent servers reached or no available ports found.
    /// </exception>
    int AllocatePort();
    
    /// <summary>
    /// Releases a previously allocated port back to the pool.
    /// </summary>
    void ReleasePort(int port);
    
    /// <summary>
    /// Checks if a specific port is currently in use.
    /// </summary>
    bool IsPortInUse(int port);
}

/// <summary>
/// Default implementation of port allocation with availability checking.
/// </summary>
public class PortAllocationService : IPortAllocationService
{
    private readonly OpenCodeOptions _options;
    private readonly ILogger<PortAllocationService> _logger;
    private readonly HashSet<int> _allocatedPorts = [];
    private readonly object _lock = new();
    private int _nextPortCandidate;

    public PortAllocationService(
        IOptions<OpenCodeOptions> options,
        ILogger<PortAllocationService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _nextPortCandidate = _options.BasePort;
    }

    public int AllocatePort()
    {
        lock (_lock)
        {
            if (_allocatedPorts.Count >= _options.MaxConcurrentServers)
            {
                throw new InvalidOperationException(
                    $"Maximum concurrent servers ({_options.MaxConcurrentServers}) reached. " +
                    "Stop a server before starting a new one.");
            }

            // Try to find an available port, checking up to MaxPortSearchAttempts ports
            const int maxAttempts = 100;
            var startPort = _nextPortCandidate;
            
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var port = _nextPortCandidate;
                _nextPortCandidate++;
                
                // Skip if we've already allocated this port
                if (_allocatedPorts.Contains(port))
                {
                    continue;
                }
                
                // Check if port is actually available
                if (!IsPortInUse(port))
                {
                    _allocatedPorts.Add(port);
                    _logger.LogDebug(
                        "Allocated port {Port} (checked {Attempts} ports starting from {StartPort})",
                        port, attempt + 1, startPort);
                    return port;
                }
                
                _logger.LogDebug("Port {Port} is in use, trying next port", port);
            }

            throw new InvalidOperationException(
                $"Could not find an available port after checking {maxAttempts} ports " +
                $"starting from {startPort}. All ports in range may be in use.");
        }
    }

    public void ReleasePort(int port)
    {
        lock (_lock)
        {
            if (_allocatedPorts.Remove(port))
            {
                _logger.LogDebug("Released port {Port}", port);
            }
            else
            {
                _logger.LogWarning("Attempted to release port {Port} that was not allocated", port);
            }
        }
    }

    public bool IsPortInUse(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }
}
