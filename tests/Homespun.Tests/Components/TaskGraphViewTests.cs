using Bunit;
using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Gitgraph;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

[TestFixture]
public class TaskGraphViewTests : BunitTestContext
{
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
    public void Renders_ConnectorRow_AsNonClickable()
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

        var connectorRow = cut.Find(".task-graph-connector-row");
        Assert.That(connectorRow.GetAttribute("aria-hidden"), Is.EqualTo("true"));
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
            TotalLanes = 1
        };
        var agentStatuses = new Dictionary<string, AgentStatusData>(StringComparer.OrdinalIgnoreCase)
        {
            ["TEST-001"] = new AgentStatusData { IsActive = true, Status = "Running", SessionId = "session-1" }
        };

        var cut = Render<TaskGraphView>(p =>
        {
            p.Add(x => x.TaskGraph, taskGraph);
            p.Add(x => x.AgentStatuses, agentStatuses);
        });

        var badge = cut.Find(".agent-status-badge");
        Assert.That(badge.TextContent, Does.Contain("Running"));
        cut.Find(".agent-status-dot-active");
    }

    [Test]
    public void Renders_GroupSeparator()
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

        cut.Find(".task-graph-separator");
    }
}
