using System.Text.Json;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Templates;

/// <summary>
/// Built-in workflow templates shipped with the application.
/// </summary>
public static class DefaultWorkflowTemplates
{
    /// <summary>
    /// Creates the standard verify-implement-review-merge workflow template.
    /// </summary>
    public static WorkflowDefinition CreateVerifyImplementReviewMerge() => new()
    {
        Id = "default-verify-implement-review-merge",
        ProjectId = "template",
        Title = "Verify, Implement, Review & Merge",
        Description = "Standard workflow that verifies the plan, implements it with TDD, performs architectural review, and merges when CI passes.",
        Enabled = true,
        Version = 1,
        Steps =
        [
            new WorkflowStep
            {
                Id = "verify",
                Name = "Verify Plan",
                StepType = WorkflowStepType.Agent,
                SessionMode = SessionMode.Plan,
                Prompt = "Review the issue plan for completeness, clarity, and feasibility. Verify all necessary details are present for implementation. Call workflow_signal with success if ready, or fail if the plan needs work.",
                OnSuccess = new StepTransition { Type = StepTransitionType.NextStep },
                OnFailure = new StepTransition { Type = StepTransitionType.Exit }
            },
            new WorkflowStep
            {
                Id = "implement",
                Name = "Implement",
                StepType = WorkflowStepType.Agent,
                SessionMode = SessionMode.Build,
                Prompt = "Implement the plan described in the issue. Follow TDD practices. Run tests. Create a PR when complete. Call workflow_signal with the PR number in data.",
                OnSuccess = new StepTransition { Type = StepTransitionType.NextStep },
                OnFailure = new StepTransition { Type = StepTransitionType.Exit }
            },
            new WorkflowStep
            {
                Id = "review",
                Name = "Architectural Review",
                StepType = WorkflowStepType.Agent,
                SessionMode = SessionMode.Plan,
                Prompt = "Review the PR diff for architectural issues, security concerns, and code quality. Call workflow_signal with success if approved, or fail with specific feedback.",
                OnSuccess = new StepTransition { Type = StepTransitionType.NextStep },
                OnFailure = new StepTransition
                {
                    Type = StepTransitionType.GoToStep,
                    TargetStepId = "fix"
                }
            },
            new WorkflowStep
            {
                Id = "merge",
                Name = "Merge When CI Passes",
                StepType = WorkflowStepType.ServerAction,
                Config = JsonSerializer.SerializeToElement(new
                {
                    actionType = "ci_merge",
                    pollIntervalSeconds = 60,
                    timeoutMinutes = 30,
                    mergeStrategy = "squash"
                }),
                OnSuccess = new StepTransition { Type = StepTransitionType.Exit },
                OnFailure = new StepTransition
                {
                    Type = StepTransitionType.GoToStep,
                    TargetStepId = "fix"
                }
            },
            new WorkflowStep
            {
                Id = "fix",
                Name = "Fix CI/Review Issues",
                StepType = WorkflowStepType.Agent,
                SessionMode = SessionMode.Build,
                Prompt = "Fix the issues identified in the previous step. The errors are: {{steps.merge.output.error}}{{steps.review.output.error}}. Push fixes and call workflow_signal when done.",
                MaxRetries = 3,
                OnSuccess = new StepTransition
                {
                    Type = StepTransitionType.GoToStep,
                    TargetStepId = "merge"
                },
                OnFailure = new StepTransition { Type = StepTransitionType.Exit }
            }
        ]
    };
}
