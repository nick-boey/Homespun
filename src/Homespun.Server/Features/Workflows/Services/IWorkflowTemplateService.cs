using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Summary of a workflow template for listing.
/// </summary>
public class WorkflowTemplateSummary
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public int StepCount { get; set; }
}

/// <summary>
/// Service for managing built-in workflow templates.
/// Templates are defined in code and shipped with the application.
/// </summary>
public interface IWorkflowTemplateService
{
    /// <summary>
    /// Returns a list of all available workflow templates.
    /// </summary>
    IReadOnlyList<WorkflowTemplateSummary> GetTemplates();

    /// <summary>
    /// Returns a specific template by ID, or null if not found.
    /// </summary>
    WorkflowDefinition? GetTemplate(string templateId);

    /// <summary>
    /// Creates a WorkflowDefinition from a template, bound to a specific project.
    /// </summary>
    WorkflowDefinition? CreateWorkflowFromTemplate(string templateId, string projectId);
}
