using Fleece.Core.Models;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Testing.Services;

namespace Homespun.Tests.Features.Gitgraph;

[TestFixture]
public class TaskGraphTextRendererTests
{
    private static async Task<TaskGraph> BuildTaskGraph(List<Issue> issues)
    {
        var adapter = new MockIssueServiceAdapter(issues);
        var nextService = new NextService(adapter);
        var service = new TaskGraphService(adapter, nextService);
        return await service.BuildGraphAsync();
    }

    [Test]
    public void Render_EmptyGraph_ReturnsEmptyString()
    {
        var taskGraph = new TaskGraph
        {
            Nodes = [],
            TotalLanes = 0
        };

        var result = TaskGraphTextRenderer.Render(taskGraph);

        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task Render_SingleOrphan_ShowsMarkerAndTitle()
    {
        var issues = new List<Issue>
        {
            new()
            {
                Id = "TEST-001",
                Title = "Standalone issue",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            }
        };

        var taskGraph = await BuildTaskGraph(issues);
        var result = TaskGraphTextRenderer.Render(taskGraph);

        Assert.That(result, Does.Contain("TEST-001"));
        Assert.That(result, Does.Contain("Standalone issue"));
        // Orphan with no dependencies should be actionable
        Assert.That(result.TrimEnd(), Does.StartWith("\u25CB"));
    }

    [Test]
    public async Task Render_ParentChild_DrawsConnector()
    {
        var issues = new List<Issue>
        {
            new()
            {
                Id = "PARENT-001",
                Title = "Parent task",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = "CHILD-001",
                Title = "Child task",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                ParentIssues = [new ParentIssueRef { ParentIssue = "PARENT-001", SortOrder = "0" }],
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            }
        };

        var taskGraph = await BuildTaskGraph(issues);
        var result = TaskGraphTextRenderer.Render(taskGraph);

        // Should contain both issues
        Assert.That(result, Does.Contain("CHILD-001"));
        Assert.That(result, Does.Contain("PARENT-001"));
        // Should contain connector characters
        Assert.That(result, Does.Contain("\u2502")); // │ vertical connector
    }

    [Test]
    public async Task Render_ThreeNodeChain_StaircasePattern()
    {
        var issues = new List<Issue>
        {
            new()
            {
                Id = "ROOT-001",
                Title = "Root",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = "MID-001",
                Title = "Middle",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ROOT-001", SortOrder = "0" }],
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = "LEAF-001",
                Title = "Leaf",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                ParentIssues = [new ParentIssueRef { ParentIssue = "MID-001", SortOrder = "0" }],
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            }
        };

        var taskGraph = await BuildTaskGraph(issues);
        var result = TaskGraphTextRenderer.Render(taskGraph);

        // All three should be present
        Assert.That(result, Does.Contain("ROOT-001"));
        Assert.That(result, Does.Contain("MID-001"));
        Assert.That(result, Does.Contain("LEAF-001"));
        // Leaf is actionable
        Assert.That(result, Does.Contain("\u25CB")); // ○ actionable marker
        // Root is not actionable
        Assert.That(result, Does.Contain("\u25CC")); // ◌ open/not-next marker
    }

    [Test]
    public async Task Render_TwoSiblings_ShareParentLane()
    {
        var issues = new List<Issue>
        {
            new()
            {
                Id = "PARENT-001",
                Title = "Parent",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = "CHILD-A",
                Title = "First child",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                ParentIssues = [new ParentIssueRef { ParentIssue = "PARENT-001", SortOrder = "0" }],
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = "CHILD-B",
                Title = "Second child",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                ParentIssues = [new ParentIssueRef { ParentIssue = "PARENT-001", SortOrder = "1" }],
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            }
        };

        var taskGraph = await BuildTaskGraph(issues);
        var result = TaskGraphTextRenderer.Render(taskGraph);

        // Both children and parent present
        Assert.That(result, Does.Contain("CHILD-A"));
        Assert.That(result, Does.Contain("CHILD-B"));
        Assert.That(result, Does.Contain("PARENT-001"));
        // Both siblings connect to the same parent lane, so we should see ┐ and ┤
        Assert.That(result, Does.Contain("\u2510")); // ┐ first child connector
        Assert.That(result, Does.Contain("\u2524")); // ┤ subsequent sibling connector
    }

    [Test]
    public async Task Render_MockIssueData_MatchesExpectedOutput()
    {
        var issues = GetMockIssues();
        var taskGraph = await BuildTaskGraph(issues);
        var result = TaskGraphTextRenderer.Render(taskGraph);

        // ISSUE-003 is Progress status - Fleece.Core marks it as not actionable (◌)
        // ISSUE-006 is actionable per Fleece.Core's NextService (○)
        var expected = string.Join("\n", new[]
        {
            "\u25CC  ISSUE-003 Fix login timeout bug",
            "",
            "\u25CB  ISSUE-001 Add dark mode support",
            "",
            "\u25CB\u2500\u2510  ISSUE-010 Implement DELETE endpoints",
            "  \u2502",
            "  \u25CC\u2500\u2510  ISSUE-009 Implement PUT/PATCH endpoints",
            "    \u2502",
            "  \u25CC\u2500\u2524  ISSUE-011 Add request validation",
            "    \u2502",
            "    \u25CC\u2500\u2510  ISSUE-008 Implement POST endpoints",
            "      \u2502",
            "    \u25CC\u2500\u2524  ISSUE-012 Add rate limiting",
            "      \u2502",
            "      \u25CC\u2500\u2510  ISSUE-007 Implement GET endpoints",
            "        \u2502",
            "      \u25CB\u2500\u2524  ISSUE-006 Write API documentation",
            "        \u2502",
            "      \u25CC\u2500\u2524  ISSUE-013 Set up API monitoring",
            "        \u2502",
            "        \u25CC\u2500\u2510  ISSUE-005 Implement API endpoints",
            "          \u2502",
            "          \u25CC  ISSUE-004 Design API schema",
            "",
            "\u25CB  ISSUE-002 Improve mobile responsiveness"
        });

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task Render_MockIssueData_NextIssuesAreActionable()
    {
        var issues = GetMockIssues();
        var taskGraph = await BuildTaskGraph(issues);
        var result = TaskGraphTextRenderer.Render(taskGraph);

        var lines = result.Split('\n');

        // Actionable issues per Fleece.Core's NextService
        // ISSUE-003 is Progress status - Fleece.Core does not mark it as actionable
        // ISSUE-006 is actionable per Fleece.Core's NextService
        var nextIssueIds = new[] { "ISSUE-001", "ISSUE-002", "ISSUE-006", "ISSUE-010" };
        foreach (var issueId in nextIssueIds)
        {
            var line = lines.FirstOrDefault(l => l.Contains(issueId));
            Assert.That(line, Is.Not.Null, $"Should contain {issueId}");
            Assert.That(line, Does.Contain("\u25CB"), $"{issueId} should use \u25CB (actionable marker)");
        }

        // Non-actionable issues should use ◌
        var nonNextIssueIds = new[] { "ISSUE-003", "ISSUE-004", "ISSUE-005", "ISSUE-007", "ISSUE-008", "ISSUE-009", "ISSUE-011", "ISSUE-012", "ISSUE-013" };
        foreach (var issueId in nonNextIssueIds)
        {
            var line = lines.FirstOrDefault(l => l.Contains(issueId));
            Assert.That(line, Is.Not.Null, $"Should contain {issueId}");
            Assert.That(line, Does.Contain("\u25CC"), $"{issueId} should use \u25CC (non-actionable marker)");
        }
    }

    [Test]
    public void Render_CompletedIssue_UsesFilledMarker()
    {
        // TaskGraphService filters out terminal statuses, so build TaskGraph directly
        var taskGraph = new TaskGraph
        {
            Nodes =
            [
                new TaskGraphNode
                {
                    Issue = new Issue
                    {
                        Id = "DONE-001",
                        Title = "Completed task",
                        Type = IssueType.Task,
                        Status = IssueStatus.Complete,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastUpdate = DateTimeOffset.UtcNow
                    },
                    Lane = 0,
                    Row = 0,
                    IsActionable = false
                }
            ],
            TotalLanes = 1
        };

        var result = TaskGraphTextRenderer.Render(taskGraph);

        Assert.That(result, Does.Contain("\u25CF")); // ● filled marker for complete
    }

    [Test]
    public void Render_ClosedIssue_UsesClosedMarker()
    {
        // TaskGraphService filters out terminal statuses, so build TaskGraph directly
        var taskGraph = new TaskGraph
        {
            Nodes =
            [
                new TaskGraphNode
                {
                    Issue = new Issue
                    {
                        Id = "CLOSED-001",
                        Title = "Closed task",
                        Type = IssueType.Task,
                        Status = IssueStatus.Closed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastUpdate = DateTimeOffset.UtcNow
                    },
                    Lane = 0,
                    Row = 0,
                    IsActionable = false
                }
            ],
            TotalLanes = 1
        };

        var result = TaskGraphTextRenderer.Render(taskGraph);

        Assert.That(result, Does.Contain("\u2298")); // ⊘ closed marker
    }

    /// <summary>
    /// Returns the same fake issues used by MockGraphService.GetFakeIssues().
    /// </summary>
    private static List<Issue> GetMockIssues()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new Issue
            {
                Id = "ISSUE-001",
                Title = "Add dark mode support",
                Type = IssueType.Feature,
                Status = IssueStatus.Open,
                Priority = 2,
                CreatedAt = now.AddDays(-14),
                LastUpdate = now.AddDays(-2)
            },
            new Issue
            {
                Id = "ISSUE-002",
                Title = "Improve mobile responsiveness",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                CreatedAt = now.AddDays(-12),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-003",
                Title = "Fix login timeout bug",
                Type = IssueType.Bug,
                Status = IssueStatus.Progress,
                Priority = 1,
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddHours(-6)
            },
            new Issue
            {
                Id = "ISSUE-004",
                Title = "Design API schema",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [],
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-3)
            },
            new Issue
            {
                Id = "ISSUE-005",
                Title = "Implement API endpoints",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "0" }],
                CreatedAt = now.AddDays(-9),
                LastUpdate = now.AddDays(-2)
            },
            new Issue
            {
                Id = "ISSUE-006",
                Title = "Write API documentation",
                Type = IssueType.Chore,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }],
                CreatedAt = now.AddDays(-8),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-007",
                Title = "Implement GET endpoints",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }],
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-008",
                Title = "Implement POST endpoints",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-007", SortOrder = "0" }],
                CreatedAt = now.AddDays(-6),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-009",
                Title = "Implement PUT/PATCH endpoints",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-008", SortOrder = "0" }],
                CreatedAt = now.AddDays(-5),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-010",
                Title = "Implement DELETE endpoints",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-009", SortOrder = "0" }],
                CreatedAt = now.AddDays(-4),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-011",
                Title = "Add request validation",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-008", SortOrder = "0" }],
                CreatedAt = now.AddDays(-5),
                LastUpdate = now.AddDays(-2)
            },
            new Issue
            {
                Id = "ISSUE-012",
                Title = "Add rate limiting",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-007", SortOrder = "0" }],
                CreatedAt = now.AddDays(-6),
                LastUpdate = now.AddDays(-3)
            },
            new Issue
            {
                Id = "ISSUE-013",
                Title = "Set up API monitoring",
                Type = IssueType.Chore,
                Status = IssueStatus.Open,
                Priority = 4,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }],
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddDays(-2)
            }
        ];
    }
}
