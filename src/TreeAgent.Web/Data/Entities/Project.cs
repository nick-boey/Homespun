namespace TreeAgent.Web.Data.Entities;

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Name { get; set; }
    public required string LocalPath { get; set; }
    public string? GitHubOwner { get; set; }
    public string? GitHubRepo { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Feature> Features { get; set; } = [];
}
