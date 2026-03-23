using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Models.Workflows;

/// <summary>
/// Type of workflow step.
/// </summary>
public enum WorkflowStepType
{
    /// <summary>Executes a Claude agent task.</summary>
    Agent,

    /// <summary>Performs a server-side action (e.g., create PR, run command).</summary>
    ServerAction,

    /// <summary>Requires approval or condition to proceed.</summary>
    Gate
}

/// <summary>
/// Type of step transition.
/// </summary>
public enum StepTransitionType
{
    /// <summary>Proceed to the next step in the list.</summary>
    NextStep,

    /// <summary>Exit the workflow.</summary>
    Exit,

    /// <summary>Jump to a specific step by ID.</summary>
    GoToStep,

    /// <summary>Retry the current step (only valid for OnFailure).</summary>
    Retry
}

/// <summary>
/// Defines what happens after a step completes or fails.
/// </summary>
public class StepTransition
{
    /// <summary>
    /// The type of transition.
    /// </summary>
    public StepTransitionType Type { get; set; } = StepTransitionType.NextStep;

    /// <summary>
    /// Target step ID when Type is GoToStep.
    /// </summary>
    public string? TargetStepId { get; set; }
}

/// <summary>
/// Represents a single step in a sequential workflow.
/// </summary>
public class WorkflowStep
{
    /// <summary>
    /// Unique identifier for this step within the workflow.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Display name for the step.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The type of step.
    /// </summary>
    public WorkflowStepType StepType { get; set; }

    /// <summary>
    /// Inline prompt text for agent steps.
    /// Supports context interpolation (e.g., "{{input.issueDescription}}").
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Reference to a named AgentPrompt template.
    /// </summary>
    public string? PromptId { get; set; }

    /// <summary>
    /// The session mode for agent steps (Plan or Build).
    /// </summary>
    public SessionMode SessionMode { get; set; } = SessionMode.Build;

    /// <summary>
    /// What to do when the step succeeds.
    /// </summary>
    public StepTransition OnSuccess { get; set; } = new() { Type = StepTransitionType.NextStep };

    /// <summary>
    /// What to do when the step fails.
    /// </summary>
    public StepTransition OnFailure { get; set; } = new() { Type = StepTransitionType.Exit };

    /// <summary>
    /// Maximum number of retry attempts (when OnFailure.Type is Retry).
    /// </summary>
    public int MaxRetries { get; set; } = 0;

    /// <summary>
    /// Delay in seconds between retry attempts.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Simple expression for skip logic. If evaluates to false, the step is skipped.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Step-type-specific configuration as JSON.
    /// </summary>
    [JsonPropertyName("config")]
    public JsonElement? Config { get; set; }
}
