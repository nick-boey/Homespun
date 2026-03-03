using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Features.Issues.Components;
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
        // Use maxDepth=int.MaxValue to show all nodes without filtering
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: int.MaxValue);

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
    [Test]
    public void ComputeLayout_MergedPrs_IssueLanesOffsetByOne()
    {
        // When merged PRs exist, issue nodes should have their lanes offset by +1
        // to make room for the vertical line in lane 0 connecting PRs to issues.
        // Connected issues in the same group maintain relative positions with offset applied.
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-001", Title = "Child at lane 0", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Parent at lane 1", Status = IssueStatus.Open },
                    Lane = 1, Row = 1, IsActionable = false
                }
            ],
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        // Skip load more, PR, and separator lines to find issue lines
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();
        Assert.That(issueLines, Has.Count.EqualTo(2));
        Assert.That(issueLines[0].Lane, Is.EqualTo(1), "Issue originally at lane 0 should be offset to lane 1");
        Assert.That(issueLines[1].Lane, Is.EqualTo(2), "Issue originally at lane 1 should be offset to lane 2");
    }

    [Test]
    public void ComputeLayout_MergedPrs_ParentLaneAlsoOffset()
    {
        // ParentLane should also be offset when merged PRs exist
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
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();
        var childLine = issueLines.First(l => l.IssueId == "CHILD-001");
        Assert.That(childLine.Lane, Is.EqualTo(1), "Child should be offset to lane 1");
        Assert.That(childLine.ParentLane, Is.EqualTo(2), "ParentLane should be offset to lane 2");
    }

    [Test]
    public void ComputeLayout_NoMergedPrs_NoLaneOffset()
    {
        // Without merged PRs, lanes should not be offset
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Issue in lane 0", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var issueLine = result.OfType<TaskGraphIssueRenderLine>().First();
        Assert.That(issueLine.Lane, Is.EqualTo(0), "Without merged PRs, lane should remain at 0");
    }

    [Test]
    public void ComputeLayout_MergedPrs_FirstPrHasNoTopLine()
    {
        // First (oldest) PR should not have a top connector line
        var taskGraph = new TaskGraphResponse
        {
            Nodes = [],
            TotalLanes = 0,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 101, Title = "PR 101 (oldest)", IsMerged = true },
                new TaskGraphPrResponse { Number = 102, Title = "PR 102 (newest)", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var prLines = result.OfType<TaskGraphPrRenderLine>().ToList();
        Assert.That(prLines, Has.Count.EqualTo(2));
        Assert.That(prLines[0].DrawTopLine, Is.False, "First PR should have no top line");
    }

    [Test]
    public void ComputeLayout_MergedPrs_MiddlePrsHaveTopAndBottomLines()
    {
        // Middle PRs should have both top and bottom connector lines
        var taskGraph = new TaskGraphResponse
        {
            Nodes = [],
            TotalLanes = 0,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 101, Title = "PR 101 (oldest)", IsMerged = true },
                new TaskGraphPrResponse { Number = 102, Title = "PR 102 (middle)", IsMerged = true },
                new TaskGraphPrResponse { Number = 103, Title = "PR 103 (newest)", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var prLines = result.OfType<TaskGraphPrRenderLine>().ToList();
        Assert.That(prLines, Has.Count.EqualTo(3));
        Assert.That(prLines[1].DrawTopLine, Is.True, "Middle PR should have top line");
        Assert.That(prLines[1].DrawBottomLine, Is.True, "Middle PR should have bottom line");
    }

    [Test]
    public void ComputeLayout_MergedPrs_LastPrHasTopAndBottomLines()
    {
        // Last (newest) PR should have top line (connects to previous) and bottom line (connects to issues)
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Some issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 101, Title = "PR 101 (oldest)", IsMerged = true },
                new TaskGraphPrResponse { Number = 102, Title = "PR 102 (newest)", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var prLines = result.OfType<TaskGraphPrRenderLine>().ToList();
        Assert.That(prLines, Has.Count.EqualTo(2));
        Assert.That(prLines[1].DrawTopLine, Is.True, "Last PR should have top line");
        Assert.That(prLines[1].DrawBottomLine, Is.True, "Last PR should have bottom line connecting to issues");
    }

    [Test]
    public void ComputeLayout_SingleMergedPr_WithIssues_HasBottomLineOnly()
    {
        // Single PR with issues below: no top line but has bottom line
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Some issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "Only PR", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var prLines = result.OfType<TaskGraphPrRenderLine>().ToList();
        Assert.That(prLines, Has.Count.EqualTo(1));
        Assert.That(prLines[0].DrawTopLine, Is.False, "Single PR should have no top line");
        Assert.That(prLines[0].DrawBottomLine, Is.True, "Single PR with issues below should have bottom line");
    }

    [Test]
    public void ComputeLayout_SingleMergedPr_NoIssues_NoBottomLine()
    {
        // Single PR with no issues below: neither top nor bottom line
        var taskGraph = new TaskGraphResponse
        {
            Nodes = [],
            TotalLanes = 0,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "Only PR", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var prLines = result.OfType<TaskGraphPrRenderLine>().ToList();
        Assert.That(prLines, Has.Count.EqualTo(1));
        Assert.That(prLines[0].DrawTopLine, Is.False, "Single PR should have no top line");
        Assert.That(prLines[0].DrawBottomLine, Is.False, "Single PR with no issues should have no bottom line");
    }

    [Test]
    public void ComputeLayout_MergedPrs_SeriesConnectorFromLaneNotOffset()
    {
        // SeriesConnectorFromLane should also be offset by +1 when merged PRs exist
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
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Series parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 1, Row = 1, IsActionable = false
                }
            ],
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);

        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();
        var parentLine = issueLines.First(l => l.IssueId == "PARENT-001");
        Assert.That(parentLine.SeriesConnectorFromLane, Is.EqualTo(1),
            "SeriesConnectorFromLane should be offset to lane 1 (original 0 + offset 1)");
    }

    [Test]
    public void ComputeLayout_MergedPrs_LeftmostIssuesGetLane0Connector()
    {
        // When merged PRs exist, issues at the leftmost lane (laneOffset) should get
        // DrawLane0Connector=true, and intermediate issues should get DrawLane0PassThrough=true.
        // Use a connected group so intermediate nodes are genuinely at higher lanes
        // (orphans get lane-normalized to 0, so they'd always be at laneOffset).
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                // Group 1: connected parent-child (child at lane 0, parent at lane 1)
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
                },
                // Group 2: orphan at lane 0
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ORPHAN-001", Title = "Orphan", Status = IssueStatus.Open },
                    Lane = 0, Row = 2, IsActionable = true
                }
            ],
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // Child at lane 1 (offset from 0) gets connector, not last
        Assert.That(issueLines[0].IssueId, Is.EqualTo("CHILD-001"));
        Assert.That(issueLines[0].DrawLane0Connector, Is.True);
        Assert.That(issueLines[0].IsLastLane0Connector, Is.False);
        Assert.That(issueLines[0].DrawLane0PassThrough, Is.False);

        // Parent at lane 2 (offset from 1) gets pass-through
        Assert.That(issueLines[1].IssueId, Is.EqualTo("PARENT-001"));
        Assert.That(issueLines[1].DrawLane0Connector, Is.False);
        Assert.That(issueLines[1].DrawLane0PassThrough, Is.True);

        // Orphan at lane 1 (offset from 0) gets connector and is last
        Assert.That(issueLines[2].IssueId, Is.EqualTo("ORPHAN-001"));
        Assert.That(issueLines[2].DrawLane0Connector, Is.True);
        Assert.That(issueLines[2].IsLastLane0Connector, Is.True);
        Assert.That(issueLines[2].DrawLane0PassThrough, Is.False);
    }

    [Test]
    public void ComputeLayout_MergedPrs_SingleLeftmostIssue_IsFirstAndLast()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Only actionable", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines[0].DrawLane0Connector, Is.True);
        Assert.That(issueLines[0].IsLastLane0Connector, Is.True);
    }

    [Test]
    public void ComputeLayout_NoMergedPrs_NoLane0Connectors()
    {
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Issue", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines[0].DrawLane0Connector, Is.False);
        Assert.That(issueLines[0].IsLastLane0Connector, Is.False);
        Assert.That(issueLines[0].DrawLane0PassThrough, Is.False);
    }

    [Test]
    public void ComputeLayout_MergedPrs_NoIssues_NoLane0Connectors()
    {
        // No issues at all, just merged PRs - no lane 0 connectors needed
        var taskGraph = new TaskGraphResponse
        {
            Nodes = [],
            TotalLanes = 0,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines, Has.Count.EqualTo(0));
    }

    [Test]
    public void ComputeLayout_MergedPrs_FullMock_CorrectLane0ConnectorFlags()
    {
        // Use the full mock with merged PRs to verify the complete scenario
        var taskGraph = BuildFullMockTaskGraph();
        taskGraph.MergedPrs =
        [
            new TaskGraphPrResponse { Number = 97, Title = "PR 97", IsMerged = true },
            new TaskGraphPrResponse { Number = 98, Title = "PR 98", IsMerged = true }
        ];

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // With offset, lane 0 nodes become lane 1
        // ISSUE-003 (lane 0→1): connector
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-003").DrawLane0Connector, Is.True);
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-003").IsLastLane0Connector, Is.False);

        // ISSUE-001 (lane 0→1): connector
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-001").DrawLane0Connector, Is.True);
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-001").IsLastLane0Connector, Is.False);

        // ISSUE-010 (lane 0→1): connector
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-010").DrawLane0Connector, Is.True);
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-010").IsLastLane0Connector, Is.False);

        // Intermediate nodes at higher lanes: pass-through
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-009").DrawLane0PassThrough, Is.True);
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-008").DrawLane0PassThrough, Is.True);
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-007").DrawLane0PassThrough, Is.True);

        // ISSUE-002 (lane 0→1): last connector
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-002").DrawLane0Connector, Is.True);
        Assert.That(issueLines.First(l => l.IssueId == "ISSUE-002").IsLastLane0Connector, Is.True);
    }

    [Test]
    public void ComputeLayout_MergedPrs_SeriesSiblingsInLane0_OnlyFirstGetsConnector()
    {
        // When series siblings are in lane 0 (offset to lane 1 with PRs),
        // only the first sibling should get DrawLane0Connector = true.
        // Subsequent siblings (with DrawTopLine = true) should get DrawLane0PassThrough = true.
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "First series sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Second series sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 1, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-C", Title = "Third series sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 2, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Series parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 1, Row = 3, IsActionable = false
                }
            ],
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: int.MaxValue);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // First series sibling: gets connector (is the "next" actionable item)
        var childA = issueLines.First(l => l.IssueId == "CHILD-A");
        Assert.That(childA.DrawLane0Connector, Is.True, "First series sibling should get connector");
        Assert.That(childA.DrawLane0PassThrough, Is.False);
        Assert.That(childA.IsSeriesChild, Is.True);
        Assert.That(childA.DrawTopLine, Is.False, "First series sibling has no top line");

        // Second series sibling: gets pass-through only (blocked)
        var childB = issueLines.First(l => l.IssueId == "CHILD-B");
        Assert.That(childB.DrawLane0Connector, Is.False, "Blocked series sibling should NOT get connector");
        Assert.That(childB.DrawLane0PassThrough, Is.True, "Blocked series sibling should get pass-through");
        Assert.That(childB.IsSeriesChild, Is.True);
        Assert.That(childB.DrawTopLine, Is.True, "Non-first series sibling has top line");

        // Third series sibling: gets pass-through only (blocked)
        var childC = issueLines.First(l => l.IssueId == "CHILD-C");
        Assert.That(childC.DrawLane0Connector, Is.False, "Blocked series sibling should NOT get connector");
        Assert.That(childC.DrawLane0PassThrough, Is.True, "Blocked series sibling should get pass-through");
        Assert.That(childC.IsSeriesChild, Is.True);
        Assert.That(childC.DrawTopLine, Is.True);

        // Parent at lane 2 (offset from 1): no lane 0 flags (it's after all leftmost issues)
        var parent = issueLines.First(l => l.IssueId == "PARENT-001");
        Assert.That(parent.DrawLane0Connector, Is.False);
        Assert.That(parent.DrawLane0PassThrough, Is.False, "Parent is after all leftmost issues, no lane 0 flags");
    }

    [Test]
    public void ComputeLayout_MergedPrs_SeriesSiblings_IsLastLane0ConnectorCorrect()
    {
        // When blocked series siblings follow the first sibling, IsLastLane0Connector
        // should be on the first sibling (since it's the only one getting a connector).
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "First series sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Second series sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 1, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Series parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 1, Row = 2, IsActionable = false
                }
            ],
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: int.MaxValue);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // First sibling gets connector AND is last connector (since subsequent siblings are blocked)
        var childA = issueLines.First(l => l.IssueId == "CHILD-A");
        Assert.That(childA.DrawLane0Connector, Is.True);
        Assert.That(childA.IsLastLane0Connector, Is.True, "First sibling should be last connector when others are blocked");

        // Second sibling gets pass-through, NOT marked as last connector
        var childB = issueLines.First(l => l.IssueId == "CHILD-B");
        Assert.That(childB.DrawLane0Connector, Is.False);
        Assert.That(childB.IsLastLane0Connector, Is.False);
    }

    [Test]
    public void ComputeLayout_MergedPrs_MixedSeriesAndOrphans_CorrectConnectors()
    {
        // Mix of series siblings and orphans - orphans should always get connectors
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ORPHAN-001", Title = "Orphan before", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "First series sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Second series sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 2, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Series parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 1, Row = 3, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ORPHAN-002", Title = "Orphan after", Status = IssueStatus.Open },
                    Lane = 0, Row = 4, IsActionable = true
                }
            ],
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: int.MaxValue);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // Orphan before: connector, not last
        var orphan1 = issueLines.First(l => l.IssueId == "ORPHAN-001");
        Assert.That(orphan1.DrawLane0Connector, Is.True);
        Assert.That(orphan1.IsLastLane0Connector, Is.False);

        // First series sibling: connector, not last
        var childA = issueLines.First(l => l.IssueId == "CHILD-A");
        Assert.That(childA.DrawLane0Connector, Is.True);
        Assert.That(childA.IsLastLane0Connector, Is.False);

        // Second series sibling: pass-through only
        var childB = issueLines.First(l => l.IssueId == "CHILD-B");
        Assert.That(childB.DrawLane0Connector, Is.False);
        Assert.That(childB.DrawLane0PassThrough, Is.True);

        // Parent: pass-through (at lane 2)
        var parent = issueLines.First(l => l.IssueId == "PARENT-001");
        Assert.That(parent.DrawLane0PassThrough, Is.True);

        // Orphan after: connector AND last connector
        var orphan2 = issueLines.First(l => l.IssueId == "ORPHAN-002");
        Assert.That(orphan2.DrawLane0Connector, Is.True);
        Assert.That(orphan2.IsLastLane0Connector, Is.True);
    }

    [Test]
    public void ComputeLayout_MergedPrs_ParallelSiblingsInLane0_AllGetConnectors()
    {
        // Parallel siblings (non-series) should ALL get connectors as before
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-A", Title = "First parallel sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-B", Title = "Second parallel sibling", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 1, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Parallel parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Parallel },
                    Lane = 1, Row = 2, IsActionable = false
                }
            ],
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: int.MaxValue);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // Both parallel siblings get connectors
        var childA = issueLines.First(l => l.IssueId == "CHILD-A");
        Assert.That(childA.DrawLane0Connector, Is.True, "Parallel child A should get connector");
        Assert.That(childA.IsSeriesChild, Is.False);

        var childB = issueLines.First(l => l.IssueId == "CHILD-B");
        Assert.That(childB.DrawLane0Connector, Is.True, "Parallel child B should get connector");
        Assert.That(childB.IsSeriesChild, Is.False);
        Assert.That(childB.IsLastLane0Connector, Is.True, "Last parallel child is last connector");
    }

    #region MaxDepth Filtering Tests

    [Test]
    public void ComputeLayout_MaxDepth_FiltersNodesAboveLimitByLane()
    {
        // The full mock has nodes from lane 0 to lane 5
        // With maxDepth=2, only lanes 0, 1, 2 should be visible
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 2);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // All lane 0, 1, 2 nodes should be present
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-003"), Is.True, "Lane 0 orphan should be visible");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-001"), Is.True, "Lane 0 orphan should be visible");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-010"), Is.True, "Lane 0 node should be visible");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-009"), Is.True, "Lane 1 node should be visible");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-011"), Is.True, "Lane 1 node should be visible");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-008"), Is.True, "Lane 2 node should be visible");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-012"), Is.True, "Lane 2 node should be visible");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-002"), Is.True, "Lane 0 orphan should be visible");

        // Lane 3, 4, 5 nodes should be filtered out
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-007"), Is.False, "Lane 3 node should be filtered");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-006"), Is.False, "Lane 3 node should be filtered");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-013"), Is.False, "Lane 3 node should be filtered");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-005"), Is.False, "Lane 4 node should be filtered");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-004"), Is.False, "Lane 5 node should be filtered");
    }

    [Test]
    public void ComputeLayout_MaxDepth_SetsHasHiddenParent_WhenParentFiltered()
    {
        // With maxDepth=2, ISSUE-008 (lane 2) has parent ISSUE-007 (lane 3) which is filtered
        // ISSUE-007 has ExecutionMode.Series, so ISSUE-008 and ISSUE-012 are series siblings
        // For series siblings with hidden parent, only the LAST sibling shows the indicator
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 2);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // ISSUE-008 has hidden parent ISSUE-007, but it connects to ISSUE-012 (next sibling)
        // so it should NOT show the hidden parent indicator (but IsSeriesChild should be true)
        var issue008 = issueLines.FirstOrDefault(l => l.IssueId == "ISSUE-008");
        Assert.That(issue008, Is.Not.Null);
        Assert.That(issue008!.HasHiddenParent, Is.False, "First series sibling should not show indicator (connects to next sibling)");
        Assert.That(issue008.IsSeriesChild, Is.True, "Should be series child via hidden parent");

        // ISSUE-012 is the last sibling with hidden parent ISSUE-007
        // It should show the hidden parent indicator
        var issue012 = issueLines.FirstOrDefault(l => l.IssueId == "ISSUE-012");
        Assert.That(issue012, Is.Not.Null);
        Assert.That(issue012!.HasHiddenParent, Is.True, "Last series sibling should show hidden parent indicator");
        Assert.That(issue012.IsSeriesChild, Is.True, "Should be series child via hidden parent");

        // ISSUE-009 has parent ISSUE-008 which is NOT filtered
        var issue009 = issueLines.FirstOrDefault(l => l.IssueId == "ISSUE-009");
        Assert.That(issue009, Is.Not.Null);
        Assert.That(issue009!.HasHiddenParent, Is.False, "Node with visible parent should have HasHiddenParent=false");
    }

    [Test]
    public void ComputeLayout_MaxDepth_OnlyDirectChildrenOfHiddenParentsGetIndicator()
    {
        // This test verifies the fix for the bug where dotted lines appeared on ALL nodes
        // with any hidden ancestor, not just the direct children of hidden parents.
        //
        // With maxDepth=2:
        // - Visible: ISSUE-010 (lane 0) -> ISSUE-009 (lane 1) -> ISSUE-008 (lane 2)
        // - Hidden:  ISSUE-007 (lane 3) -> ISSUE-005 (lane 4) -> ISSUE-004 (lane 5)
        //
        // Only ISSUE-008 and ISSUE-012 should have HasHiddenParent=true because
        // their DIRECT parent (ISSUE-007) is hidden. ISSUE-009, ISSUE-010, and ISSUE-011
        // should NOT have HasHiddenParent=true even though they have hidden ancestors
        // (grandparents) in the chain.
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 2);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // Direct children of hidden parent ISSUE-007 (ExecutionMode.Series) should show indicator
        // only on the LAST sibling (ISSUE-012), not on earlier siblings that connect to next sibling
        var issue008 = issueLines.First(l => l.IssueId == "ISSUE-008");
        var issue012 = issueLines.First(l => l.IssueId == "ISSUE-012");
        Assert.That(issue008.HasHiddenParent, Is.False, "ISSUE-008 has connection to next sibling, no indicator");
        Assert.That(issue012.HasHiddenParent, Is.True, "ISSUE-012 is last sibling, should show indicator");

        // Nodes with visible parents but hidden grandparents should NOT have indicator
        var issue009 = issueLines.First(l => l.IssueId == "ISSUE-009");
        var issue010 = issueLines.First(l => l.IssueId == "ISSUE-010");
        var issue011 = issueLines.First(l => l.IssueId == "ISSUE-011");
        Assert.That(issue009.HasHiddenParent, Is.False, "ISSUE-009 has visible parent ISSUE-008, hidden grandparent doesn't count");
        Assert.That(issue010.HasHiddenParent, Is.False, "ISSUE-010 has visible parent ISSUE-009");
        Assert.That(issue011.HasHiddenParent, Is.False, "ISSUE-011 has visible parent ISSUE-008");

        // Orphans should never have hidden parent indicator
        var issue003 = issueLines.First(l => l.IssueId == "ISSUE-003");
        var issue001 = issueLines.First(l => l.IssueId == "ISSUE-001");
        var issue002 = issueLines.First(l => l.IssueId == "ISSUE-002");
        Assert.That(issue003.HasHiddenParent, Is.False, "Orphan ISSUE-003 has no parent");
        Assert.That(issue001.HasHiddenParent, Is.False, "Orphan ISSUE-001 has no parent");
        Assert.That(issue002.HasHiddenParent, Is.False, "Orphan ISSUE-002 has no parent");
    }

    [Test]
    public void ComputeLayout_MaxDepth_SetsHiddenParentIsSeriesMode_ForSeriesParent()
    {
        // ISSUE-008 has parent ISSUE-007 which is a Series execution mode parent
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 2);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // ISSUE-008's filtered parent (ISSUE-007) has ExecutionMode.Series
        var issue008 = issueLines.FirstOrDefault(l => l.IssueId == "ISSUE-008");
        Assert.That(issue008, Is.Not.Null);
        Assert.That(issue008!.HiddenParentIsSeriesMode, Is.True, "Hidden parent with Series mode should set flag");

        // ISSUE-012's filtered parent (ISSUE-007) also has ExecutionMode.Series
        var issue012 = issueLines.FirstOrDefault(l => l.IssueId == "ISSUE-012");
        Assert.That(issue012, Is.Not.Null);
        Assert.That(issue012!.HiddenParentIsSeriesMode, Is.True);
    }

    [Test]
    public void ComputeLayout_MaxDepth_PreservesConnectionsWithinLimit()
    {
        // With maxDepth=2, connections between visible nodes should still work
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 2);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // ISSUE-009 (lane 1) should still connect to ISSUE-008 (lane 2)
        var issue009 = issueLines.FirstOrDefault(l => l.IssueId == "ISSUE-009");
        Assert.That(issue009, Is.Not.Null);
        Assert.That(issue009!.ParentLane, Is.EqualTo(2), "Connection to visible parent should be preserved");
    }

    [Test]
    public void ComputeLayout_DefaultMaxDepth_IsThree()
    {
        // Default maxDepth should be 3 (lanes 0, 1, 2, 3 visible)
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // Lane 3 nodes should be visible with default maxDepth
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-007"), Is.True, "Lane 3 should be visible with default maxDepth=3");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-006"), Is.True, "Lane 3 should be visible with default maxDepth=3");

        // Lane 4 and 5 nodes should be filtered with default maxDepth
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-005"), Is.False, "Lane 4 should be filtered with default maxDepth=3");
        Assert.That(issueLines.Any(l => l.IssueId == "ISSUE-004"), Is.False, "Lane 5 should be filtered with default maxDepth=3");
    }

    [Test]
    public void ComputeLayout_MaxDepth_One_ShowsOnlyLaneZeroNodes()
    {
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 0);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // Only lane 0 nodes should be visible
        Assert.That(issueLines.All(l => l.Lane == 0), Is.True, "Only lane 0 nodes should be visible with maxDepth=0");
        Assert.That(issueLines.Count, Is.EqualTo(4), "Should have 4 lane-0 nodes: ISSUE-003, ISSUE-001, ISSUE-010, ISSUE-002");
    }

    [Test]
    public void ComputeLayout_MaxDepth_NoLimit_ShowsAllNodes()
    {
        // With very high maxDepth, all nodes should be visible
        var taskGraph = BuildFullMockTaskGraph();

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 100);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines.Count, Is.EqualTo(13), "All 13 nodes should be visible with high maxDepth");
        Assert.That(issueLines.All(l => !l.HasHiddenParent), Is.True, "No nodes should have hidden parents");
    }

    [Test]
    public void ComputeLayout_MaxDepth_WithMergedPrs_StillAppliesOffset()
    {
        // When merged PRs exist, lanes are offset by 1, but maxDepth should work on original lanes
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
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "GRANDPARENT" }] },
                    Lane = 1, Row = 1, IsActionable = false
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "GRANDPARENT", Title = "Grandparent", Status = IssueStatus.Open },
                    Lane = 2, Row = 2, IsActionable = false
                }
            ],
            TotalLanes = 3,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        // With maxDepth=1, only lanes 0 and 1 should be visible (GRANDPARENT at lane 2 filtered)
        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 1);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines.Any(l => l.IssueId == "CHILD-001"), Is.True);
        Assert.That(issueLines.Any(l => l.IssueId == "PARENT-001"), Is.True);
        Assert.That(issueLines.Any(l => l.IssueId == "GRANDPARENT"), Is.False, "Lane 2 node should be filtered");

        // PARENT-001 should have HasHiddenParent=true
        var parent = issueLines.FirstOrDefault(l => l.IssueId == "PARENT-001");
        Assert.That(parent!.HasHiddenParent, Is.True);
    }

    [Test]
    public void ComputeLayout_MaxDepth_SeriesSiblingsWithHiddenParent_HaveVerticalConnections()
    {
        // ISSUE-008 and ISSUE-012 are children of hidden ISSUE-007 (ExecutionMode.Series)
        // They should have drawTopLine/drawBottomLine to connect vertically as series siblings
        var taskGraph = BuildFullMockTaskGraph();
        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 2);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        var issue008 = issueLines.First(l => l.IssueId == "ISSUE-008");
        var issue012 = issueLines.First(l => l.IssueId == "ISSUE-012");

        // ISSUE-008 is first series sibling - should have drawBottomLine to connect to ISSUE-012
        Assert.That(issue008.DrawBottomLine, Is.True, "First series sibling with hidden parent should have bottom line");
        Assert.That(issue008.IsSeriesChild, Is.True, "Should be detected as series child via hidden parent");

        // ISSUE-012 is last series sibling - should have drawTopLine to connect from ISSUE-008
        Assert.That(issue012.DrawTopLine, Is.True, "Last series sibling should have top line connecting from previous sibling");
        Assert.That(issue012.IsSeriesChild, Is.True, "Should be detected as series child via hidden parent");
    }

    [Test]
    public void ComputeLayout_MaxDepth_OnlyLastSeriesSiblingWithHiddenParent_ShowsDottedIndicator()
    {
        // ISSUE-008 and ISSUE-012 are children of hidden ISSUE-007 (ExecutionMode.Series)
        // Only ISSUE-012 (last sibling) should have HasHiddenParent=true because ISSUE-008
        // has a connection to the next sibling (drawBottomLine=true)
        var taskGraph = BuildFullMockTaskGraph();
        var result = TaskGraphLayoutService.ComputeLayout(taskGraph, maxDepth: 2);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        var issue008 = issueLines.First(l => l.IssueId == "ISSUE-008");
        var issue012 = issueLines.First(l => l.IssueId == "ISSUE-012");

        // ISSUE-008 has a connection to ISSUE-012 (drawBottomLine=true), so no dotted indicator
        Assert.That(issue008.HasHiddenParent, Is.False, "First sibling should not show indicator (has connection to next sibling)");
        Assert.That(issue008.HiddenParentIsSeriesMode, Is.True, "Flag should still indicate series mode");

        // ISSUE-012 is the last sibling (no next series sibling), should show dotted indicator
        Assert.That(issue012.HasHiddenParent, Is.True, "Last sibling should show dotted indicator");
        Assert.That(issue012.HiddenParentIsSeriesMode, Is.True, "Should indicate series mode for vertical dots");
    }

    #endregion

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

    #region Lane0Color Priority Tests

    [Test]
    public void ComputeLayout_MergedPrs_ActionableWithPriority0_SetsRedLane0Color()
    {
        // P0 (critical) priority should produce red color
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Critical issue", Status = IssueStatus.Open, Priority = 0 },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines[0].Lane0Color, Is.EqualTo("#ef4444"), "P0 priority should produce red color");
    }

    [Test]
    public void ComputeLayout_MergedPrs_ActionableWithPriority4_SetsBlueLane0Color()
    {
        // P4 (low) priority should produce blue color
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Low priority issue", Status = IssueStatus.Open, Priority = 4 },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines[0].Lane0Color, Is.EqualTo("#3b82f6"), "P4 priority should produce blue color");
    }

    [Test]
    public void ComputeLayout_MergedPrs_NoPriority_SetsGreyLane0Color()
    {
        // No priority should produce grey color
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "No priority issue", Status = IssueStatus.Open, Priority = null },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines[0].Lane0Color, Is.EqualTo("#6b7280"), "Null priority should produce grey color");
    }

    [Test]
    public void ComputeLayout_MergedPrs_ParentIssuesInheritLane0ColorFromActionable()
    {
        // Parent issues in the same group should inherit the lane0Color from the actionable (lane 0) issue
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-001", Title = "Child", Status = IssueStatus.Open, Priority = 1,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Parent", Status = IssueStatus.Open, Priority = 4 },
                    Lane = 1, Row = 1, IsActionable = false
                }
            ],
            TotalLanes = 2,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        // Child is at lane 0 (becomes 1 with offset) - it determines the group color
        // Both child and parent should have the same lane0Color based on the child's priority (P1 = orange)
        Assert.That(issueLines.First(l => l.IssueId == "CHILD-001").Lane0Color, Is.EqualTo("#f97316"),
            "Child (actionable) should have P1 orange color");
        Assert.That(issueLines.First(l => l.IssueId == "PARENT-001").Lane0Color, Is.EqualTo("#f97316"),
            "Parent should inherit lane0Color from actionable child");
    }

    [Test]
    public void ComputeLayout_MergedPrs_MultipleGroups_EachHasOwnLane0Color()
    {
        // Each group should have its own lane0Color based on its actionable issue's priority
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                // Group 1: P0 priority (red)
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "GROUP1-001", Title = "Critical", Status = IssueStatus.Open, Priority = 0 },
                    Lane = 0, Row = 0, IsActionable = true
                },
                // Group 2: P3 priority (green)
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "GROUP2-001", Title = "Normal", Status = IssueStatus.Open, Priority = 3 },
                    Lane = 0, Row = 1, IsActionable = true
                }
            ],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines.First(l => l.IssueId == "GROUP1-001").Lane0Color, Is.EqualTo("#ef4444"),
            "Group 1 should have P0 red color");
        Assert.That(issueLines.First(l => l.IssueId == "GROUP2-001").Lane0Color, Is.EqualTo("#22c55e"),
            "Group 2 should have P3 green color");
    }

    [Test]
    public void ComputeLayout_NoMergedPrs_Lane0ColorIsNull()
    {
        // Without merged PRs, there's no lane 0 connector, so Lane0Color should be null
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Issue", Status = IssueStatus.Open, Priority = 0 },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var result = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var issueLines = result.OfType<TaskGraphIssueRenderLine>().ToList();

        Assert.That(issueLines[0].Lane0Color, Is.Null, "Without merged PRs, Lane0Color should be null");
    }

    #endregion

    #region Draft Issue Layout Tests

    [Test]
    public void ComputeDraftIssueLine_InsertBetweenSiblings_HasTopAndBottomLines()
    {
        // Arrange: Three orphan issues (siblings at same lane)
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
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-003", Title = "Third", Status = IssueStatus.Open },
                    Lane = 0, Row = 2, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var existingLines = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var draft = new DraftIssueContext(
            ReferenceIssueId: "ISSUE-002",
            IsAbove: false, // Insert below ISSUE-002 (between 002 and 003)
            PendingParentId: null,
            PendingChildId: null,
            InheritedParentId: null);

        // Act
        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(taskGraph, draft, existingLines);

        // Assert
        Assert.That(draftLine, Is.Not.Null);
        Assert.That(draftLine!.DrawTopLine, Is.True, "Draft between siblings should have top line");
        Assert.That(draftLine.DrawBottomLine, Is.True, "Draft between siblings should have bottom line");
        Assert.That(draftLine.Lane, Is.EqualTo(0), "Draft should be in same lane as siblings");
    }

    [Test]
    public void ComputeDraftIssueLine_InsertAboveFirst_HasOnlyBottomLine()
    {
        // Arrange: Two orphan issues
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

        var existingLines = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var draft = new DraftIssueContext(
            ReferenceIssueId: "ISSUE-001",
            IsAbove: true, // Insert above first issue
            PendingParentId: null,
            PendingChildId: null,
            InheritedParentId: null);

        // Act
        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(taskGraph, draft, existingLines);

        // Assert
        Assert.That(draftLine, Is.Not.Null);
        Assert.That(draftLine!.DrawTopLine, Is.False, "Draft above first should not have top line");
        Assert.That(draftLine.DrawBottomLine, Is.True, "Draft above first should have bottom line to connect to sibling");
    }

    [Test]
    public void ComputeDraftIssueLine_InsertBelowLast_HasOnlyTopLine()
    {
        // Arrange: Two orphan issues
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

        var existingLines = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var draft = new DraftIssueContext(
            ReferenceIssueId: "ISSUE-002",
            IsAbove: false, // Insert below last issue
            PendingParentId: null,
            PendingChildId: null,
            InheritedParentId: null);

        // Act
        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(taskGraph, draft, existingLines);

        // Assert
        Assert.That(draftLine, Is.Not.Null);
        Assert.That(draftLine!.DrawTopLine, Is.True, "Draft below last should have top line");
        Assert.That(draftLine.DrawBottomLine, Is.False, "Draft below last should not have bottom line");
    }

    [Test]
    public void ComputeDraftIssueLine_InsertAsChildOfSeriesParent_IsSeriesChild()
    {
        // Arrange: Series parent with existing child
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "CHILD-001", Title = "Existing child", Status = IssueStatus.Open,
                        ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001" }] },
                    Lane = 0, Row = 0, IsActionable = true
                },
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "PARENT-001", Title = "Series parent", Status = IssueStatus.Open,
                        ExecutionMode = ExecutionMode.Series },
                    Lane = 1, Row = 1, IsActionable = false
                }
            ],
            TotalLanes = 2
        };

        var existingLines = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var draft = new DraftIssueContext(
            ReferenceIssueId: "CHILD-001",
            IsAbove: false, // Insert below existing child
            PendingParentId: null,
            PendingChildId: null,
            InheritedParentId: "PARENT-001"); // Inheriting same parent (sibling creation)

        // Act
        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(taskGraph, draft, existingLines);

        // Assert
        Assert.That(draftLine, Is.Not.Null);
        Assert.That(draftLine!.IsSeriesChild, Is.True, "Draft under series parent should be series child");
        Assert.That(draftLine.DrawBottomLine, Is.True, "Series child should have bottom line");
        Assert.That(draftLine.DrawTopLine, Is.True, "Draft below existing series child should have top line");
    }

    [Test]
    public void ComputeDraftIssueLine_TabPressed_BecomesParent()
    {
        // Arrange: Single issue that draft will become parent of
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Will become child", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var existingLines = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var draft = new DraftIssueContext(
            ReferenceIssueId: "ISSUE-001",
            IsAbove: true, // Insert above
            PendingParentId: null,
            PendingChildId: "ISSUE-001", // Tab pressed - draft becomes parent of reference
            InheritedParentId: null);

        // Act
        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(taskGraph, draft, existingLines);

        // Assert
        Assert.That(draftLine, Is.Not.Null);
        // Draft becomes parent, so it should be in a higher lane than the child
        Assert.That(draftLine!.Lane, Is.EqualTo(1), "Draft as parent should be in lane 1 (higher than child at lane 0)");
    }

    [Test]
    public void ComputeDraftIssueLine_ShiftTabPressed_BecomesChild()
    {
        // Arrange: Single issue that draft will become child of
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "Will become parent", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1
        };

        var existingLines = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var draft = new DraftIssueContext(
            ReferenceIssueId: "ISSUE-001",
            IsAbove: false, // Insert below
            PendingParentId: "ISSUE-001", // Shift+Tab pressed - draft becomes child of reference
            PendingChildId: null,
            InheritedParentId: null);

        // Act
        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(taskGraph, draft, existingLines);

        // Assert
        Assert.That(draftLine, Is.Not.Null);
        // Draft becomes child, reference becomes parent - draft stays at lane 0, parent moves to lane 1
        Assert.That(draftLine!.Lane, Is.EqualTo(0), "Draft as child should stay at lane 0");
        Assert.That(draftLine.ParentLane, Is.EqualTo(1), "Draft should have parent lane pointing to reference");
    }

    [Test]
    public void ComputeDraftIssueLine_WithMergedPrs_LanesAreOffset()
    {
        // Arrange: Issue with merged PRs (lanes should be offset by 1)
        var taskGraph = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse
                {
                    Issue = new IssueResponse { Id = "ISSUE-001", Title = "First", Status = IssueStatus.Open },
                    Lane = 0, Row = 0, IsActionable = true
                }
            ],
            TotalLanes = 1,
            MergedPrs =
            [
                new TaskGraphPrResponse { Number = 100, Title = "PR 100", IsMerged = true }
            ]
        };

        var existingLines = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var draft = new DraftIssueContext(
            ReferenceIssueId: "ISSUE-001",
            IsAbove: false,
            PendingParentId: null,
            PendingChildId: null,
            InheritedParentId: null);

        // Act
        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(taskGraph, draft, existingLines);

        // Assert
        Assert.That(draftLine, Is.Not.Null);
        // With merged PRs, lane 0 is reserved for PR connector, so issues start at lane 1
        Assert.That(draftLine!.Lane, Is.EqualTo(1), "Draft with merged PRs should be at lane 1 (offset)");
    }

    [Test]
    public void ComputeDraftIssueLine_NullTaskGraph_ReturnsNull()
    {
        var draft = new DraftIssueContext(
            ReferenceIssueId: "ISSUE-001",
            IsAbove: false,
            PendingParentId: null,
            PendingChildId: null,
            InheritedParentId: null);

        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(null, draft, []);

        Assert.That(draftLine, Is.Null);
    }

    [Test]
    public void ComputeDraftIssueLine_NoReferenceIssue_ReturnsDefaultLine()
    {
        // Arrange: Empty task graph (creating first issue)
        var taskGraph = new TaskGraphResponse
        {
            Nodes = [],
            TotalLanes = 0
        };

        var existingLines = TaskGraphLayoutService.ComputeLayout(taskGraph);
        var draft = new DraftIssueContext(
            ReferenceIssueId: null,
            IsAbove: false,
            PendingParentId: null,
            PendingChildId: null,
            InheritedParentId: null);

        // Act
        var draftLine = TaskGraphLayoutService.ComputeDraftIssueLine(taskGraph, draft, existingLines);

        // Assert
        Assert.That(draftLine, Is.Not.Null);
        Assert.That(draftLine!.Lane, Is.EqualTo(0), "First draft issue should be at lane 0");
        Assert.That(draftLine.DrawTopLine, Is.False);
        Assert.That(draftLine.DrawBottomLine, Is.False);
    }

    #endregion
}
