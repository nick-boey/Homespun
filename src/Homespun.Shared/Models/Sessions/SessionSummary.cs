namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Summary of a session for listing.
/// </summary>
public class SessionSummary
{
    public required string Id { get; init; }
    public required string EntityId { get; init; }
    public required string ProjectId { get; init; }
    public required string Model { get; init; }
    public required SessionMode Mode { get; init; }
    public ClaudeSessionStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
    public int MessageCount { get; init; }
    public decimal TotalCostUsd { get; init; }
}
