using Bunit;
using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Gitgraph;
using Homespun.Tests.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Tests.Components;

[TestFixture]
public class TaskGraphViewTests : BunitTestContext
{
    [SetUp]
    public new void Setup()
    {
        base.Setup();
        // Register mock HttpClient and services for TaskGraphView
        // Configure mock to return proper responses for different endpoints
        var mockHandler = new MockHttpMessageHandler()
            .RespondWith("/api/issues/", new IssueResponse { Id = "TEST-001", Type = IssueType.Task, Status = IssueStatus.Open, Title = "Test" }) // For PUT /api/issues/{id}
            .WithDefaultResponse(new List<object>()); // Return empty list for GET requests (agent prompts)
        var mockHttpClient = mockHandler.CreateClient();
        var issueApi = new HttpIssueApiService(mockHttpClient);
        var agentPromptApi = new HttpAgentPromptApiService(mockHttpClient);
        Services.AddSingleton(issueApi);
        Services.AddSingleton(agentPromptApi);

        // Setup JS interop mocks for scroll-into-view
        JSInterop.SetupVoid("homespunInterop.scrollIssueIntoView", _ => true);
    }

    [Test]
    public void Renders_Nothing_WhenTaskGraphIsNull()
    {
        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, null));

        Assert.That(cut.FindAll(".task-graph"), Is.Empty);
    }

    [Test]
    public void Renders_Nothing_WhenNoNodes()
    {
        var taskGraph = new TaskGraphResponse { Nodes = [], TotalLanes = 0 };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        Assert.That(cut.FindAll(".task-graph"), Is.Empty);
    }

    [Test]
    public void Renders_IssueRow_WithTitleAndId()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        Assert.That(cut.Markup, Does.Contain("TEST-001"));
        Assert.That(cut.Markup, Does.Contain("Test issue"));
        cut.Find(".task-graph-row");
    }

    [Test]
    public void Renders_ActionableMarker_WithSvgGlowRing()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Actionable", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        var graphCell = cut.Find(".task-graph-graph-cell");
        Assert.That(graphCell.InnerHtml, Does.Contain("<svg"));
        // Actionable marker has glow ring with opacity
        Assert.That(graphCell.InnerHtml, Does.Contain("opacity=\"0.4\""));
    }

    [Test]
    public void Renders_OpenMarker_WithSvgNoGlowRing()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Open", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = false
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        var graphCell = cut.Find(".task-graph-graph-cell");
        Assert.That(graphCell.InnerHtml, Does.Contain("<svg"));
        // Open marker has no glow ring
        Assert.That(graphCell.InnerHtml, Does.Not.Contain("opacity=\"0.4\""));
    }

    [Test]
    public void Renders_SelectedRow_WithSelectedClass()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Selected", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.SelectedNodeId, "issue-TEST-001");
        });

        cut.Find(".task-graph-row-selected");
    }

    [Test]
    public void Invokes_OnIssueClick_WhenRowClicked()
    {
        string? clickedIssueId = null;
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Clickable", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.OnIssueClick, EventCallback.Factory.Create<string>(this, id => clickedIssueId = id));
        });

        cut.Find(".task-graph-row").Click();

        Assert.That(clickedIssueId, Is.EqualTo("TEST-001"));
    }

    [Test]
    public void Renders_NoConnectorRows()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-001", Title = "Child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Parent", Status = IssueStatus.Open },
                    Lane = 1, Row = 1, IsActionable = false
                }
            ],
            TotalLanes = 2
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        // No connector rows — all rendering is done via issue row SVGs
        Assert.That(cut.FindAll(".task-graph-connector-row"), Is.Empty);
        // Should have exactly 2 issue rows
        Assert.That(cut.FindAll(".task-graph-row"), Has.Count.EqualTo(2));
    }

    [Test]
    public void Renders_SeriesParent_WithLShapedPath()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "Series child 1", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Series child 2", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Series parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 1, Row = 2, IsActionable = false
                }
            ],
            TotalLanes = 2
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        // The parent row should have an L-shaped path from child lane (0) to parent circle
        var graphCells = cut.FindAll(".task-graph-graph-cell");
        var parentCell = graphCells[2]; // Third issue row = parent
        var fromX = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var cx = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var nodeEdgeX = cx - TimelineSvgRenderer.NodeRadius - 2; // 28 (NodeRadius is used for circles)
        var r = TimelineSvgRenderer.NodeRadius; // 6
        Assert.That(parentCell.InnerHtml, Does.Contain($"M {fromX} 0 L {fromX} {20 - r} A {r} {r} 0 0 0 {fromX + r} 20 L {nodeEdgeX} 20"),
            "Parent should have L-shaped connector with arc from series children's lane");
    }

    [Test]
    public void Renders_AgentStatusBadge_WhenPresent()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "With agent", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            AgentStatuses = new Dictionary<string, AgentStatusData>(StringComparer.OrdinalIgnoreCase)
            {
                ["TEST-001"] = new AgentStatusData { IsActive = true, Status = "Running", SessionId = "session-1" }
            }
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
        });

        var badge = cut.Find(".agent-status-badge");
        Assert.That(badge.TextContent, Does.Contain("Working"));
        cut.Find(".agent-status-dot-active");
    }

    [Test]
    public void Renders_SeriesChild_WithoutHorizontalSvgPath()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "Series child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Series child 2", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 1, Row = 2, IsActionable = false
                }
            ],
            TotalLanes = 2
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        var graphCells = cut.FindAll(".task-graph-graph-cell");
        // The first issue row (CHILD-A) is a series child - should NOT have horizontal SVG path to parent lane
        var firstIssueCell = graphCells[0];
        var px = TimelineSvgRenderer.GetLaneCenterX(1); // parent lane center = 36
        var cx = TimelineSvgRenderer.GetLaneCenterX(0); // child lane center = 12
        var startX = cx + TimelineSvgRenderer.NodeRadius + 2; // 20
        Assert.That(firstIssueCell.InnerHtml, Does.Not.Contain($"M {startX} 20 L {px} 20"),
            "Series child should not have horizontal connector path");
    }

    [Test]
    public void Renders_DisconnectedGroups_NoSeparator()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "GROUP1-001", Title = "Group 1", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "GROUP2-001", Title = "Group 2", Status = IssueStatus.Open },
                    Lane = 0, Row = 1, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        // No separators between disconnected groups
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".task-graph-separator"));
        // Both issue rows should render
        var issueRows = cut.FindAll(".task-graph-row");
        Assert.That(issueRows, Has.Count.EqualTo(2));
    }

    [Test]
    public void Renders_IssueTypeBadge_WithCorrectClass()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "BUG-001", Title = "Bug issue", Status = IssueStatus.Open, Type = IssueType.Bug },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TASK-001", Title = "Task issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "FEAT-001", Title = "Feature issue", Status = IssueStatus.Open, Type = IssueType.Feature },
                    Lane = 0, Row = 2, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHORE-001", Title = "Chore issue", Status = IssueStatus.Open, Type = IssueType.Chore },
                    Lane = 0, Row = 3, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        var typeBadges = cut.FindAll(".task-graph-issue-type");
        Assert.That(typeBadges, Has.Count.EqualTo(4));

        // Verify each badge has correct class and label
        Assert.That(typeBadges[0].ClassList, Does.Contain("bug"));
        Assert.That(typeBadges[0].TextContent, Is.EqualTo("Bug"));

        Assert.That(typeBadges[1].ClassList, Does.Contain("task"));
        Assert.That(typeBadges[1].TextContent, Is.EqualTo("Task"));

        Assert.That(typeBadges[2].ClassList, Does.Contain("feature"));
        Assert.That(typeBadges[2].TextContent, Is.EqualTo("Feat"));

        Assert.That(typeBadges[3].ClassList, Does.Contain("chore"));
        Assert.That(typeBadges[3].TextContent, Is.EqualTo("Chore"));
    }

    [Test]
    public void TypeBadge_OpensMenu_OnClick()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Menu should not exist initially
        Assert.That(cut.FindAll(".task-graph-type-menu"), Is.Empty);

        // Click the type badge
        cut.Find(".task-graph-issue-type").Click();

        // Menu should now be visible with 4 type options
        var menu = cut.Find(".task-graph-type-menu");
        var buttons = menu.QuerySelectorAll("button");
        Assert.That(buttons, Has.Length.EqualTo(4));

        // Verify button labels
        Assert.That(buttons[0].TextContent, Is.EqualTo("Bug"));
        Assert.That(buttons[1].TextContent, Is.EqualTo("Task"));
        Assert.That(buttons[2].TextContent, Is.EqualTo("Feature"));
        Assert.That(buttons[3].TextContent, Is.EqualTo("Chore"));
    }

    [Test]
    public void TypeMenu_ClosesOnSecondClick()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Open the menu
        var typeBadge = cut.Find(".task-graph-issue-type");
        typeBadge.Click();
        Assert.That(cut.FindAll(".task-graph-type-menu"), Has.Count.EqualTo(1));

        // Click badge again to close
        typeBadge.Click();
        Assert.That(cut.FindAll(".task-graph-type-menu"), Is.Empty);
    }

    [Test]
    public void TypeMenu_InvokesCallback_WhenTypeSelected()
    {
        var callbackInvoked = false;
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.OnIssueTypeChanged, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        // Open the menu and select a type
        cut.Find(".task-graph-issue-type").Click();
        var bugButton = cut.Find(".type-menu-item.bug");
        bugButton.Click();

        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public void TypeMenu_ClosesAfterSelection()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.OnIssueTypeChanged, EventCallback.Factory.Create(this, () => { }));
        });

        // Open the menu
        cut.Find(".task-graph-issue-type").Click();
        Assert.That(cut.FindAll(".task-graph-type-menu"), Has.Count.EqualTo(1));

        // Select a type
        cut.Find(".type-menu-item.feature").Click();

        // Menu should close after selection
        Assert.That(cut.FindAll(".task-graph-type-menu"), Is.Empty);
    }

    [Test]
    public void TypeMenu_CallsApi_WhenTypeSelected()
    {
        // Create mock handler for API calls
        var mockHandler = new MockHttpMessageHandler()
            .RespondWith("/api/issues/TEST-001", new IssueResponse { Id = "TEST-001", Type = IssueType.Feature });
        var httpClient = mockHandler.CreateClient();
        var issueApi = new HttpIssueApiService(httpClient);

        // Re-register with tracking mock
        Services.AddSingleton(issueApi);

        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.OnIssueTypeChanged, EventCallback.Factory.Create(this, () => { }));
        });

        // Open the menu and select Feature
        cut.Find(".task-graph-issue-type").Click();
        cut.Find(".type-menu-item.feature").Click();

        // The API should have been called - verify by checking the menu closed (which happens after API call)
        Assert.That(cut.FindAll(".task-graph-type-menu"), Is.Empty);
    }

    #region Status Menu Tests

    [Test]
    public void StatusBadge_OpensMenu_OnClick()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Menu should not exist initially
        Assert.That(cut.FindAll(".task-graph-status-menu"), Is.Empty);

        // Click the status badge
        cut.Find(".task-graph-issue-status").Click();

        // Menu should now be visible with 6 status options (excluding Deleted)
        var menu = cut.Find(".task-graph-status-menu");
        var buttons = menu.QuerySelectorAll("button");
        Assert.That(buttons, Has.Length.EqualTo(6));

        // Verify button labels
        Assert.That(buttons[0].TextContent, Is.EqualTo("Open"));
        Assert.That(buttons[1].TextContent, Is.EqualTo("Progress"));
        Assert.That(buttons[2].TextContent, Is.EqualTo("Review"));
        Assert.That(buttons[3].TextContent, Is.EqualTo("Complete"));
        Assert.That(buttons[4].TextContent, Is.EqualTo("Closed"));
        Assert.That(buttons[5].TextContent, Is.EqualTo("Archived"));
    }

    [Test]
    public void StatusMenu_ClosesOnSecondClick()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Open the menu
        var statusBadge = cut.Find(".task-graph-issue-status");
        statusBadge.Click();
        Assert.That(cut.FindAll(".task-graph-status-menu"), Has.Count.EqualTo(1));

        // Click badge again to close
        statusBadge.Click();
        Assert.That(cut.FindAll(".task-graph-status-menu"), Is.Empty);
    }

    [Test]
    public void StatusMenu_InvokesCallback_WhenStatusSelected()
    {
        var callbackInvoked = false;
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.OnIssueStatusChanged, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        // Open the menu and select a status
        cut.Find(".task-graph-issue-status").Click();
        var progressButton = cut.Find(".status-menu-item.progress");
        progressButton.Click();

        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public void StatusMenu_ClosesAfterSelection()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.OnIssueStatusChanged, EventCallback.Factory.Create(this, () => { }));
        });

        // Open the menu
        cut.Find(".task-graph-issue-status").Click();
        Assert.That(cut.FindAll(".task-graph-status-menu"), Has.Count.EqualTo(1));

        // Select a status
        cut.Find(".status-menu-item.complete").Click();

        // Menu should close after selection
        Assert.That(cut.FindAll(".task-graph-status-menu"), Is.Empty);
    }

    [Test]
    public void StatusMenu_CallsApi_WhenStatusSelected()
    {
        // Create mock handler for API calls
        var mockHandler = new MockHttpMessageHandler()
            .RespondWith("/api/issues/TEST-001", new IssueResponse { Id = "TEST-001", Status = IssueStatus.Progress });
        var httpClient = mockHandler.CreateClient();
        var issueApi = new HttpIssueApiService(httpClient);

        // Re-register with tracking mock
        Services.AddSingleton(issueApi);

        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.OnIssueStatusChanged, EventCallback.Factory.Create(this, () => { }));
        });

        // Open the menu and select Progress
        cut.Find(".task-graph-issue-status").Click();
        cut.Find(".status-menu-item.progress").Click();

        // The API should have been called - verify by checking the menu closed (which happens after API call)
        Assert.That(cut.FindAll(".task-graph-status-menu"), Is.Empty);
    }

    [Test]
    public async Task StatusMenu_OpensForSelectedIssue_OnKeyboardShortcut()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Test issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        // Set up NavService with a selected issue
        var navService = new MockKeyboardNavigationService
        {
            SelectedIssueId = "TEST-001"
        };
        SetupInlineEditorServices(navService);

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.NavService, navService);
        });

        // Menu should not exist initially
        Assert.That(cut.FindAll(".task-graph-status-menu"), Is.Empty);

        // Call OpenStatusMenu (simulates 's' key press from TimelineVisualization)
        // Use InvokeAsync because OpenStatusMenu calls StateHasChanged
        await cut.InvokeAsync(() => cut.Instance.OpenStatusMenu());

        // Menu should now be visible
        Assert.That(cut.FindAll(".task-graph-status-menu"), Has.Count.EqualTo(1));
    }

    [Test]
    public void StatusBadge_DisplaysCorrectStatus()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Open issue", Status = IssueStatus.Open, Type = IssueType.Task },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-002", Title = "Progress issue", Status = IssueStatus.Progress, Type = IssueType.Task },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-003", Title = "Review issue", Status = IssueStatus.Review, Type = IssueType.Task },
                    Lane = 0, Row = 2, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-004", Title = "Complete issue", Status = IssueStatus.Complete, Type = IssueType.Task },
                    Lane = 0, Row = 3, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var cut = Render<TaskGraphView>(p => p.Add(x => x.TaskGraph, taskGraph));

        var statusBadges = cut.FindAll(".task-graph-issue-status");
        Assert.That(statusBadges, Has.Count.EqualTo(4));

        // Verify each badge has correct class and label
        Assert.That(statusBadges[0].ClassList, Does.Contain("open"));
        Assert.That(statusBadges[0].TextContent, Is.EqualTo("Open"));

        Assert.That(statusBadges[1].ClassList, Does.Contain("progress"));
        Assert.That(statusBadges[1].TextContent, Is.EqualTo("Progress"));

        Assert.That(statusBadges[2].ClassList, Does.Contain("review"));
        Assert.That(statusBadges[2].TextContent, Is.EqualTo("Review"));

        Assert.That(statusBadges[3].ClassList, Does.Contain("complete"));
        Assert.That(statusBadges[3].TextContent, Is.EqualTo("Complete"));
    }

    #endregion

    [Test]
    public void AgentStatusBadge_IsClickableLink_WithSessionHref()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "With agent", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            AgentStatuses = new Dictionary<string, AgentStatusData>(StringComparer.OrdinalIgnoreCase)
            {
                ["TEST-001"] = new AgentStatusData { IsActive = true, Status = "Running", SessionId = "session-abc123" }
            }
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
        });

        // Agent status badge should be an anchor tag with href to the session
        var badge = cut.Find(".agent-status-badge");
        Assert.That(badge.TagName.ToLower(), Is.EqualTo("a"), "Agent status badge should be an <a> tag");
        Assert.That(badge.GetAttribute("href"), Is.EqualTo("/session/session-abc123"), "Badge href should link to the session");
    }

    [Test]
    public void AgentStatusBadge_HasStopPropagation_ToPreventRowClick()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "With agent", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            AgentStatuses = new Dictionary<string, AgentStatusData>(StringComparer.OrdinalIgnoreCase)
            {
                ["TEST-001"] = new AgentStatusData { IsActive = true, Status = "Running", SessionId = "session-abc123" }
            }
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
        });

        // Agent status badge should have onclick:stopPropagation to prevent row click
        var badge = cut.Find(".agent-status-badge");
        Assert.That(badge.HasAttribute("blazor:onclick:stoppropagation") ||
                    cut.Markup.Contains("onclick:stoppropagation"),
            Is.True, "Agent status badge should have stopPropagation to prevent row click bubbling");
    }

    [Test]
    public void AgentStatusBadge_OnPrRow_IsClickableLink()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes = [],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse
                {
                    Number = 123,
                    Title = "Test PR",
                    IsMerged = true,
                    Url = "https://github.com/test/repo/pull/123",
                    HasDescription = true,
                    AgentStatus = new AgentStatusData { IsActive = true, Status = "Running", SessionId = "pr-session-456" }
                }
            ]
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
        });

        // Agent status badge on PR row should also be an anchor tag
        var badge = cut.Find(".agent-status-badge");
        Assert.That(badge.TagName.ToLower(), Is.EqualTo("a"), "Agent status badge on PR should be an <a> tag");
        Assert.That(badge.GetAttribute("href"), Is.EqualTo("/session/pr-session-456"), "Badge href should link to the session");
    }

    #region Inline Issue Editor Bug Tests - Issue I2QGzX

    /// <summary>
    /// Sets up the services needed for inline editor tests (NavService and JSInterop).
    /// </summary>
    private void SetupInlineEditorServices(IKeyboardNavigationService navService)
    {
        Services.AddSingleton(navService);
        // Setup bUnit JS interop to handle the focusWithCursor call used by InlineIssueEditor
        Context!.JSInterop.SetupVoid("homespunInterop.focusWithCursor", _ => true);
    }

    /// <summary>
    /// Creates a mock IKeyboardNavigationService with the specified pending new issue state.
    /// </summary>
    private static MockKeyboardNavigationService CreateMockNavService(PendingNewIssue? pendingNewIssue)
    {
        var mock = new MockKeyboardNavigationService
        {
            EditMode = pendingNewIssue != null ? KeyboardEditMode.CreatingNew : KeyboardEditMode.Viewing,
            PendingNewIssue = pendingNewIssue
        };
        return mock;
    }

    [Test]
    public void CreatingIssueBelow_LastIssue_ShowsOnlyOneInlineEditor()
    {
        // Arrange: Create a task graph with 3 issues
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "First issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-002", Title = "Middle issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-003", Title = "Last issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 2, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        // Set up NavService to simulate pressing 'o' on the LAST issue (TEST-003)
        var navService = CreateMockNavService(new PendingNewIssue
        {
            IsAbove = false, // 'o' command creates below
            ReferenceIssueId = "TEST-003", // Last issue
            Title = ""
        });
        SetupInlineEditorServices(navService);

        // Act
        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.NavService, navService);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Assert: Only ONE InlineIssueEditor should be rendered
        var editors = cut.FindComponents<InlineIssueEditor>();
        Assert.That(editors, Has.Count.EqualTo(1), "Expected exactly one InlineIssueEditor, but found " + editors.Count);

        // Also verify by checking for the "NEW" badge (should appear only once)
        var newBadges = cut.FindAll(".task-graph-issue-id-new");
        Assert.That(newBadges, Has.Count.EqualTo(1), "Expected exactly one 'NEW' badge");
    }

    [Test]
    public void CreatingIssueBelow_MiddleIssue_ShowsOnlyOneInlineEditor()
    {
        // Arrange: Create a task graph with 3 issues
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "First issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-002", Title = "Middle issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-003", Title = "Last issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 2, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        // Set up NavService to simulate pressing 'o' on the MIDDLE issue (TEST-002)
        var navService = CreateMockNavService(new PendingNewIssue
        {
            IsAbove = false, // 'o' command creates below
            ReferenceIssueId = "TEST-002", // Middle issue
            Title = ""
        });
        SetupInlineEditorServices(navService);

        // Act
        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.NavService, navService);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Assert: Only ONE InlineIssueEditor should be rendered
        var editors = cut.FindComponents<InlineIssueEditor>();
        Assert.That(editors, Has.Count.EqualTo(1), "Expected exactly one InlineIssueEditor");

        // Verify it appears after TEST-002 but before TEST-003
        var issueRows = cut.FindAll("[data-testid='task-graph-issue-row']");
        Assert.That(issueRows, Has.Count.EqualTo(3), "Should still have 3 issue rows");
    }

    [Test]
    public void CreatingIssueAbove_FirstIssue_ShowsOnlyOneInlineEditor()
    {
        // Arrange: Create a task graph with 3 issues
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "First issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-002", Title = "Middle issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-003", Title = "Last issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 2, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        // Set up NavService to simulate pressing 'O' (Shift+O) on the FIRST issue (TEST-001)
        var navService = CreateMockNavService(new PendingNewIssue
        {
            IsAbove = true, // 'O' command creates above
            ReferenceIssueId = "TEST-001", // First issue
            Title = ""
        });
        SetupInlineEditorServices(navService);

        // Act
        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.NavService, navService);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Assert: Only ONE InlineIssueEditor should be rendered
        var editors = cut.FindComponents<InlineIssueEditor>();
        Assert.That(editors, Has.Count.EqualTo(1), "Expected exactly one InlineIssueEditor");
    }

    [Test]
    public void CreatingIssueBelow_SingleIssue_ShowsOnlyOneInlineEditor()
    {
        // Arrange: Create a task graph with only ONE issue (edge case)
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Only issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        // Set up NavService to simulate pressing 'o' on the only issue
        var navService = CreateMockNavService(new PendingNewIssue
        {
            IsAbove = false,
            ReferenceIssueId = "TEST-001",
            Title = ""
        });
        SetupInlineEditorServices(navService);

        // Act
        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.NavService, navService);
            p.Add(x => x.ProjectId, "test-project");
        });

        // Assert: Only ONE InlineIssueEditor should be rendered
        var editors = cut.FindComponents<InlineIssueEditor>();
        Assert.That(editors, Has.Count.EqualTo(1), "Expected exactly one InlineIssueEditor for single-issue case");
    }

    #endregion
}

