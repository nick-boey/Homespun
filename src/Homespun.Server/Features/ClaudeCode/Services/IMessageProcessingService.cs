using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Handles sending messages, processing SDK message streams,
/// content block assembly, and message format conversions.
/// </summary>
public interface IMessageProcessingService
{
    Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default);

    Task SendMessageAsync(string sessionId, string message, SessionMode mode, CancellationToken cancellationToken = default);

    Task SendMessageAsync(string sessionId, string message, SessionMode mode, string? model, CancellationToken cancellationToken = default);
}
