using Fleece.Core.Models;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Background service for handling asynchronous agent startup.
/// </summary>
public interface IAgentStartBackgroundService
{
    /// <summary>
    /// Queues an agent startup task to run in the background.
    /// </summary>
    /// <param name="request">The agent start request containing all necessary parameters.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task QueueAgentStartAsync(AgentStartRequest request);
}

/// <summary>
/// Request model for starting an agent in the background.
/// </summary>
public record AgentStartRequest
{
    /// <summary>
    /// The ID of the issue to start the agent on.
    /// </summary>
    public required string IssueId { get; init; }

    /// <summary>
    /// The ID of the project containing the issue.
    /// </summary>
    public required string ProjectId { get; init; }

    /// <summary>
    /// The local file path of the project.
    /// </summary>
    public required string ProjectLocalPath { get; init; }

    /// <summary>
    /// The default branch of the project.
    /// </summary>
    public required string ProjectDefaultBranch { get; init; }

    /// <summary>
    /// The issue to start the agent on.
    /// </summary>
    public required Issue Issue { get; init; }

    /// <summary>
    /// The name of the skill (directory under <c>.claude/skills/</c>) to
    /// dispatch with. When set, the skill's SKILL.md body is used as the
    /// session's initial message and the skill's declared mode (if any)
    /// is applied when the caller did not set <see cref="Mode"/>.
    /// </summary>
    public string? SkillName { get; init; }

    /// <summary>
    /// Optional named arguments to append to the skill body when composing
    /// the initial message. Each entry becomes an <c>arg-name: value</c>
    /// line after the skill body.
    /// </summary>
    public IReadOnlyDictionary<string, string>? SkillArgs { get; init; }

    /// <summary>
    /// Optional system prompt override (e.g. for injecting schema context
    /// when an OpenSpec skill runs against a non-default schema). Passed
    /// verbatim to <c>StartSessionAsync</c>.
    /// </summary>
    public string? SystemPromptOverride { get; init; }

    /// <summary>
    /// The base branch to create the working branch from.
    /// </summary>
    public string? BaseBranch { get; init; }

    /// <summary>
    /// The Claude model to use.
    /// </summary>
    public string Model { get; init; } = "sonnet";

    /// <summary>
    /// The branch name to use for the agent session.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Optional user instructions that override the prompt template.
    /// When provided, this text is sent as the initial message instead of rendering the prompt template.
    /// </summary>
    public string? UserInstructions { get; init; }

    /// <summary>
    /// The session mode explicitly requested by the caller.
    /// When provided, takes precedence over any mode declared by the skill.
    /// </summary>
    public SessionMode? Mode { get; init; }
}
