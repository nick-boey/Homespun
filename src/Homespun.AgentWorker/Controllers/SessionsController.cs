using System.Text.Json;
using Homespun.AgentWorker.Models;
using Homespun.AgentWorker.Services;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.AgentWorker.Controllers;

/// <summary>
/// API controller for managing agent sessions.
/// Uses SSE (Server-Sent Events) for streaming responses.
/// </summary>
[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly WorkerSessionService _sessionService;
    private readonly ILogger<SessionsController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SessionsController(
        WorkerSessionService sessionService,
        ILogger<SessionsController> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <summary>
    /// Starts a new agent session and returns SSE stream.
    /// </summary>
    [HttpPost]
    public async Task StartSession([FromBody] StartSessionRequest request)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        _logger.LogInformation("Starting new session in {Mode} mode for {WorkingDirectory}",
            request.Mode, request.WorkingDirectory);

        try
        {
            await foreach (var (eventType, data) in _sessionService.StartSessionAsync(request, HttpContext.RequestAborted))
            {
                await WriteEventAsync(eventType, data);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Session start cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting session");
            await WriteEventAsync(SseEventTypes.Error, new ErrorData
            {
                SessionId = "unknown",
                Message = ex.Message,
                Code = "STARTUP_ERROR",
                IsRecoverable = false
            });
        }
    }

    /// <summary>
    /// Sends a message to an existing session and returns SSE stream.
    /// </summary>
    [HttpPost("{sessionId}/message")]
    public async Task SendMessage(string sessionId, [FromBody] SendMessageRequest request)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        _logger.LogInformation("Sending message to session {SessionId}", sessionId);

        try
        {
            await foreach (var (eventType, data) in _sessionService.SendMessageAsync(sessionId, request, HttpContext.RequestAborted))
            {
                await WriteEventAsync(eventType, data);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message send cancelled by client for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to session {SessionId}", sessionId);
            await WriteEventAsync(SseEventTypes.Error, new ErrorData
            {
                SessionId = sessionId,
                Message = ex.Message,
                Code = "MESSAGE_ERROR",
                IsRecoverable = false
            });
        }
    }

    /// <summary>
    /// Answers a pending question. The answer is sent as a tool result to the running CLI process.
    /// Events continue flowing through the original SSE stream from StartSession or SendMessage.
    /// </summary>
    [HttpPost("{sessionId}/answer")]
    public async Task<IActionResult> AnswerQuestion(string sessionId, [FromBody] AnswerQuestionRequest request)
    {
        _logger.LogInformation("Answering question in session {SessionId}", sessionId);

        try
        {
            await _sessionService.AnswerQuestionAsync(sessionId, request, HttpContext.RequestAborted);
            return Ok(new { message = "Answer accepted", sessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering question in session {SessionId}", sessionId);
            return StatusCode(500, new { message = ex.Message, code = "ANSWER_ERROR" });
        }
    }

    /// <summary>
    /// Stops a session.
    /// </summary>
    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> StopSession(string sessionId)
    {
        _logger.LogInformation("Stopping session {SessionId}", sessionId);

        var session = _sessionService.GetSession(sessionId);
        if (session == null)
        {
            return NotFound(new { message = $"Session {sessionId} not found" });
        }

        await _sessionService.StopSessionAsync(sessionId);
        return Ok(new { message = "Session stopped", sessionId });
    }

    /// <summary>
    /// Gets information about a session.
    /// </summary>
    [HttpGet("{sessionId}")]
    public IActionResult GetSession(string sessionId)
    {
        var session = _sessionService.GetSession(sessionId);
        if (session == null)
        {
            return NotFound(new { message = $"Session {sessionId} not found" });
        }

        return Ok(new
        {
            session.Id,
            session.WorkingDirectory,
            Mode = session.Mode.ToString(),
            session.Model,
            session.ConversationId,
            session.CreatedAt,
            session.LastActivityAt
        });
    }

    private async Task WriteEventAsync(string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var eventText = $"event: {eventType}\ndata: {json}\n\n";

        await Response.WriteAsync(eventText);
        await Response.Body.FlushAsync();
    }
}
