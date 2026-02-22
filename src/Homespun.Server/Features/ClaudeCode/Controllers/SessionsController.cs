using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Containers.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Containers;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using SdkPermissionMode = Homespun.ClaudeAgentSdk.PermissionMode;

namespace Homespun.Features.ClaudeCode.Controllers;

/// <summary>
/// API endpoints for managing Claude Code sessions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SessionsController(
    IClaudeSessionService sessionService,
    IProjectService projectService,
    IContainerQueryService containerService) : ControllerBase
{
    /// <summary>
    /// Get all active sessions.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<SessionSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SessionSummary>>> GetAll(CancellationToken cancellationToken)
    {
        var sessions = sessionService.GetAllSessions();

        // Fetch container status to get authoritative session status from workers
        var containers = await containerService.GetAllContainersAsync(cancellationToken);
        var containerByEntityId = containers
            .Where(c => !string.IsNullOrEmpty(c.IssueId))
            .ToDictionary(c => c.IssueId!, c => c);

        var summaries = sessions.Select(s => MapToSummary(s, containerByEntityId)).ToList();
        return Ok(summaries);
    }

    /// <summary>
    /// Get a session by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType<ClaudeSession>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ClaudeSession> GetById(string id)
    {
        var session = sessionService.GetSession(id);
        if (session == null)
        {
            return NotFound();
        }
        return Ok(session);
    }

    /// <summary>
    /// Get a session by entity ID.
    /// </summary>
    [HttpGet("entity/{entityId}")]
    [ProducesResponseType<ClaudeSession>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ClaudeSession> GetByEntityId(string entityId)
    {
        var session = sessionService.GetSessionByEntityId(entityId);
        if (session == null)
        {
            return NotFound();
        }
        return Ok(session);
    }

    /// <summary>
    /// Get sessions for a project.
    /// </summary>
    [HttpGet("project/{projectId}")]
    [ProducesResponseType<List<SessionSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SessionSummary>>> GetByProject(string projectId, CancellationToken cancellationToken)
    {
        var sessions = sessionService.GetSessionsForProject(projectId);

        // Fetch container status to get authoritative session status from workers
        var containers = await containerService.GetAllContainersAsync(cancellationToken);
        var containerByEntityId = containers
            .Where(c => !string.IsNullOrEmpty(c.IssueId))
            .ToDictionary(c => c.IssueId!, c => c);

        var summaries = sessions.Select(s => MapToSummary(s, containerByEntityId)).ToList();
        return Ok(summaries);
    }

    /// <summary>
    /// Get resumable sessions for an entity.
    /// </summary>
    [HttpGet("entity/{entityId}/resumable")]
    [ProducesResponseType<IReadOnlyList<ResumableSession>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ResumableSession>>> GetResumableSessions(
        string entityId,
        [FromQuery] string workingDirectory)
    {
        var sessions = await sessionService.GetResumableSessionsAsync(entityId, workingDirectory);
        return Ok(sessions);
    }

    /// <summary>
    /// Get session history for an entity.
    /// </summary>
    [HttpGet("history/{projectId}/{entityId}")]
    [ProducesResponseType<IReadOnlyList<SessionCacheSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SessionCacheSummary>>> GetSessionHistory(
        string projectId,
        string entityId)
    {
        var history = await sessionService.GetSessionHistoryAsync(projectId, entityId);
        return Ok(history);
    }

    /// <summary>
    /// Get cached messages for a session.
    /// </summary>
    [HttpGet("{id}/cached-messages")]
    [ProducesResponseType<IReadOnlyList<ClaudeMessage>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClaudeMessage>>> GetCachedMessages(string id)
    {
        var messages = await sessionService.GetCachedMessagesAsync(id);
        return Ok(messages);
    }

    /// <summary>
    /// Start a new Claude Code session.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ClaudeSession>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaudeSession>> Create([FromBody] CreateSessionRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var workingDirectory = request.WorkingDirectory ?? project.LocalPath;
        var model = request.Model ?? project.DefaultModel ?? "sonnet";

        try
        {
            var session = await sessionService.StartSessionAsync(
                request.EntityId,
                request.ProjectId,
                workingDirectory,
                request.Mode,
                model,
                request.SystemPrompt);

            return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to start session: {ex.Message}");
        }
    }

    /// <summary>
    /// Resume a previously saved session.
    /// </summary>
    [HttpPost("{id}/resume")]
    [ProducesResponseType<ClaudeSession>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClaudeSession>> Resume(string id, [FromBody] ResumeSessionRequest request)
    {
        try
        {
            var session = await sessionService.ResumeSessionAsync(
                id,
                request.EntityId,
                request.ProjectId,
                request.WorkingDirectory);

            return Ok(session);
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to resume session: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop all sessions for an entity.
    /// </summary>
    [HttpDelete("entity/{entityId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> StopAllForEntity(string entityId)
    {
        var count = await sessionService.StopAllSessionsForEntityAsync(entityId);
        return Ok(count);
    }

    /// <summary>
    /// Send a message to an existing session.
    /// </summary>
    [HttpPost("{id}/messages")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendMessage(string id, [FromBody] SendMessageRequest request)
    {
        var session = sessionService.GetSession(id);
        if (session == null)
        {
            return NotFound();
        }

        try
        {
            await sessionService.SendMessageAsync(id, request.Message, (SdkPermissionMode)request.PermissionMode);
            return Accepted();
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop an existing session.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stop(string id)
    {
        var session = sessionService.GetSession(id);
        if (session == null)
        {
            return NotFound();
        }

        try
        {
            await sessionService.StopSessionAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to stop session: {ex.Message}");
        }
    }

    /// <summary>
    /// Interrupt an existing session's current execution without fully stopping it.
    /// The session remains alive so the user can send another message to resume.
    /// </summary>
    [HttpPost("{id}/interrupt")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Interrupt(string id)
    {
        var session = sessionService.GetSession(id);
        if (session == null)
        {
            return NotFound();
        }

        try
        {
            await sessionService.InterruptSessionAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to interrupt session: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps a ClaudeSession to a SessionSummary, using container status as the authoritative source.
    /// </summary>
    /// <param name="session">The in-memory session.</param>
    /// <param name="containerByEntityId">Container lookup by entity/issue ID.</param>
    /// <returns>A SessionSummary with container-derived status when available.</returns>
    private static SessionSummary MapToSummary(
        ClaudeSession session,
        Dictionary<string, WorkerContainerDto> containerByEntityId)
    {
        // Try to find the container for this session's entity
        containerByEntityId.TryGetValue(session.EntityId, out var container);

        // Use container status as authoritative when available
        var status = container?.SessionStatus ?? session.Status;

        return new SessionSummary
        {
            Id = session.Id,
            EntityId = session.EntityId,
            ProjectId = session.ProjectId,
            Model = session.Model,
            Mode = session.Mode,
            Status = status,
            CreatedAt = session.CreatedAt,
            LastActivityAt = container?.LastActivityAt ?? session.LastActivityAt,
            MessageCount = session.Messages.Count,
            TotalCostUsd = session.TotalCostUsd,
            ContainerId = container?.ContainerId,
            ContainerName = container?.ContainerName
        };
    }
}

// SessionSummary is now defined in Homespun.Shared.Models.Sessions

/// <summary>
/// Request model for creating a session.
/// </summary>
public class CreateSessionRequest
{
    /// <summary>
    /// The entity ID (e.g., issue ID, PR ID).
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The session mode.
    /// </summary>
    public SessionMode Mode { get; set; } = SessionMode.Plan;

    /// <summary>
    /// The Claude model to use (defaults to project's default model).
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Working directory (defaults to project local path).
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Optional system prompt.
    /// </summary>
    public string? SystemPrompt { get; set; }
}

/// <summary>
/// Request model for sending a message.
/// </summary>
public class SendMessageRequest
{
    /// <summary>
    /// The message to send.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// The permission mode for this message. Defaults to BypassPermissions.
    /// </summary>
    public Homespun.Shared.Models.Sessions.PermissionMode PermissionMode { get; set; } =
        Homespun.Shared.Models.Sessions.PermissionMode.BypassPermissions;
}
