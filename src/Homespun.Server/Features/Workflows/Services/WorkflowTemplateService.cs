using Homespun.Features.Workflows.Templates;
using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Service for managing built-in workflow templates.
/// Templates are defined in code and shipped with the application.
/// </summary>
public class WorkflowTemplateService : IWorkflowTemplateService
{
    private readonly Dictionary<string, Func<WorkflowDefinition>> _templateFactories = new()
    {
        ["default-verify-implement-review-merge"] = DefaultWorkflowTemplates.CreateVerifyImplementReviewMerge
    };

    public IReadOnlyList<WorkflowTemplateSummary> GetTemplates()
    {
        return _templateFactories.Select(kvp =>
        {
            var template = kvp.Value();
            return new WorkflowTemplateSummary
            {
                Id = kvp.Key,
                Title = template.Title,
                Description = template.Description,
                StepCount = template.Steps.Count
            };
        }).ToList();
    }

    public WorkflowDefinition? GetTemplate(string templateId)
    {
        return _templateFactories.TryGetValue(templateId, out var factory) ? factory() : null;
    }

    public WorkflowDefinition? CreateWorkflowFromTemplate(string templateId, string projectId)
    {
        var template = GetTemplate(templateId);
        if (template is null)
            return null;

        template.Id = Guid.NewGuid().ToString("N")[..12];
        template.ProjectId = projectId;
        template.CreatedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        template.CreatedBy = "template";

        return template;
    }
}
