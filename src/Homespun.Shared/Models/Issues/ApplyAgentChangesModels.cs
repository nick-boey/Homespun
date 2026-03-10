using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Shared.Models.Issues;

/// <summary>
/// Request model for applying agent changes from a clone back to the main branch.
/// </summary>
public class ApplyAgentChangesRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The session ID that made the changes.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// If true, only preview changes without applying them.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Strategy for handling conflicts.
    /// </summary>
    public ConflictResolutionStrategy ConflictStrategy { get; set; } = ConflictResolutionStrategy.Manual;
}

/// <summary>
/// Response model for applying agent changes.
/// </summary>
public class ApplyAgentChangesResponse
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// List of changes that were detected/applied.
    /// </summary>
    public List<IssueChangeDto> Changes { get; set; } = [];

    /// <summary>
    /// List of conflicts that need resolution.
    /// </summary>
    public List<IssueConflictDto> Conflicts { get; set; } = [];

    /// <summary>
    /// Summary message of the operation.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// If DryRun was true, indicates whether changes would be applied successfully.
    /// </summary>
    public bool? WouldApply { get; set; }
}

/// <summary>
/// Represents a single issue change from the agent.
/// </summary>
public class IssueChangeDto
{
    /// <summary>
    /// The issue ID.
    /// </summary>
    public required string IssueId { get; set; }

    /// <summary>
    /// The type of change.
    /// </summary>
    public required ChangeType ChangeType { get; set; }

    /// <summary>
    /// The issue title for display.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// List of field-level changes.
    /// </summary>
    public List<FieldChangeDto> FieldChanges { get; set; } = [];

    /// <summary>
    /// The issue state before changes (for updates/deletes).
    /// </summary>
    public IssueDto? OriginalIssue { get; set; }

    /// <summary>
    /// The issue state after changes (for creates/updates).
    /// </summary>
    public IssueDto? ModifiedIssue { get; set; }
}

/// <summary>
/// Types of changes that can be made to an issue.
/// </summary>
public enum ChangeType
{
    /// <summary>Issue was created.</summary>
    Created,
    /// <summary>Issue was updated.</summary>
    Updated,
    /// <summary>Issue was deleted.</summary>
    Deleted
}

/// <summary>
/// Represents a change to a specific field.
/// </summary>
public class FieldChangeDto
{
    /// <summary>
    /// The field name.
    /// </summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// The original value (as string).
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// The new value (as string).
    /// </summary>
    public string? NewValue { get; set; }
}

/// <summary>
/// Represents a conflict between agent changes and main branch.
/// </summary>
public class IssueConflictDto
{
    /// <summary>
    /// The issue ID with conflicts.
    /// </summary>
    public required string IssueId { get; set; }

    /// <summary>
    /// The issue title for display.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// List of conflicting fields.
    /// </summary>
    public List<FieldConflictDto> FieldConflicts { get; set; } = [];

    /// <summary>
    /// The base issue state (common ancestor).
    /// </summary>
    public IssueDto? BaseIssue { get; set; }

    /// <summary>
    /// The current main branch issue state.
    /// </summary>
    public IssueDto? MainIssue { get; set; }

    /// <summary>
    /// The agent's issue state.
    /// </summary>
    public IssueDto? AgentIssue { get; set; }
}

/// <summary>
/// Represents a conflict in a specific field.
/// </summary>
public class FieldConflictDto
{
    /// <summary>
    /// The field name.
    /// </summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// The base value (common ancestor).
    /// </summary>
    public string? BaseValue { get; set; }

    /// <summary>
    /// The current main branch value.
    /// </summary>
    public string? MainValue { get; set; }

    /// <summary>
    /// The agent's value.
    /// </summary>
    public string? AgentValue { get; set; }
}

/// <summary>
/// Strategy for handling conflicts.
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>Require manual resolution of conflicts.</summary>
    Manual,
    /// <summary>Agent changes win.</summary>
    AgentWins,
    /// <summary>Main branch changes win.</summary>
    MainWins,
    /// <summary>Abort on any conflict.</summary>
    Abort
}

/// <summary>
/// Request to resolve specific conflicts.
/// </summary>
public class ResolveConflictsRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The session ID that made the changes.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// List of conflict resolutions.
    /// </summary>
    public List<ConflictResolution> Resolutions { get; set; } = [];
}

/// <summary>
/// Resolution for a single conflict.
/// </summary>
public class ConflictResolution
{
    /// <summary>
    /// The issue ID.
    /// </summary>
    public required string IssueId { get; set; }

    /// <summary>
    /// Field-level resolutions.
    /// </summary>
    public List<FieldResolution> FieldResolutions { get; set; } = [];
}

/// <summary>
/// Resolution for a single field conflict.
/// </summary>
public class FieldResolution
{
    /// <summary>
    /// The field name.
    /// </summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// Which value to use.
    /// </summary>
    public ConflictChoice Choice { get; set; }

    /// <summary>
    /// Custom value (if Choice is Custom).
    /// </summary>
    public string? CustomValue { get; set; }
}

/// <summary>
/// Choice for resolving a field conflict.
/// </summary>
public enum ConflictChoice
{
    /// <summary>Use the main branch value.</summary>
    UseMain,
    /// <summary>Use the agent's value.</summary>
    UseAgent,
    /// <summary>Use a custom value.</summary>
    Custom
}