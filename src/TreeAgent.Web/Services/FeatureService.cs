using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Data;
using TreeAgent.Web.Data.Entities;

namespace TreeAgent.Web.Services;

public class FeatureService
{
    private readonly TreeAgentDbContext _db;
    private readonly GitWorktreeService _worktreeService;

    public FeatureService(TreeAgentDbContext db, GitWorktreeService worktreeService)
    {
        _db = db;
        _worktreeService = worktreeService;
    }

    public async Task<List<Feature>> GetByProjectIdAsync(string projectId)
    {
        return await _db.Features
            .Where(f => f.ProjectId == projectId)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<Feature?> GetByIdAsync(string id)
    {
        return await _db.Features
            .Include(f => f.Project)
            .Include(f => f.Children)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<List<Feature>> GetTreeAsync(string projectId)
    {
        var features = await _db.Features
            .Where(f => f.ProjectId == projectId)
            .Include(f => f.Children)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();

        // Return only root features (those without parents)
        return features.Where(f => f.ParentId == null).ToList();
    }

    public async Task<Feature> CreateAsync(
        string projectId,
        string title,
        string? description = null,
        string? branchName = null,
        string? parentId = null)
    {
        var feature = new Feature
        {
            ProjectId = projectId,
            ParentId = parentId,
            Title = title,
            Description = description,
            BranchName = branchName,
            Status = FeatureStatus.Future
        };

        _db.Features.Add(feature);
        await _db.SaveChangesAsync();
        return feature;
    }

    public async Task<Feature?> UpdateAsync(
        string id,
        string title,
        string? description,
        string? branchName,
        FeatureStatus status)
    {
        var feature = await _db.Features.FindAsync(id);
        if (feature == null) return null;

        feature.Title = title;
        feature.Description = description;
        feature.BranchName = branchName;
        feature.Status = status;
        feature.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return feature;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var feature = await _db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (feature == null) return false;

        // Clean up worktree if exists
        if (!string.IsNullOrEmpty(feature.WorktreePath))
        {
            await _worktreeService.RemoveWorktreeAsync(feature.Project.LocalPath, feature.WorktreePath);
        }

        _db.Features.Remove(feature);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> StartDevelopmentAsync(string id)
    {
        var feature = await _db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (feature == null) return false;
        if (string.IsNullOrEmpty(feature.BranchName)) return false;

        // Create worktree for the feature
        var worktreePath = await _worktreeService.CreateWorktreeAsync(
            feature.Project.LocalPath,
            feature.BranchName,
            createBranch: true,
            baseBranch: feature.Project.DefaultBranch);

        if (worktreePath == null) return false;

        feature.WorktreePath = worktreePath;
        feature.Status = FeatureStatus.InDevelopment;
        feature.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkReadyForReviewAsync(string id)
    {
        var feature = await _db.Features.FindAsync(id);
        if (feature == null) return false;

        feature.Status = FeatureStatus.ReadyForReview;
        feature.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CompleteAsync(string id, bool merged)
    {
        var feature = await _db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (feature == null) return false;

        // Clean up worktree
        if (!string.IsNullOrEmpty(feature.WorktreePath))
        {
            await _worktreeService.RemoveWorktreeAsync(feature.Project.LocalPath, feature.WorktreePath);
            feature.WorktreePath = null;
        }

        feature.Status = merged ? FeatureStatus.Merged : FeatureStatus.Cancelled;
        feature.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetParentAsync(string id, string? parentId)
    {
        var feature = await _db.Features.FindAsync(id);
        if (feature == null) return false;

        // Prevent circular references
        if (parentId != null)
        {
            var parent = await _db.Features.FindAsync(parentId);
            if (parent == null) return false;

            // Check if setting this parent would create a cycle
            var currentParent = parent;
            while (currentParent != null)
            {
                if (currentParent.Id == id) return false;
                currentParent = currentParent.ParentId != null
                    ? await _db.Features.FindAsync(currentParent.ParentId)
                    : null;
            }
        }

        feature.ParentId = parentId;
        feature.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task CleanupStalWorktreesAsync(string projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return;

        await _worktreeService.PruneWorktreesAsync(project.LocalPath);
    }
}
