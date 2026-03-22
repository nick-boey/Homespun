namespace Homespun.Shared.Models.Workflows;

/// <summary>
/// Type of workflow trigger.
/// </summary>
public enum WorkflowTriggerType
{
    /// <summary>Manual trigger - workflow is started by user action.</summary>
    Manual,

    /// <summary>Event trigger - workflow starts when an event occurs.</summary>
    Event,

    /// <summary>Scheduled trigger - workflow runs on a schedule.</summary>
    Scheduled,

    /// <summary>Webhook trigger - workflow starts from external webhook.</summary>
    Webhook
}

/// <summary>
/// Events that can trigger workflow execution.
/// </summary>
public enum WorkflowEventType
{
    /// <summary>Issue created.</summary>
    IssueCreated,

    /// <summary>Issue status changed.</summary>
    IssueStatusChanged,

    /// <summary>Issue assigned.</summary>
    IssueAssigned,

    /// <summary>Pull request opened.</summary>
    PullRequestOpened,

    /// <summary>Pull request merged.</summary>
    PullRequestMerged,

    /// <summary>Pull request review requested.</summary>
    PullRequestReviewRequested,

    /// <summary>Pull request checks completed.</summary>
    PullRequestChecksCompleted,

    /// <summary>Agent session completed.</summary>
    AgentSessionCompleted,

    /// <summary>Agent session failed.</summary>
    AgentSessionFailed,

    /// <summary>Branch created.</summary>
    BranchCreated,

    /// <summary>Branch merged.</summary>
    BranchMerged,

    /// <summary>Custom event fired via API.</summary>
    Custom
}

/// <summary>
/// Trigger configuration for a workflow.
/// </summary>
public class WorkflowTrigger
{
    /// <summary>
    /// The type of trigger.
    /// </summary>
    public WorkflowTriggerType Type { get; set; }

    /// <summary>
    /// Whether this trigger is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Event configuration (for Event triggers).
    /// </summary>
    public EventTriggerConfig? EventConfig { get; set; }

    /// <summary>
    /// Schedule configuration (for Scheduled triggers).
    /// </summary>
    public ScheduleTriggerConfig? ScheduleConfig { get; set; }

    /// <summary>
    /// Webhook configuration (for Webhook triggers).
    /// </summary>
    public WebhookTriggerConfig? WebhookConfig { get; set; }
}

/// <summary>
/// Configuration for event-based triggers.
/// </summary>
public class EventTriggerConfig
{
    /// <summary>
    /// The event types that trigger this workflow.
    /// </summary>
    public List<WorkflowEventType> EventTypes { get; set; } = [];

    /// <summary>
    /// Optional filter conditions for the event.
    /// </summary>
    public EventFilter? Filter { get; set; }
}

/// <summary>
/// Filter conditions for event triggers.
/// </summary>
public class EventFilter
{
    /// <summary>
    /// Filter by issue types (for issue-related events).
    /// </summary>
    public List<string>? IssueTypes { get; set; }

    /// <summary>
    /// Filter by issue statuses (for IssueStatusChanged events).
    /// </summary>
    public List<string>? IssueStatuses { get; set; }

    /// <summary>
    /// Filter by branch patterns (glob patterns).
    /// </summary>
    public List<string>? BranchPatterns { get; set; }

    /// <summary>
    /// Filter by tags/labels.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Custom filter expression.
    /// </summary>
    public string? Expression { get; set; }
}

/// <summary>
/// Configuration for scheduled triggers.
/// </summary>
public class ScheduleTriggerConfig
{
    /// <summary>
    /// Cron expression for the schedule.
    /// </summary>
    public required string CronExpression { get; set; }

    /// <summary>
    /// Timezone for the schedule (e.g., "UTC", "America/New_York").
    /// </summary>
    public string Timezone { get; set; } = "UTC";

    /// <summary>
    /// Whether to skip execution if the previous run is still in progress.
    /// </summary>
    public bool SkipIfRunning { get; set; } = true;
}

/// <summary>
/// Configuration for webhook triggers.
/// </summary>
public class WebhookTriggerConfig
{
    /// <summary>
    /// Optional secret for validating webhook payloads.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Expected content type of webhook payload.
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Path parameters to extract from the webhook URL.
    /// </summary>
    public List<string>? PathParameters { get; set; }
}
