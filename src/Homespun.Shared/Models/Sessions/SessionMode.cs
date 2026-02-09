namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Defines the mode of operation for a Claude Code session.
/// </summary>
public enum SessionMode
{
    /// <summary>
    /// Planning mode - read-only access to the codebase.
    /// Tools available: Read, Glob, Grep, WebFetch, WebSearch
    /// </summary>
    Plan,

    /// <summary>
    /// Build mode - full access to modify the codebase.
    /// All tools available including Write, Edit, Bash.
    /// </summary>
    Build
}
