using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Tests.Components;

[TestFixture]
public class TaskGraphLayoutServiceTests
{
    [Test]
    public void ComputeLayout_EmptyNodes_ReturnsEmptyList()
    {
        var taskGraph = new TaskGraphResponse { Nodes = [], TotalLanes = 0 };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ComputeLayout_NullInput_ReturnsEmptyList()
    {
        var result = TaskGraphLayoutService.ComputeLayout(null);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ComputeLayout_SingleOrphan_ReturnsSingleIssueLine()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "TEST-001", Title = "Standalone", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.TypeOf<TaskGraphIssueRenderLine>());
        var line = (TaskGraphIssueRenderLine)result[0];
        Assert.That(line.IssueId, Is.EqualTo("TEST-001"));
        Assert.That(line.Title, Is.EqualTo("Standalone"));
        Assert.That(line.Lane, Is.EqualTo(0));
        Assert.That(line.ParentLane, Is.Null);
        Assert.That(line.DrawTopLine, Is.False);
        Assert.That(line.DrawBottomLine, Is.False);
        Assert.That(line.SeriesConnectorFromLane, Is.Null);
    }

    [Test]
    public void ComputeLayout_SingleOrphan_Actionable_CorrectMarker()
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

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var line = (TaskGraphIssueRenderLine)result[0];
        Assert.That(line.Marker, Is.EqualTo(TaskGraphMarkerType.Actionable));
    }

    [Test]
    public void ComputeLayout_ParentChild_ProducesIssueLines()
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

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        // No connector rows — only 2 issue lines
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0], Is.TypeOf<TaskGraphIssueRenderLine>());
        Assert.That(result[1], Is.TypeOf<TaskGraphIssueRenderLine>());
    }

    [Test]
    public void ComputeLayout_ParentChild_CorrectParentLane()
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

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var childLine = (TaskGraphIssueRenderLine)result[0];
        Assert.That(childLine.ParentLane, Is.EqualTo(1));
        Assert.That(childLine.IsFirstChild, Is.True);
    }

    [Test]
    public void ComputeLayout_ParallelChild_FollowedByNodeAtParentLane_DrawTopLine()
    {
        // When a parallel child's junction is at lane X, the next node at lane X should have DrawTopLine=true
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
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Parallel },
                    Lane = 1, Row = 1, IsActionable = false
                }
            ],
            TotalLanes = 2
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var parentLine = (TaskGraphIssueRenderLine)result[1];
        Assert.That(parentLine.DrawTopLine, Is.True,
            "Parent at same lane as child's junction should have DrawTopLine=true");
    }

    [Test]
    public void ComputeLayout_TwoSiblings_SeriesParent_IsSeriesChild()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "First child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Second child", Status = IssueStatus.Open,
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

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        // First child should be IsFirstChild=true and IsSeriesChild=true
        var firstChild = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "CHILD-A");
        Assert.That(firstChild.IsFirstChild, Is.True);
        Assert.That(firstChild.IsSeriesChild, Is.True);

        // Second child should be IsFirstChild=false and IsSeriesChild=true
        var secondChild = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "CHILD-B");
        Assert.That(secondChild.IsFirstChild, Is.False);
        Assert.That(secondChild.IsSeriesChild, Is.True);
    }

    [Test]
    public void ComputeLayout_TwoSiblings_SeriesParent_DrawingFlags()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "First child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Second child", Status = IssueStatus.Open,
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

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var firstChild = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "CHILD-A");
        Assert.That(firstChild.DrawTopLine, Is.False, "First series child has no top line");
        Assert.That(firstChild.DrawBottomLine, Is.True, "Series child always has bottom line");

        var secondChild = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "CHILD-B");
        Assert.That(secondChild.DrawTopLine, Is.True, "Second series sibling has top line (continuity)");
        Assert.That(secondChild.DrawBottomLine, Is.True, "Series child always has bottom line");

        var parent = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "PARENT-001");
        Assert.That(parent.SeriesConnectorFromLane, Is.EqualTo(0),
            "Parent receiving series children should have SeriesConnectorFromLane set to child lane");
    }

    [Test]
    public void ComputeLayout_TwoSiblings_ParallelParent_NotSeriesChild()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "First child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Second child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Parallel },
                    Lane = 1, Row = 2, IsActionable = false
                }
            ],
            TotalLanes = 2
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var firstChild = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "CHILD-A");
        Assert.That(firstChild.IsSeriesChild, Is.False);
        Assert.That(firstChild.DrawBottomLine, Is.False);

        var secondChild = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "CHILD-B");
        Assert.That(secondChild.IsSeriesChild, Is.False);
        Assert.That(secondChild.DrawBottomLine, Is.False);

        var parent = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "PARENT-001");
        Assert.That(parent.SeriesConnectorFromLane, Is.Null);
    }

    [Test]
    public void ComputeLayout_SeriesParent_SingleChild_IsSeriesChild()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "Only child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 1, Row = 1, IsActionable = false
                }
            ],
            TotalLanes = 2
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var child = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "CHILD-A");
        Assert.That(child.IsSeriesChild, Is.True, "Single child of series parent is a series child");
        Assert.That(child.DrawBottomLine, Is.True, "Series child has bottom line");

        var parent = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "PARENT-001");
        Assert.That(parent.SeriesConnectorFromLane, Is.EqualTo(0), "Parent receives L-shaped connector from child lane");
    }

    [Test]
    public void ComputeLayout_SeriesParentWithParallelConnector_HasBothConnectors()
    {
        // ISSUE-005 is a series parent receiving children AND connects horizontally to ISSUE-004
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "First child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Second child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Series parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "GRANDPARENT" }] },
                    Lane = 1, Row = 2, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "GRANDPARENT", Title = "Grandparent", Status = IssueStatus.Open },
                    Lane = 2, Row = 3, IsActionable = false
                }
            ],
            TotalLanes = 3
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var parent = result.OfType<TaskGraphIssueRenderLine>().First(l => l.IssueId == "PARENT-001");
        Assert.That(parent.SeriesConnectorFromLane, Is.EqualTo(0), "Series connector from child lane");
        Assert.That(parent.ParentLane, Is.EqualTo(2), "Also has parallel connector to grandparent");
    }

    [Test]
    public void ComputeLayout_DisconnectedGroups_NoSeparators()
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

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        // Two orphans = two disconnected groups, no separators between them
        Assert.That(result.Count(l => l is TaskGraphSeparatorRenderLine), Is.EqualTo(0));
        Assert.That(result.Count(l => l is TaskGraphIssueRenderLine), Is.EqualTo(2));
    }

    [Test]
    public void ComputeLayout_ThreeNodeChain_StaircasePattern()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "LEAF-001", Title = "Leaf", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "MID-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "MID-001", Title = "Middle", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ROOT-001" }] },
                    Lane = 1, Row = 1, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ROOT-001", Title = "Root", Status = IssueStatus.Open },
                    Lane = 2, Row = 2, IsActionable = false
                }
            ],
            TotalLanes = 3
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        // Only issue lines, no connector rows
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();
        Assert.That(issueLines, Has.Count.EqualTo(3));
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(issueLines[0].Lane, Is.EqualTo(0)); // Leaf at lane 0
        Assert.That(issueLines[1].Lane, Is.EqualTo(1)); // Mid at lane 1
        Assert.That(issueLines[2].Lane, Is.EqualTo(2)); // Root at lane 2
    }

    [Test]
    public void ComputeLayout_CompletedIssue_CompleteMarker()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "DONE-001", Title = "Done", Status = IssueStatus.Complete },
                    Lane = 0, Row = 0, IsActionable = false
                }
            ],
            TotalLanes = 1
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var line = (TaskGraphIssueRenderLine)result[0];
        Assert.That(line.Marker, Is.EqualTo(TaskGraphMarkerType.Complete));
    }

    [Test]
    public void ComputeLayout_ClosedIssue_ClosedMarker()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CLOSED-001", Title = "Closed", Status = IssueStatus.Closed },
                    Lane = 0, Row = 0, IsActionable = false
                }
            ],
            TotalLanes = 1
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var line = (TaskGraphIssueRenderLine)result[0];
        Assert.That(line.Marker, Is.EqualTo(TaskGraphMarkerType.Closed));
    }

    [Test]
    public void ComputeLayout_FullMockData_CorrectLineCount()
    {
        // No connector rows, no separators — only issue lines
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var issueCount = result.Count(l => l is TaskGraphIssueRenderLine);
        var separatorCount = result.Count(l => l is TaskGraphSeparatorRenderLine);

        Assert.That(issueCount, Is.EqualTo(13), "Should have 13 issue lines");
        Assert.That(separatorCount, Is.EqualTo(0), "Should have no separator lines");
        Assert.That(result, Has.Count.EqualTo(13), "Total: 13 issues only");
    }

    /// <summary>
    /// Builds a TaskGraphResponse matching the expected output structure from
    /// TaskGraphTextRendererTests.Render_MockIssueData_MatchesExpectedOutput.
    /// This represents the pre-computed output from Fleece.Core's TaskGraphService.
    /// </summary>
    private static TaskGraphResponse BuildFullMockTaskGraph()
    {
        return new TaskGraphResponse
        {
            TotalLanes = 6,
            Nodes =
            [
                // Group 1: ISSUE-003 (orphan, progress status)
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-003", Title = "Fix login timeout bug", Status = IssueStatus.Progress },
                    Lane = 0, Row = 0, IsActionable = false
                },
                // Group 2: ISSUE-001 (orphan)
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Add dark mode support", Status = IssueStatus.Open },
                    Lane = 0, Row = 1, IsActionable = true
                },
                // Group 3: The big dependency tree (ISSUE-010 through ISSUE-004)
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-010", Title = "Implement DELETE endpoints", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-009" }] },
                    Lane = 0, Row = 2, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-009", Title = "Implement PUT/PATCH endpoints", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-008" }] },
                    Lane = 1, Row = 3, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-011", Title = "Add request validation", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-008" }] },
                    Lane = 1, Row = 4, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-008", Title = "Implement POST endpoints", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-007" }],
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 2, Row = 5, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-012", Title = "Add rate limiting", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-007" }] },
                    Lane = 2, Row = 6, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-007", Title = "Implement GET endpoints", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-005" }],
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 3, Row = 7, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-006", Title = "Write API documentation", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-005" }] },
                    Lane = 3, Row = 8, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-013", Title = "Set up API monitoring", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-005" }] },
                    Lane = 3, Row = 9, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-005", Title = "Implement API endpoints", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "ISSUE-004" }],
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 4, Row = 10, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-004", Title = "Design API schema", Status = IssueStatus.Open },
                    Lane = 5, Row = 11, IsActionable = false
                },
                // Group 4: ISSUE-002 (orphan)
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-002", Title = "Improve mobile responsiveness", Status = IssueStatus.Open },
                    Lane = 0, Row = 12, IsActionable = true
                }
            ]
        };
    }
}