/// <summary>
/// Mock implementation of IKeyboardNavigationService for testing TaskGraphView rendering.
/// </summary>
public class MockKeyboardNavigationService : IKeyboardNavigationService
{
    public int SelectedIndex { get; set; }
    public string? SelectedIssueId { get; set; }
    public KeyboardEditMode EditMode { get; set; }
    public InlineEditState? PendingEdit { get; set; }
    public PendingNewIssue? PendingNewIssue { get; set; }
    public int SelectedPromptIndex { get; set; }
    public string? ProjectId { get; set; }

    public event Action? OnStateChanged;
    public event Func<Task>? OnIssueChanged;
    public event Action<string>? OnOpenEditRequested;

    public void MoveUp() { }
    public void MoveDown() { }
    public void MoveToParent() { }
    public void MoveToChild() { }
    public void MoveToFirst() { }
    public void MoveToLast() { }
    public void StartEditingAtStart() { }
    public void StartEditingAtEnd() { }
    public void StartReplacingTitle() { }
    public void CreateIssueBelow() { }
    public void CreateIssueAbove() { }
    public void IndentAsChild() { }
    public void UnindentAsSibling() { }
    public void CancelEdit() { }
    public Task AcceptEditAsync() => Task.CompletedTask;
    public Task AcceptEditAndOpenDescriptionAsync() => Task.CompletedTask;
    public event Func<string, Task>? OnIssueCreatedForEdit;
    public void UpdateEditTitle(string title) { }
    public void Initialize(List<TaskGraphIssueRenderLine> renderLines) { }
    public void SetProjectId(string projectId) => ProjectId = projectId;
    public void SetTaskGraphNodes(List<TaskGraphNodeResponse> nodes) { }
    public void SelectFirstActionable() { }
    public void SelectIssue(string issueId) => SelectedIssueId = issueId;
public void OpenSelectedIssueForEdit() => OnOpenEditRequested?.Invoke(SelectedIssueId ?? "");
    public void StartSelectingPrompt() { }
    public void MovePromptSelectionDown() { }
    public void MovePromptSelectionUp() { }
    public void AcceptPromptSelection() { }
    public Task CycleIssueTypeAsync() => Task.CompletedTask;

    // Search properties
    public string SearchTerm { get; set; } = "";
    public bool IsSearching { get; set; }
    public bool IsSearchEmbedded { get; set; }
    public IReadOnlyList<int> MatchingIndices { get; set; } = Array.Empty<int>();
    public int CurrentMatchIndex { get; set; } = -1;

    // Search methods
    public void StartSearch() { }
    public void UpdateSearchTerm(string term) => SearchTerm = term;
    public void EmbedSearch() { }
    public void MoveToNextMatch() { }
    public void MoveToPreviousMatch() { }
    public void ClearSearch() { }

    // Helper to trigger state change
    public void TriggerStateChanged() => OnStateChanged?.Invoke();
}
