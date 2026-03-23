using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Models.Workflows;

namespace Homespun.Tests.Features.Workflows;

/// <summary>
/// Unit tests for WorkflowTemplateService.
/// </summary>
[TestFixture]
public class WorkflowTemplateServiceTests
{
    private WorkflowTemplateService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new WorkflowTemplateService();
    }

    #region GetTemplates Tests

    [Test]
    public void GetTemplates_ReturnsNonEmptyList()
    {
        var templates = _service.GetTemplates();

        Assert.That(templates, Is.Not.Empty);
    }

    [Test]
    public void GetTemplates_ContainsDefaultTemplate()
    {
        var templates = _service.GetTemplates();

        Assert.That(templates, Has.Exactly(1).Matches<WorkflowTemplateSummary>(
            t => t.Id == "default-verify-implement-review-merge"));
    }

    [Test]
    public void GetTemplates_DefaultTemplateHasCorrectStepCount()
    {
        var templates = _service.GetTemplates();
        var defaultTemplate = templates.First(t => t.Id == "default-verify-implement-review-merge");

        Assert.That(defaultTemplate.StepCount, Is.EqualTo(5));
    }

    [Test]
    public void GetTemplates_DefaultTemplateHasTitleAndDescription()
    {
        var templates = _service.GetTemplates();
        var defaultTemplate = templates.First(t => t.Id == "default-verify-implement-review-merge");

        Assert.That(defaultTemplate.Title, Is.Not.Null.And.Not.Empty);
        Assert.That(defaultTemplate.Description, Is.Not.Null.And.Not.Empty);
    }

    #endregion

    #region GetTemplate Tests

    [Test]
    public void GetTemplate_ReturnsWorkflowDefinition_ForValidId()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge");

        Assert.That(workflow, Is.Not.Null);
    }

    [Test]
    public void GetTemplate_ReturnsNull_ForInvalidId()
    {
        var workflow = _service.GetTemplate("nonexistent");

        Assert.That(workflow, Is.Null);
    }

    [Test]
    public void GetTemplate_HasAllRequiredFields()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;

        Assert.That(workflow.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(workflow.Title, Is.Not.Null.And.Not.Empty);
        Assert.That(workflow.Steps, Has.Count.EqualTo(5));
        Assert.That(workflow.Enabled, Is.True);
    }

    [Test]
    public void GetTemplate_HasProjectIdSetToTemplate()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;

        Assert.That(workflow.ProjectId, Is.EqualTo("template"));
    }

    #endregion

    #region Step Structure Tests

    [Test]
    public void GetTemplate_Step1_IsVerifyPlan()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[0];

        Assert.That(step.Id, Is.EqualTo("verify"));
        Assert.That(step.Name, Is.EqualTo("Verify Plan"));
        Assert.That(step.StepType, Is.EqualTo(WorkflowStepType.Agent));
        Assert.That(step.SessionMode, Is.EqualTo(SessionMode.Plan));
        Assert.That(step.OnSuccess.Type, Is.EqualTo(StepTransitionType.NextStep));
        Assert.That(step.OnFailure.Type, Is.EqualTo(StepTransitionType.Exit));
    }

    [Test]
    public void GetTemplate_Step2_IsImplement()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[1];

        Assert.That(step.Id, Is.EqualTo("implement"));
        Assert.That(step.Name, Is.EqualTo("Implement"));
        Assert.That(step.StepType, Is.EqualTo(WorkflowStepType.Agent));
        Assert.That(step.SessionMode, Is.EqualTo(SessionMode.Build));
        Assert.That(step.OnSuccess.Type, Is.EqualTo(StepTransitionType.NextStep));
        Assert.That(step.OnFailure.Type, Is.EqualTo(StepTransitionType.Exit));
    }

    [Test]
    public void GetTemplate_Step3_IsArchitecturalReview()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[2];

        Assert.That(step.Id, Is.EqualTo("review"));
        Assert.That(step.Name, Is.EqualTo("Architectural Review"));
        Assert.That(step.StepType, Is.EqualTo(WorkflowStepType.Agent));
        Assert.That(step.SessionMode, Is.EqualTo(SessionMode.Plan));
        Assert.That(step.OnSuccess.Type, Is.EqualTo(StepTransitionType.NextStep));
        Assert.That(step.OnFailure.Type, Is.EqualTo(StepTransitionType.GoToStep));
        Assert.That(step.OnFailure.TargetStepId, Is.EqualTo("fix"));
    }

    [Test]
    public void GetTemplate_Step4_IsMerge()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[3];

        Assert.That(step.Id, Is.EqualTo("merge"));
        Assert.That(step.Name, Is.EqualTo("Merge When CI Passes"));
        Assert.That(step.StepType, Is.EqualTo(WorkflowStepType.ServerAction));
        Assert.That(step.OnSuccess.Type, Is.EqualTo(StepTransitionType.Exit));
        Assert.That(step.OnFailure.Type, Is.EqualTo(StepTransitionType.GoToStep));
        Assert.That(step.OnFailure.TargetStepId, Is.EqualTo("fix"));
    }

    [Test]
    public void GetTemplate_Step4_HasCiMergeConfig()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[3];

        Assert.That(step.Config, Is.Not.Null);
        var config = step.Config!.Value;
        Assert.That(config.GetProperty("actionType").GetString(), Is.EqualTo("ci_merge"));
        Assert.That(config.GetProperty("pollIntervalSeconds").GetInt32(), Is.EqualTo(60));
        Assert.That(config.GetProperty("timeoutMinutes").GetInt32(), Is.EqualTo(30));
        Assert.That(config.GetProperty("mergeStrategy").GetString(), Is.EqualTo("squash"));
    }

    [Test]
    public void GetTemplate_Step5_IsFixStep()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[4];

        Assert.That(step.Id, Is.EqualTo("fix"));
        Assert.That(step.Name, Is.EqualTo("Fix CI/Review Issues"));
        Assert.That(step.StepType, Is.EqualTo(WorkflowStepType.Agent));
        Assert.That(step.SessionMode, Is.EqualTo(SessionMode.Build));
        Assert.That(step.MaxRetries, Is.EqualTo(3));
        Assert.That(step.OnSuccess.Type, Is.EqualTo(StepTransitionType.GoToStep));
        Assert.That(step.OnSuccess.TargetStepId, Is.EqualTo("merge"));
        Assert.That(step.OnFailure.Type, Is.EqualTo(StepTransitionType.Exit));
    }

    #endregion

    #region Step Transition Validation Tests

    [Test]
    public void GetTemplate_AllGoToStepTransitions_ReferenceValidStepIds()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var stepIds = workflow.Steps.Select(s => s.Id).ToHashSet();

        foreach (var step in workflow.Steps)
        {
            if (step.OnSuccess.Type == StepTransitionType.GoToStep)
            {
                Assert.That(stepIds, Does.Contain(step.OnSuccess.TargetStepId),
                    $"Step '{step.Id}' OnSuccess references invalid step '{step.OnSuccess.TargetStepId}'");
            }
            if (step.OnFailure.Type == StepTransitionType.GoToStep)
            {
                Assert.That(stepIds, Does.Contain(step.OnFailure.TargetStepId),
                    $"Step '{step.Id}' OnFailure references invalid step '{step.OnFailure.TargetStepId}'");
            }
        }
    }

    [Test]
    public void GetTemplate_AllStepIds_AreUnique()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var stepIds = workflow.Steps.Select(s => s.Id).ToList();

        Assert.That(stepIds, Is.Unique);
    }

    #endregion

    #region Prompt Content Tests

    [Test]
    public void GetTemplate_VerifyStep_HasPromptContent()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[0];

        Assert.That(step.Prompt, Is.Not.Null.And.Not.Empty);
        Assert.That(step.Prompt, Does.Contain("workflow_signal"));
    }

    [Test]
    public void GetTemplate_ImplementStep_HasPromptContent()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[1];

        Assert.That(step.Prompt, Is.Not.Null.And.Not.Empty);
        Assert.That(step.Prompt, Does.Contain("workflow_signal"));
    }

    [Test]
    public void GetTemplate_ReviewStep_HasPromptContent()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[2];

        Assert.That(step.Prompt, Is.Not.Null.And.Not.Empty);
        Assert.That(step.Prompt, Does.Contain("workflow_signal"));
    }

    [Test]
    public void GetTemplate_FixStep_HasTemplateVariableReferences()
    {
        var workflow = _service.GetTemplate("default-verify-implement-review-merge")!;
        var step = workflow.Steps[4];

        Assert.That(step.Prompt, Is.Not.Null.And.Not.Empty);
        Assert.That(step.Prompt, Does.Contain("{{steps.merge.output.error}}"));
        Assert.That(step.Prompt, Does.Contain("{{steps.review.output.error}}"));
    }

    #endregion

    #region CreateWorkflowFromTemplate Tests

    [Test]
    public void CreateWorkflowFromTemplate_ReturnsWorkflow_ForValidTemplate()
    {
        var workflow = _service.CreateWorkflowFromTemplate("default-verify-implement-review-merge", "project-123");

        Assert.That(workflow, Is.Not.Null);
    }

    [Test]
    public void CreateWorkflowFromTemplate_SetsProjectId()
    {
        var workflow = _service.CreateWorkflowFromTemplate("default-verify-implement-review-merge", "project-123")!;

        Assert.That(workflow.ProjectId, Is.EqualTo("project-123"));
    }

    [Test]
    public void CreateWorkflowFromTemplate_GeneratesUniqueId()
    {
        var workflow1 = _service.CreateWorkflowFromTemplate("default-verify-implement-review-merge", "project-123")!;
        var workflow2 = _service.CreateWorkflowFromTemplate("default-verify-implement-review-merge", "project-123")!;

        Assert.That(workflow1.Id, Is.Not.EqualTo(workflow2.Id));
    }

    [Test]
    public void CreateWorkflowFromTemplate_ReturnsNull_ForInvalidTemplate()
    {
        var workflow = _service.CreateWorkflowFromTemplate("nonexistent", "project-123");

        Assert.That(workflow, Is.Null);
    }

    [Test]
    public void CreateWorkflowFromTemplate_CopiesAllSteps()
    {
        var workflow = _service.CreateWorkflowFromTemplate("default-verify-implement-review-merge", "project-123")!;

        Assert.That(workflow.Steps, Has.Count.EqualTo(5));
    }

    [Test]
    public void CreateWorkflowFromTemplate_SetsTimestamps()
    {
        var before = DateTime.UtcNow;
        var workflow = _service.CreateWorkflowFromTemplate("default-verify-implement-review-merge", "project-123")!;
        var after = DateTime.UtcNow;

        Assert.That(workflow.CreatedAt, Is.InRange(before, after));
        Assert.That(workflow.UpdatedAt, Is.InRange(before, after));
    }

    #endregion
}
