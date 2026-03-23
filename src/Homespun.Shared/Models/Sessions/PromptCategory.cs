namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Categorizes agent prompts to distinguish between standard and specialized prompt types.
/// </summary>
public enum PromptCategory
{
    /// <summary>
    /// Standard agent prompts available for normal agent sessions.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Prompts designed for issue agent workflows, selectable by the user.
    /// </summary>
    IssueAgent = 1
}
