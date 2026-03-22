using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Models.Workflows.NodeConfigs;

/// <summary>
/// Configuration for an agent node that executes Claude agent tasks.
/// </summary>
public class AgentNodeConfig
{
    /// <summary>
    /// The prompt to send to the agent.
    /// Supports context interpolation (e.g., "{{input.issueDescription}}").
    /// </summary>
    public required string Prompt { get; set; }

    /// <summary>
    /// Optional system prompt for the agent session.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// The session mode (Plan or Build).
    /// </summary>
    public SessionMode Mode { get; set; } = SessionMode.Build;

    /// <summary>
    /// The Claude model to use (e.g., "sonnet", "opus").
    /// If null, uses the project's default model.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Working directory for the agent.
    /// If null, uses the project's local path or an appropriate clone.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Whether to create a new branch for this agent's work.
    /// </summary>
    public bool CreateBranch { get; set; } = false;

    /// <summary>
    /// Branch name pattern to use when CreateBranch is true.
    /// Supports interpolation (e.g., "workflow/{{workflow.id}}/{{node.id}}").
    /// </summary>
    public string? BranchNamePattern { get; set; }

    /// <summary>
    /// Base branch to create the working branch from.
    /// If null, uses the project's default branch.
    /// </summary>
    public string? BaseBranch { get; set; }

    /// <summary>
    /// Maximum tokens for the agent response.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Whether to wait for user input if the agent asks a question.
    /// </summary>
    public bool WaitForQuestionAnswer { get; set; } = true;

    /// <summary>
    /// Default answers for known question patterns.
    /// Maps question pattern (regex) to answer.
    /// </summary>
    public Dictionary<string, string>? AutoAnswers { get; set; }

    /// <summary>
    /// Whether to automatically approve plans in Plan mode.
    /// </summary>
    public bool AutoApprovePlans { get; set; } = false;

    /// <summary>
    /// Output mapping - defines which parts of the agent output to extract.
    /// </summary>
    public AgentOutputMapping? OutputMapping { get; set; }
}

/// <summary>
/// Defines how to extract and map agent output to context variables.
/// </summary>
public class AgentOutputMapping
{
    /// <summary>
    /// Whether to capture the full conversation transcript.
    /// </summary>
    public bool CaptureTranscript { get; set; } = false;

    /// <summary>
    /// Whether to capture the agent's final response text.
    /// </summary>
    public bool CaptureFinalResponse { get; set; } = true;

    /// <summary>
    /// Custom extraction rules using regex patterns.
    /// Maps output field name to extraction pattern.
    /// </summary>
    public Dictionary<string, string>? ExtractionPatterns { get; set; }

    /// <summary>
    /// Variables to extract from tool results.
    /// Maps output field name to tool/field path (e.g., "gh pr create.url").
    /// </summary>
    public Dictionary<string, string>? ToolResultMappings { get; set; }
}
