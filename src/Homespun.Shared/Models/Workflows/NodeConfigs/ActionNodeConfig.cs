namespace Homespun.Shared.Models.Workflows.NodeConfigs;

/// <summary>
/// Type of action to perform.
/// </summary>
public enum ActionType
{
    /// <summary>Create a pull request.</summary>
    CreatePullRequest,

    /// <summary>Merge a pull request.</summary>
    MergePullRequest,

    /// <summary>Close a pull request.</summary>
    ClosePullRequest,

    /// <summary>Create a branch.</summary>
    CreateBranch,

    /// <summary>Delete a branch.</summary>
    DeleteBranch,

    /// <summary>Create an issue.</summary>
    CreateIssue,

    /// <summary>Update an issue.</summary>
    UpdateIssue,

    /// <summary>Send a notification.</summary>
    SendNotification,

    /// <summary>Execute a shell command.</summary>
    RunCommand,

    /// <summary>Make an HTTP request.</summary>
    HttpRequest,

    /// <summary>Set context variables.</summary>
    SetVariables,

    /// <summary>Trigger another workflow.</summary>
    TriggerWorkflow
}

/// <summary>
/// Configuration for an action node that performs a specific operation.
/// </summary>
public class ActionNodeConfig
{
    /// <summary>
    /// The type of action to perform.
    /// </summary>
    public ActionType ActionType { get; set; }

    /// <summary>
    /// Configuration for pull request actions.
    /// </summary>
    public PullRequestActionConfig? PullRequest { get; set; }

    /// <summary>
    /// Configuration for branch actions.
    /// </summary>
    public BranchActionConfig? Branch { get; set; }

    /// <summary>
    /// Configuration for issue actions.
    /// </summary>
    public IssueActionConfig? Issue { get; set; }

    /// <summary>
    /// Configuration for notification actions.
    /// </summary>
    public NotificationActionConfig? Notification { get; set; }

    /// <summary>
    /// Configuration for command execution.
    /// </summary>
    public CommandActionConfig? Command { get; set; }

    /// <summary>
    /// Configuration for HTTP requests.
    /// </summary>
    public HttpActionConfig? Http { get; set; }

    /// <summary>
    /// Configuration for setting variables.
    /// </summary>
    public SetVariablesConfig? Variables { get; set; }

    /// <summary>
    /// Configuration for triggering another workflow.
    /// </summary>
    public TriggerWorkflowConfig? TriggerWorkflow { get; set; }
}

/// <summary>
/// Configuration for pull request actions.
/// </summary>
public class PullRequestActionConfig
{
    /// <summary>
    /// PR number (for merge/close actions).
    /// Supports interpolation.
    /// </summary>
    public string? PullRequestNumber { get; set; }

    /// <summary>
    /// Branch name (for create PR).
    /// </summary>
    public string? BranchName { get; set; }

    /// <summary>
    /// Target branch for the PR.
    /// </summary>
    public string? TargetBranch { get; set; }

    /// <summary>
    /// PR title. Supports interpolation.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// PR body/description. Supports interpolation.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Whether to use draft PR.
    /// </summary>
    public bool Draft { get; set; } = false;

    /// <summary>
    /// Labels to add to the PR.
    /// </summary>
    public List<string>? Labels { get; set; }

    /// <summary>
    /// Reviewers to request.
    /// </summary>
    public List<string>? Reviewers { get; set; }

    /// <summary>
    /// Merge method (merge, squash, rebase).
    /// </summary>
    public string? MergeMethod { get; set; }

    /// <summary>
    /// Whether to delete the branch after merging.
    /// </summary>
    public bool DeleteBranchAfterMerge { get; set; } = true;
}

/// <summary>
/// Configuration for branch actions.
/// </summary>
public class BranchActionConfig
{
    /// <summary>
    /// Branch name. Supports interpolation.
    /// </summary>
    public required string BranchName { get; set; }

    /// <summary>
    /// Base branch to create from (for create actions).
    /// </summary>
    public string? BaseBranch { get; set; }

    /// <summary>
    /// Whether to force delete (for delete actions).
    /// </summary>
    public bool Force { get; set; } = false;
}

/// <summary>
/// Configuration for issue actions.
/// </summary>
public class IssueActionConfig
{
    /// <summary>
    /// Issue ID (for update actions).
    /// </summary>
    public string? IssueId { get; set; }

    /// <summary>
    /// Issue title. Supports interpolation.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Issue description. Supports interpolation.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Issue status to set.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Issue type.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// User to assign the issue to.
    /// </summary>
    public string? AssignTo { get; set; }

    /// <summary>
    /// Tags to add.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Parent issue ID for creating child issues.
    /// </summary>
    public string? ParentIssueId { get; set; }
}

/// <summary>
/// Configuration for notification actions.
/// </summary>
public class NotificationActionConfig
{
    /// <summary>
    /// Notification title. Supports interpolation.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Notification message. Supports interpolation.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Notification type (info, success, warning, error).
    /// </summary>
    public string Type { get; set; } = "info";

    /// <summary>
    /// Recipients (user emails/IDs). Empty means broadcast.
    /// </summary>
    public List<string>? Recipients { get; set; }

    /// <summary>
    /// Duration in milliseconds to show the notification.
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Optional URL to link to.
    /// </summary>
    public string? LinkUrl { get; set; }
}

/// <summary>
/// Configuration for command execution actions.
/// </summary>
public class CommandActionConfig
{
    /// <summary>
    /// The command to execute. Supports interpolation.
    /// </summary>
    public required string Command { get; set; }

    /// <summary>
    /// Working directory for the command.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables for the command.
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to capture stdout.
    /// </summary>
    public bool CaptureOutput { get; set; } = true;

    /// <summary>
    /// Whether to fail the node if the command exits with non-zero.
    /// </summary>
    public bool FailOnNonZeroExit { get; set; } = true;
}

/// <summary>
/// Configuration for HTTP request actions.
/// </summary>
public class HttpActionConfig
{
    /// <summary>
    /// The URL to request. Supports interpolation.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, etc.).
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    /// Request headers.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Request body. Supports interpolation.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Content type for the request body.
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Expected success status codes.
    /// </summary>
    public List<int> SuccessStatusCodes { get; set; } = [200, 201, 202, 204];

    /// <summary>
    /// Whether to parse JSON response.
    /// </summary>
    public bool ParseJsonResponse { get; set; } = true;
}

/// <summary>
/// Configuration for setting context variables.
/// </summary>
public class SetVariablesConfig
{
    /// <summary>
    /// Variables to set. Values support interpolation.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = [];
}

/// <summary>
/// Configuration for triggering another workflow.
/// </summary>
public class TriggerWorkflowConfig
{
    /// <summary>
    /// ID of the workflow to trigger.
    /// </summary>
    public required string WorkflowId { get; set; }

    /// <summary>
    /// Input data to pass to the triggered workflow.
    /// Values support interpolation.
    /// </summary>
    public Dictionary<string, string>? Input { get; set; }

    /// <summary>
    /// Whether to wait for the triggered workflow to complete.
    /// </summary>
    public bool WaitForCompletion { get; set; } = false;

    /// <summary>
    /// Timeout in seconds when waiting for completion.
    /// </summary>
    public int? WaitTimeoutSeconds { get; set; }
}
