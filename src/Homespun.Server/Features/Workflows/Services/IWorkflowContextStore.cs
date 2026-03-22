using System.Text.Json;
using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Stored context model for workflow execution persistence.
/// </summary>
public class StoredWorkflowContext
{
    /// <summary>
    /// Unique identifier for this execution.
    /// </summary>
    public required string ExecutionId { get; set; }

    /// <summary>
    /// The workflow definition ID.
    /// </summary>
    public required string WorkflowId { get; set; }

    /// <summary>
    /// The working directory for this execution.
    /// </summary>
    public required string WorkingDirectory { get; set; }

    /// <summary>
    /// Trigger data that initiated this workflow.
    /// </summary>
    public JsonElement TriggerData { get; set; }

    /// <summary>
    /// Output data from each completed node, keyed by node ID.
    /// </summary>
    public Dictionary<string, NodeOutput> NodeOutputs { get; set; } = [];

    /// <summary>
    /// User-defined variables stored during execution.
    /// </summary>
    public Dictionary<string, JsonElement> Variables { get; set; } = [];

    /// <summary>
    /// Artifacts produced during execution.
    /// </summary>
    public List<WorkflowArtifact> Artifacts { get; set; } = [];

    /// <summary>
    /// When the context was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the context was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents an artifact produced during workflow execution.
/// </summary>
public class WorkflowArtifact
{
    /// <summary>
    /// Name of the artifact.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Path to the artifact.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Type of artifact (e.g., "file", "log", "report").
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Size in bytes, if applicable.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// MIME type, if applicable.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// When the artifact was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional metadata about the artifact.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Service for storing and retrieving workflow execution context.
/// Provides persistence with crash recovery through JSON file storage.
/// </summary>
public interface IWorkflowContextStore : IDisposable
{
    /// <summary>
    /// Initializes a new context for a workflow execution.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">Unique identifier for the execution.</param>
    /// <param name="workflowId">The workflow definition ID.</param>
    /// <param name="workingDirectory">Working directory for the execution.</param>
    /// <param name="triggerData">Initial trigger data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The initialized context.</returns>
    Task<StoredWorkflowContext> InitializeContextAsync(
        string projectPath,
        string executionId,
        string workflowId,
        string workingDirectory,
        JsonElement triggerData,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the full context for an execution.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The context, or null if not found.</returns>
    Task<StoredWorkflowContext?> GetContextAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Sets a context variable.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The variable value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false if context not found or key is invalid.</returns>
    Task<bool> SetValueAsync(
        string projectPath,
        string executionId,
        string key,
        JsonElement value,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a specific variable value.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="key">The variable key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The value, or null if not found.</returns>
    Task<JsonElement?> GetValueAsync(
        string projectPath,
        string executionId,
        string key,
        CancellationToken ct = default);

    /// <summary>
    /// Stores output from a completed node.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="nodeId">The node ID.</param>
    /// <param name="output">The node output.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false if context not found or nodeId is invalid.</returns>
    Task<bool> MergeNodeOutputAsync(
        string projectPath,
        string executionId,
        string nodeId,
        NodeOutput output,
        CancellationToken ct = default);

    /// <summary>
    /// Adds an artifact to the context.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="artifact">The artifact to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false if context not found.</returns>
    Task<bool> AddArtifactAsync(
        string projectPath,
        string executionId,
        WorkflowArtifact artifact,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a context and its file.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteContextAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);
}
