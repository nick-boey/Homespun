namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Tracks plan-file artefacts written during a session and removes them when the
/// owning session is stopped, restarted, or its container removed. Closes the
/// FI-6 leak where plan files survived their owning session.
/// </summary>
public interface IPlanArtefactStore
{
    /// <summary>
    /// Records that <paramref name="filePath"/> was written by <paramref name="sessionId"/>.
    /// Repeated registrations of the same path are idempotent.
    /// </summary>
    void Register(string sessionId, string filePath);

    /// <summary>
    /// Deletes every registered file for <paramref name="sessionId"/> and removes the
    /// session from the registry. Returns the number of files actually deleted from disk.
    /// Missing files are treated as already-removed and counted as zero.
    /// </summary>
    Task<int> RemoveForSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the path is currently registered for any session. Test hook.
    /// </summary>
    bool IsRegistered(string filePath);
}
