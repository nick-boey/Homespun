using System.Collections.Concurrent;

namespace Homespun.Features.ClaudeCode.Services;

/// <inheritdoc />
public sealed class PlanArtefactStore(ILogger<PlanArtefactStore> logger) : IPlanArtefactStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _bySession = new();

    public void Register(string sessionId, string filePath)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        // Skip non-disk paths (e.g. the "agent:~/.claude/plans/" placeholder used when the
        // server only read the plan via the agent-container RPC and there is no real file
        // to clean up on the host).
        if (filePath.StartsWith("agent:", StringComparison.Ordinal))
        {
            return;
        }

        var paths = _bySession.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        paths.TryAdd(filePath, 0);
    }

    public Task<int> RemoveForSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.FromResult(0);
        }

        if (!_bySession.TryRemove(sessionId, out var paths))
        {
            return Task.FromResult(0);
        }

        var deleted = 0;
        foreach (var path in paths.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete plan artefact {Path} for session {SessionId}", path, sessionId);
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("Removed {Count} plan artefact(s) for session {SessionId}", deleted, sessionId);
        }

        return Task.FromResult(deleted);
    }

    public bool IsRegistered(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        foreach (var paths in _bySession.Values)
        {
            if (paths.ContainsKey(filePath))
            {
                return true;
            }
        }
        return false;
    }
}
