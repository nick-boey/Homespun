using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.ClaudeCode.Controllers;

[ApiController]
[Route("api/session-cache")]
[Produces("application/json")]
public class SessionCacheController(IMessageCacheStore messageCacheStore) : ControllerBase
{
    [HttpGet("{sessionId}/messages")]
    [ProducesResponseType<IReadOnlyList<ClaudeMessage>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClaudeMessage>>> GetMessages(string sessionId, CancellationToken ct)
    {
        var messages = await messageCacheStore.GetMessagesAsync(sessionId, ct);
        return Ok(messages);
    }

    [HttpGet("{sessionId}/summary")]
    [ProducesResponseType<SessionCacheSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SessionCacheSummary>> GetSummary(string sessionId, CancellationToken ct)
    {
        var summary = await messageCacheStore.GetSessionSummaryAsync(sessionId, ct);
        if (summary == null)
        {
            return NotFound();
        }
        return Ok(summary);
    }

    [HttpGet("project/{projectId}")]
    [ProducesResponseType<IReadOnlyList<SessionCacheSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SessionCacheSummary>>> ListSessions(string projectId, CancellationToken ct)
    {
        var sessions = await messageCacheStore.ListSessionsAsync(projectId, ct);
        return Ok(sessions);
    }

    [HttpGet("entity/{projectId}/{entityId}")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetEntitySessionIds(
        string projectId,
        string entityId,
        CancellationToken ct)
    {
        var sessionIds = await messageCacheStore.GetSessionIdsForEntityAsync(projectId, entityId, ct);
        return Ok(sessionIds);
    }
}
