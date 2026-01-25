namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Extension methods for ClaudeSessionStatus.
/// </summary>
public static class ClaudeSessionStatusExtensions
{
    /// <summary>
    /// Gets the human-readable display label for a status.
    /// </summary>
    public static string ToDisplayLabel(this ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.Starting => "Starting",
        ClaudeSessionStatus.Running => "Working",
        ClaudeSessionStatus.WaitingForInput => "Waiting",
        ClaudeSessionStatus.Stopped => "Stopped",
        ClaudeSessionStatus.Error => "Error",
        _ => status.ToString()
    };

    /// <summary>
    /// Gets the Bootstrap badge CSS class for a status.
    /// </summary>
    public static string ToBadgeClass(this ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.Starting => "bg-warning text-dark",
        ClaudeSessionStatus.Running => "bg-success",
        ClaudeSessionStatus.WaitingForInput => "bg-info",
        ClaudeSessionStatus.Stopped => "bg-secondary",
        ClaudeSessionStatus.Error => "bg-danger",
        _ => "bg-secondary"
    };

    /// <summary>
    /// Gets the CSS indicator class for a status.
    /// </summary>
    public static string ToIndicatorClass(this ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.Starting => "starting",
        ClaudeSessionStatus.Running => "running",
        ClaudeSessionStatus.WaitingForInput => "waiting",
        ClaudeSessionStatus.Stopped => "stopped",
        ClaudeSessionStatus.Error => "error",
        _ => "stopped"
    };

    /// <summary>
    /// Returns true if the status represents an active session.
    /// </summary>
    public static bool IsActive(this ClaudeSessionStatus status) => status is
        ClaudeSessionStatus.Starting or
        ClaudeSessionStatus.Running or
        ClaudeSessionStatus.WaitingForInput;
}
