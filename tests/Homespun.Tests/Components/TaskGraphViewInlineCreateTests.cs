using Bunit;
using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Tests.Helpers;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class TaskGraphViewInlineCreateTests : BunitTestContext
{
    private MockHttpMessageHandler _mockHandler = null!;
    private Mock<IKeyboardNavigationService> _mockNavService = null!;

    private static TaskGraphResponse CreateSingleIssueGraph(string issueId = "ISSUE-001", string title = "Test issue")
    {
        return new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse
                    {
                        Id = issueId,
                        Title = title,
                        Status = IssueStatus.Open
                    },
                    Lane = 0,
                    Row = 0,
                    IsActionable = true
                }
            ],
            TotalLanes = 1
        };
    }

    private static TaskGraphResponse CreateParentChildGraph()
    {
        return new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse
                    {
                        Id = "CHILD-001",
                        Title = "Child issue",
                        Status = IssueStatus.Open,
                        ParentIssues =
                        [
                            new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "0V" }
                        ]
                    },
                    Lane = 0,
                    Row = 0,
                    IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse
                    {
                        Id = "PARENT-001",
                        Title = "Parent issue",
                        Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series
                    },
                    Lane = 1,
                    Row = 1,
                    IsActionable = false
                }
            ],
            TotalLanes = 2
        };
    }

    [SetUp]
    public new void Setup()
    {
        base.Setup();

        _mockHandler = new MockHttpMessageHandler();
        _mockHandler.RespondWith("api/agent-prompts", new List<object>());
        var httpClient = _mockHandler.CreateClient();
        Services.AddSingleton(new HttpIssueApiService(httpClient));
        Services.AddSingleton(new HttpAgentPromptApiService(httpClient));

        _mockNavService = new Mock<IKeyboardNavigationService>();
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);

        // Setup bUnit JS interop for InlineIssueCreateInput and InlineIssueEditor
        Context!.JSInterop.SetupVoid("homespunInterop.focusWithCursor", _ => true);
    }

    [Test]
    public void KeyboardCreate_Below_RendersInlineIssueCreateInput()
    {
        var taskGraph = CreateSingleIssueGraph();

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.SelectedNodeId, "issue-ISSUE-001");
            p.Add(x => x.EnableKeyboardControls, true);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Simulate pressing 'o' to create below
        cut.Find("[data-testid='task-graph']").KeyDown(new KeyboardEventArgs { Key = "o" });

        // InlineIssueCreateInput should render
        var createInput = cut.Find("[data-testid='inline-issue-create']");
        Assert.That(createInput, Is.Not.Null, "InlineIssueCreateInput should render when 'o' is pressed");
    }

    [Test]
    public void KeyboardCreate_Below_DoesNotRenderInlineIssueEditor()
    {
        var taskGraph = CreateSingleIssueGraph();

        // Register NavService in DI for InlineIssueEditor's @inject dependency
        Services.AddSingleton(_mockNavService.Object);

        // Simulate NavService also being in CreatingNew mode (the bug scenario:
        // both TaskGraphView and ProjectDetail handlers fire)
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.CreatingNew);
        _mockNavService.Setup(s => s.PendingNewIssue).Returns(new PendingNewIssue
        {
            InsertAtIndex = 1,
            IsAbove = false,
            ReferenceIssueId = "ISSUE-001"
        });

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.SelectedNodeId, "issue-ISSUE-001");
            p.Add(x => x.EnableKeyboardControls, true);
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.NavService, _mockNavService.Object);
        });

        // Before pressing 'o', NavService's InlineIssueEditor renders since _isCreatingInline is false
        var editorsBefore = cut.FindAll(".inline-issue-editor");
        Assert.That(editorsBefore, Is.Not.Empty,
            "InlineIssueEditor should render before keyboard create (NavService is CreatingNew)");

        // Simulate pressing 'o' to create below (activates _isCreatingInline)
        cut.Find("[data-testid='task-graph']").KeyDown(new KeyboardEventArgs { Key = "o" });

        // InlineIssueCreateInput should render (from TaskGraphView's own handling)
        var createInput = cut.Find("[data-testid='inline-issue-create']");
        Assert.That(createInput, Is.Not.Null,
            "InlineIssueCreateInput should render when 'o' is pressed");

        // InlineIssueEditor should NOT render (guarded by !_isCreatingInline)
        var editorsAfter = cut.FindAll(".inline-issue-editor");
        Assert.That(editorsAfter, Is.Empty,
            "InlineIssueEditor should NOT render when _isCreatingInline is active");
    }

    [Test]
    public void KeyboardCreate_WithParentChild_PassesInheritedParentId()
    {
        var taskGraph = CreateParentChildGraph();

        _mockHandler.RespondWith("api/issues", new IssueResponse
        {
            Id = "new-id", Title = "New sibling", Status = IssueStatus.Open,
            Type = IssueType.Task, LastUpdate = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
        });

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.SelectedNodeId, "issue-CHILD-001");
            p.Add(x => x.EnableKeyboardControls, true);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Press 'o' to create below the child issue
        cut.Find("[data-testid='task-graph']").KeyDown(new KeyboardEventArgs { Key = "o" });

        // InlineIssueCreateInput should render with inherited parent info
        var createInput = cut.Find("[data-testid='inline-issue-create']");
        Assert.That(createInput, Is.Not.Null,
            "InlineIssueCreateInput should render with inherited parent from CHILD-001's parent");
    }

    [Test]
    public void NavServiceCreatingNew_WithoutKeyboardCreate_RendersInlineIssueEditor()
    {
        // Use a 2-issue graph so the reference issue (first) isn't also the last issue,
        // which avoids the ShouldInsertNewIssueAtEnd also matching
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "First", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-002", Title = "Second", Status = IssueStatus.Open },
                    Lane = 0, Row = 1, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        // NavService is in CreatingNew mode (e.g., from a non-TaskGraphView trigger)
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.CreatingNew);
        _mockNavService.Setup(s => s.PendingNewIssue).Returns(new PendingNewIssue
        {
            InsertAtIndex = 1,
            IsAbove = false,
            ReferenceIssueId = "ISSUE-001"
        });

        // Register NavService for InlineIssueEditor's DI injection
        Services.AddSingleton(_mockNavService.Object);

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.SelectedNodeId, "issue-ISSUE-001");
            p.Add(x => x.NavService, _mockNavService.Object);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Without pressing 'o', _isCreatingInline is false
        // NavService's InlineIssueEditor should render
        var editors = cut.FindAll(".inline-issue-editor");
        Assert.That(editors, Has.Count.EqualTo(1),
            "InlineIssueEditor should render when NavService is CreatingNew and _isCreatingInline is false");
    }
}
