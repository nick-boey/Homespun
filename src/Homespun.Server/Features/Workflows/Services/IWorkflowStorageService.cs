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
    /// Initial nodes for the workflow.
    /// </summary>
    public List<WorkflowNode>? Nodes { get; set; }

    /// <summary>
    /// Initial edges for the workflow.
    /// </summary>
    public List<WorkflowEdge>? Edges { get; set; }

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
    /// Updated nodes.
    /// </summary>
    public List<WorkflowNode>? Nodes { get; set; }

    /// <summary>
    /// Updated edges.
    /// </summary>
    public List<WorkflowEdge>? Edges { get; set; }

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
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow, or null if not found.</returns>
    Task<WorkflowDefinition?> GetWorkflowAsync(string projectPath, string workflowId, CancellationToken ct = default);

    /// <summary>
    /// Lists all workflows for a project.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of workflows.</returns>
    Task<IReadOnlyList<WorkflowDefinition>> ListWorkflowsAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Creates a new workflow.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="createParams">Parameters for creating the workflow.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created workflow.</returns>
    Task<WorkflowDefinition> CreateWorkflowAsync(string projectPath, CreateWorkflowParams createParams, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing workflow.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="workflowId">The workflow ID to update.</param>
    /// <param name="updateParams">Parameters for updating the workflow.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated workflow, or null if not found.</returns>
    Task<WorkflowDefinition?> UpdateWorkflowAsync(string projectPath, string workflowId, UpdateWorkflowParams updateParams, CancellationToken ct = default);

    /// <summary>
    /// Deletes a workflow.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="workflowId">The workflow ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteWorkflowAsync(string projectPath, string workflowId, CancellationToken ct = default);

    /// <summary>
    /// Reloads workflows from disk, clearing the cache.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReloadFromDiskAsync(string projectPath, CancellationToken ct = default);
}
