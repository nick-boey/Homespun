using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Data;
using TreeAgent.Web.Data.Entities;

namespace TreeAgent.Web.Services;

public class ProjectService
{
    private readonly TreeAgentDbContext _db;

    public ProjectService(TreeAgentDbContext db)
    {
        _db = db;
    }

    public async Task<List<Project>> GetAllAsync()
    {
        return await _db.Projects
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Project?> GetByIdAsync(string id)
    {
        return await _db.Projects.FindAsync(id);
    }

    public async Task<Project> CreateAsync(string name, string localPath, string? gitHubOwner = null, string? gitHubRepo = null, string defaultBranch = "main")
    {
        var project = new Project
        {
            Name = name,
            LocalPath = localPath,
            GitHubOwner = gitHubOwner,
            GitHubRepo = gitHubRepo,
            DefaultBranch = defaultBranch
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    public async Task<Project?> UpdateAsync(string id, string name, string localPath, string? gitHubOwner, string? gitHubRepo, string defaultBranch)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null) return null;

        project.Name = name;
        project.LocalPath = localPath;
        project.GitHubOwner = gitHubOwner;
        project.GitHubRepo = gitHubRepo;
        project.DefaultBranch = defaultBranch;
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return project;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null) return false;

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();
        return true;
    }
}
