namespace Homespun.Shared.Models.Workflows.NodeConfigs;

/// <summary>
/// Type of gate.
/// </summary>
public enum GateType
{
    /// <summary>Requires manual approval from a user.</summary>
    ManualApproval,

    /// <summary>Waits for a condition to be met.</summary>
    Condition,

    /// <summary>Waits for a specific time period.</summary>
    Delay,

    /// <summary>Waits for an external webhook callback.</summary>
    Webhook,

    /// <summary>Waits for all incoming edges to complete (join).</summary>
    Join
}

/// <summary>
/// Configuration for a gate node that controls workflow progression.
/// </summary>
public class GateNodeConfig
{
    /// <summary>
    /// The type of gate.
    /// </summary>
    public GateType GateType { get; set; }

    /// <summary>
    /// Title to display for approval requests.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Description/instructions for approval requests.
    /// Supports context interpolation.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Approval configuration (for ManualApproval gates).
    /// </summary>
    public ApprovalConfig? Approval { get; set; }

    /// <summary>
    /// Condition configuration (for Condition gates).
    /// </summary>
    public GateConditionConfig? Condition { get; set; }

    /// <summary>
    /// Delay configuration (for Delay gates).
    /// </summary>
    public DelayConfig? Delay { get; set; }

    /// <summary>
    /// Join configuration (for Join gates).
    /// </summary>
    public JoinConfig? Join { get; set; }

    /// <summary>
    /// Timeout in seconds for the gate to be satisfied.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Action to take on timeout.
    /// </summary>
    public TimeoutAction TimeoutAction { get; set; } = TimeoutAction.Fail;
}

/// <summary>
/// Configuration for manual approval gates.
/// </summary>
public class ApprovalConfig
{
    /// <summary>
    /// Users who can approve (email or username).
    /// Empty list means any user can approve.
    /// </summary>
    public List<string> Approvers { get; set; } = [];

    /// <summary>
    /// Minimum number of approvals required.
    /// </summary>
    public int RequiredApprovals { get; set; } = 1;

    /// <summary>
    /// Options to present to the approver.
    /// </summary>
    public List<ApprovalOption> Options { get; set; } =
    [
        new ApprovalOption { Value = "approve", Label = "Approve", IsDefault = true },
        new ApprovalOption { Value = "reject", Label = "Reject" }
    ];

    /// <summary>
    /// Whether to require a comment with the approval.
    /// </summary>
    public bool RequireComment { get; set; } = false;

    /// <summary>
    /// Notification configuration for approval requests.
    /// </summary>
    public ApprovalNotificationConfig? Notifications { get; set; }
}

/// <summary>
/// An option for approval decisions.
/// </summary>
public class ApprovalOption
{
    /// <summary>
    /// The value to store when this option is selected.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Display label for the option.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// Optional description of what this option means.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is the default/recommended option.
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Visual style for the option (e.g., "primary", "danger", "warning").
    /// </summary>
    public string? Style { get; set; }
}

/// <summary>
/// Notification configuration for approval requests.
/// </summary>
public class ApprovalNotificationConfig
{
    /// <summary>
    /// Whether to send email notifications.
    /// </summary>
    public bool Email { get; set; } = true;

    /// <summary>
    /// Whether to send in-app notifications.
    /// </summary>
    public bool InApp { get; set; } = true;

    /// <summary>
    /// Custom message template for notifications.
    /// </summary>
    public string? MessageTemplate { get; set; }
}

/// <summary>
/// Configuration for condition-based gates.
/// </summary>
public class GateConditionConfig
{
    /// <summary>
    /// Expression to evaluate. Uses context interpolation.
    /// Should evaluate to a truthy/falsy value.
    /// </summary>
    public required string Expression { get; set; }

    /// <summary>
    /// How often to re-evaluate the condition (in seconds).
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of poll attempts before timing out.
    /// </summary>
    public int MaxAttempts { get; set; } = 100;
}

/// <summary>
/// Configuration for delay gates.
/// </summary>
public class DelayConfig
{
    /// <summary>
    /// Duration to wait in seconds.
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Optional specific time to wait until (UTC).
    /// </summary>
    public DateTime? WaitUntil { get; set; }

    /// <summary>
    /// Whether to skip the delay in test/dev environments.
    /// </summary>
    public bool SkipInDevelopment { get; set; } = false;
}

/// <summary>
/// Configuration for join gates.
/// </summary>
public class JoinConfig
{
    /// <summary>
    /// How to handle multiple incoming branches.
    /// </summary>
    public JoinMode Mode { get; set; } = JoinMode.All;

    /// <summary>
    /// Specific node IDs to wait for (if Mode is Specific).
    /// </summary>
    public List<string>? WaitForNodes { get; set; }

    /// <summary>
    /// Minimum number of branches that must complete (if Mode is AtLeast).
    /// </summary>
    public int? MinimumBranches { get; set; }
}

/// <summary>
/// Mode for join gates.
/// </summary>
public enum JoinMode
{
    /// <summary>Wait for all incoming branches.</summary>
    All,

    /// <summary>Continue when any incoming branch completes.</summary>
    Any,

    /// <summary>Wait for a minimum number of branches.</summary>
    AtLeast,

    /// <summary>Wait for specific nodes to complete.</summary>
    Specific
}

/// <summary>
/// Action to take when a gate times out.
/// </summary>
public enum TimeoutAction
{
    /// <summary>Fail the workflow.</summary>
    Fail,

    /// <summary>Skip the gate and continue.</summary>
    Skip,

    /// <summary>Use the default/first approval option.</summary>
    UseDefault
}
