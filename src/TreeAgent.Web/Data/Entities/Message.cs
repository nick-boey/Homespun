namespace TreeAgent.Web.Data.Entities;

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string AgentId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; }

    public Agent Agent { get; set; } = null!;
}
