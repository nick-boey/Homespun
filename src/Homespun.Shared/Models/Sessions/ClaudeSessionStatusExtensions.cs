namespace Homespun.Shared.Models.Sessions;

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
        ClaudeSessionStatus.RunningHooks => "Running Hooks",
        ClaudeSessionStatus.Running => "Working",
        ClaudeSessionStatus.WaitingForInput => "Waiting",
        ClaudeSessionStatus.WaitingForQuestionAnswer => "Question",
        ClaudeSessionStatus.WaitingForPlanExecution => "Plan Ready",
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
        ClaudeSessionStatus.RunningHooks => "bg-warning text-dark",
        ClaudeSessionStatus.Running => "bg-success",
        ClaudeSessionStatus.WaitingForInput => "bg-info",
        ClaudeSessionStatus.WaitingForQuestionAnswer => "bg-status-question text-white",
        ClaudeSessionStatus.WaitingForPlanExecution => "bg-info",
        ClaudeSessionStatus.Stopped => "bg-secondary",
        ClaudeSessionStatus.Error => "bg-danger",
        _ => "bg-secondary"
    };

    /// <summary>
    /// Gets the BbBadge CSS class for a status (Tailwind-based for BbBadge component).
    /// </summary>
    public static string ToBbBadgeClass(this ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.Starting => "bg-amber-500 text-white border-transparent",
        ClaudeSessionStatus.RunningHooks => "bg-amber-500 text-white border-transparent",
        ClaudeSessionStatus.Running => "bg-green-600 text-white border-transparent",
        ClaudeSessionStatus.WaitingForInput => "bg-blue-500 text-white border-transparent",
        ClaudeSessionStatus.WaitingForQuestionAnswer => "bg-purple-500 text-white border-transparent",
        ClaudeSessionStatus.WaitingForPlanExecution => "bg-blue-500 text-white border-transparent",
        ClaudeSessionStatus.Stopped => "bg-secondary text-secondary-foreground border-transparent",
        ClaudeSessionStatus.Error => "bg-destructive text-destructive-foreground border-transparent",
        _ => "bg-secondary text-secondary-foreground border-transparent"
    };

    /// <summary>
    /// Gets the CSS indicator class for a status.
    /// </summary>
    public static string ToIndicatorClass(this ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.Starting => "starting",
        ClaudeSessionStatus.RunningHooks => "running-hooks",
        ClaudeSessionStatus.Running => "running",
        ClaudeSessionStatus.WaitingForInput => "waiting",
        ClaudeSessionStatus.WaitingForQuestionAnswer => "question",
        ClaudeSessionStatus.WaitingForPlanExecution => "plan-ready",
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
        ClaudeSessionStatus.WaitingForInput or
        ClaudeSessionStatus.WaitingForQuestionAnswer or
        ClaudeSessionStatus.WaitingForPlanExecution;
}
