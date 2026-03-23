using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Parameters for creating a new workflow.
/// </summary>
public class CreateWorkflowParams
{
    /// <summary>
    /// The project ID this workflow belongs to.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The title of the workflow.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Optional description of the workflow.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Initial steps for the workflow.
    /// </summary>
    public List<WorkflowStep>? Steps { get; set; }

    /// <summary>
    /// Trigger configuration.
    /// </summary>
    public WorkflowTrigger? Trigger { get; set; }

    /// <summary>
    /// Workflow settings.
    /// </summary>
    public WorkflowSettings? Settings { get; set; }

    /// <summary>
    /// Whether the workflow should be enabled. Defaults to true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// User who created the workflow.
    /// </summary>
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Parameters for updating an existing workflow.
/// </summary>
public class UpdateWorkflowParams
{
    /// <summary>
    /// Updated title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Updated description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Updated steps.
    /// </summary>
    public List<WorkflowStep>? Steps { get; set; }

    /// <summary>
    /// Updated trigger.
    /// </summary>
    public WorkflowTrigger? Trigger { get; set; }

    /// <summary>
    /// Updated settings.
    /// </summary>
    public WorkflowSettings? Settings { get; set; }

    /// <summary>
    /// Updated enabled state.
    /// </summary>
    public bool? Enabled { get; set; }
}

/// <summary>
/// Service for storing and retrieving workflow definitions.
/// Uses JSONL files in the .workflows directory for persistence.
/// </summary>
public interface IWorkflowStorageService : IDisposable
{
    /// <summary>
    /// Gets a single workflow by ID.
    /// </summary>
    Task<WorkflowDefinition?> GetWorkflowAsync(string projectPath, string workflowId, CancellationToken ct = default);

    /// <summary>
    /// Lists all workflows for a project.
    /// </summary>
    Task<IReadOnlyList<WorkflowDefinition>> ListWorkflowsAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Creates a new workflow.
    /// </summary>
    Task<WorkflowDefinition> CreateWorkflowAsync(string projectPath, CreateWorkflowParams createParams, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing workflow.
    /// </summary>
    Task<WorkflowDefinition?> UpdateWorkflowAsync(string projectPath, string workflowId, UpdateWorkflowParams updateParams, CancellationToken ct = default);

    /// <summary>
    /// Deletes a workflow.
    /// </summary>
    Task<bool> DeleteWorkflowAsync(string projectPath, string workflowId, CancellationToken ct = default);

    /// <summary>
    /// Reloads workflows from disk, clearing the cache.
    /// </summary>
    Task ReloadFromDiskAsync(string projectPath, CancellationToken ct = default);
}
