using Homespun.Features.Roadmap;

namespace Homespun.Tests.Features.Roadmap;

/// <summary>
/// Tests for the updated ROADMAP.json schema (v1.1) with:
/// - Flat list structure (parents array instead of children tree)
/// - shortTitle field for branch name generation
/// - id as full branch name (group/type/shortTitle)
/// - FutureChangeStatus field
/// - Lowercase enum serialization for type
/// </summary>
[TestFixture]
public class RoadmapSchemaV2Tests
{
    #region Schema Structure Tests

    [Test]
    public void RoadmapParser_V2Schema_ParsesFlatListWithParents()
    {
        // Arrange - Flat list with parents instead of nested children
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/add-auth",
                    "shortTitle": "add-auth",
                    "group": "core",
                    "type": "feature",
                    "title": "Add Authentication",
                    "parents": []
                },
                {
                    "id": "core/feature/add-oauth",
                    "shortTitle": "add-oauth",
                    "group": "core",
                    "type": "feature",
                    "title": "Add OAuth Support",
                    "parents": ["core/feature/add-auth"]
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes, Has.Count.EqualTo(2));
        Assert.That(result.Changes[0].Id, Is.EqualTo("core/feature/add-auth"));
        Assert.That(result.Changes[0].ShortTitle, Is.EqualTo("add-auth"));
        Assert.That(result.Changes[0].Parents, Is.Empty);
        Assert.That(result.Changes[1].Parents, Has.Count.EqualTo(1));
        Assert.That(result.Changes[1].Parents[0], Is.EqualTo("core/feature/add-auth"));
    }

    [Test]
    public void RoadmapParser_V2Schema_SupportsMultipleParents_DAG()
    {
        // Arrange - DAG structure with multiple parents
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/base-a",
                    "shortTitle": "base-a",
                    "group": "core",
                    "type": "feature",
                    "title": "Base Feature A",
                    "parents": []
                },
                {
                    "id": "core/feature/base-b",
                    "shortTitle": "base-b",
                    "group": "core",
                    "type": "feature",
                    "title": "Base Feature B",
                    "parents": []
                },
                {
                    "id": "core/feature/combined",
                    "shortTitle": "combined",
                    "group": "core",
                    "type": "feature",
                    "title": "Combined Feature",
                    "parents": ["core/feature/base-a", "core/feature/base-b"]
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        var combined = result.Changes.First(c => c.ShortTitle == "combined");
        Assert.That(combined.Parents, Has.Count.EqualTo(2));
        Assert.That(combined.Parents, Contains.Item("core/feature/base-a"));
        Assert.That(combined.Parents, Contains.Item("core/feature/base-b"));
    }

    [Test]
    public void RoadmapParser_V2Schema_IdIsFullBranchName()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "frontend/bug/fix-login",
                    "shortTitle": "fix-login",
                    "group": "frontend",
                    "type": "bug",
                    "title": "Fix Login Bug",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var change = result.Changes[0];

        // Assert - id IS the branch name, no need for GetBranchName()
        Assert.That(change.Id, Is.EqualTo("frontend/bug/fix-login"));
        Assert.That(change.Id, Does.Contain("/"));
    }

    #endregion

    #region ShortTitle Validation Tests

    [Test]
    public void RoadmapParser_V2Schema_RequiresShortTitle()
    {
        // Arrange - Missing shortTitle
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/test",
                    "group": "core",
                    "type": "feature",
                    "title": "Test",
                    "parents": []
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("shortTitle"));
    }

    [Test]
    public void RoadmapParser_V2Schema_ShortTitleMustBeLowercaseAlphanumericHyphens()
    {
        // Arrange - Invalid shortTitle with uppercase
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/Test-Feature",
                    "shortTitle": "Test-Feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Test",
                    "parents": []
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("shortTitle").Or.Contain("lowercase"));
    }

    [Test]
    public void RoadmapParser_V2Schema_ShortTitleCannotContainSlashes()
    {
        // Arrange - Invalid shortTitle with slashes
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/test/feature",
                    "shortTitle": "test/feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Test",
                    "parents": []
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("shortTitle"));
    }

    [Test]
    public void RoadmapParser_V2Schema_ValidShortTitlePatterns()
    {
        // Arrange - Valid shortTitle patterns
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/simple",
                    "shortTitle": "simple",
                    "group": "core",
                    "type": "feature",
                    "title": "Simple",
                    "parents": []
                },
                {
                    "id": "core/feature/with-hyphens",
                    "shortTitle": "with-hyphens",
                    "group": "core",
                    "type": "feature",
                    "title": "With Hyphens",
                    "parents": []
                },
                {
                    "id": "core/feature/with123numbers",
                    "shortTitle": "with123numbers",
                    "group": "core",
                    "type": "feature",
                    "title": "With Numbers",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes, Has.Count.EqualTo(3));
    }

    #endregion

    #region ID Format Validation Tests

    [Test]
    public void RoadmapParser_V2Schema_IdMustMatchGroupTypeShortTitle()
    {
        // Arrange - id doesn't match group/type/shortTitle
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "wrong/path/here",
                    "shortTitle": "test",
                    "group": "core",
                    "type": "feature",
                    "title": "Test",
                    "parents": []
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("id").Or.Contain("match"));
    }

    [Test]
    public void RoadmapParser_V2Schema_ValidIdFormat()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "backend/refactor/cleanup-api",
                    "shortTitle": "cleanup-api",
                    "group": "backend",
                    "type": "refactor",
                    "title": "API Cleanup",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes[0].Id, Is.EqualTo("backend/refactor/cleanup-api"));
    }

    #endregion

    #region FutureChangeStatus Tests

    [Test]
    public void RoadmapParser_V2Schema_ParsesStatus()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/pending-task",
                    "shortTitle": "pending-task",
                    "group": "core",
                    "type": "feature",
                    "title": "Pending Task",
                    "parents": [],
                    "status": "pending"
                },
                {
                    "id": "core/feature/in-progress-task",
                    "shortTitle": "in-progress-task",
                    "group": "core",
                    "type": "feature",
                    "title": "In Progress Task",
                    "parents": [],
                    "status": "inProgress"
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes[0].Status, Is.EqualTo(FutureChangeStatus.Pending));
        Assert.That(result.Changes[1].Status, Is.EqualTo(FutureChangeStatus.InProgress));
    }

    [Test]
    public void RoadmapParser_V2Schema_DefaultStatusIsPending()
    {
        // Arrange - No status specified
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/no-status",
                    "shortTitle": "no-status",
                    "group": "core",
                    "type": "feature",
                    "title": "No Status",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes[0].Status, Is.EqualTo(FutureChangeStatus.Pending));
    }

    [Test]
    public void RoadmapParser_V2Schema_AllStatusValuesSupported()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/pending",
                    "shortTitle": "pending",
                    "group": "core",
                    "type": "feature",
                    "title": "Pending",
                    "parents": [],
                    "status": "pending"
                },
                {
                    "id": "core/feature/in-progress",
                    "shortTitle": "in-progress",
                    "group": "core",
                    "type": "feature",
                    "title": "In Progress",
                    "parents": [],
                    "status": "inProgress"
                },
                {
                    "id": "core/feature/awaiting-pr",
                    "shortTitle": "awaiting-pr",
                    "group": "core",
                    "type": "feature",
                    "title": "Awaiting PR",
                    "parents": [],
                    "status": "awaitingPR"
                },
                {
                    "id": "core/feature/complete",
                    "shortTitle": "complete",
                    "group": "core",
                    "type": "feature",
                    "title": "Complete",
                    "parents": [],
                    "status": "complete"
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes[0].Status, Is.EqualTo(FutureChangeStatus.Pending));
        Assert.That(result.Changes[1].Status, Is.EqualTo(FutureChangeStatus.InProgress));
        Assert.That(result.Changes[2].Status, Is.EqualTo(FutureChangeStatus.AwaitingPR));
        Assert.That(result.Changes[3].Status, Is.EqualTo(FutureChangeStatus.Complete));
    }

    #endregion

    #region Lowercase Enum Serialization Tests

    [Test]
    public void RoadmapParser_V2Schema_TypeSerializesToLowercase()
    {
        // Arrange
        var roadmap = new Homespun.Features.Roadmap.Roadmap
        {
            Version = "1.1",
            Changes =
            [
                new FutureChange
                {
                    Id = "core/feature/test",
                    ShortTitle = "test",
                    Group = "core",
                    Type = ChangeType.Feature,
                    Title = "Test",
                    Parents = []
                }
            ]
        };

        // Act
        var json = RoadmapParser.Serialize(roadmap);

        // Assert - type should be lowercase "feature", not "Feature"
        Assert.That(json, Does.Contain("\"type\": \"feature\"").Or.Contain("\"type\":\"feature\""));
        Assert.That(json, Does.Not.Contain("\"type\": \"Feature\""));
        Assert.That(json, Does.Not.Contain("\"type\":\"Feature\""));
    }

    [Test]
    public void RoadmapParser_V2Schema_ParsesLowercaseType()
    {
        // Arrange - All lowercase types
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/f1",
                    "shortTitle": "f1",
                    "group": "core",
                    "type": "feature",
                    "title": "Feature",
                    "parents": []
                },
                {
                    "id": "core/bug/b1",
                    "shortTitle": "b1",
                    "group": "core",
                    "type": "bug",
                    "title": "Bug",
                    "parents": []
                },
                {
                    "id": "core/refactor/r1",
                    "shortTitle": "r1",
                    "group": "core",
                    "type": "refactor",
                    "title": "Refactor",
                    "parents": []
                },
                {
                    "id": "core/docs/d1",
                    "shortTitle": "d1",
                    "group": "core",
                    "type": "docs",
                    "title": "Docs",
                    "parents": []
                },
                {
                    "id": "core/test/t1",
                    "shortTitle": "t1",
                    "group": "core",
                    "type": "test",
                    "title": "Test",
                    "parents": []
                },
                {
                    "id": "core/chore/c1",
                    "shortTitle": "c1",
                    "group": "core",
                    "type": "chore",
                    "title": "Chore",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes[0].Type, Is.EqualTo(ChangeType.Feature));
        Assert.That(result.Changes[1].Type, Is.EqualTo(ChangeType.Bug));
        Assert.That(result.Changes[2].Type, Is.EqualTo(ChangeType.Refactor));
        Assert.That(result.Changes[3].Type, Is.EqualTo(ChangeType.Docs));
        Assert.That(result.Changes[4].Type, Is.EqualTo(ChangeType.Test));
        Assert.That(result.Changes[5].Type, Is.EqualTo(ChangeType.Chore));
    }

    #endregion

    #region Parent Reference Validation Tests

    [Test]
    public void RoadmapParser_V2Schema_ParentMustExist()
    {
        // Arrange - Parent reference to non-existent change
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/orphan",
                    "shortTitle": "orphan",
                    "group": "core",
                    "type": "feature",
                    "title": "Orphan",
                    "parents": ["core/feature/non-existent"]
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("parent").Or.Contain("non-existent"));
    }

    [Test]
    public void RoadmapParser_V2Schema_EmptyParentsIsValid()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/root",
                    "shortTitle": "root",
                    "group": "core",
                    "type": "feature",
                    "title": "Root",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes[0].Parents, Is.Empty);
    }

    #endregion

    #region Serialization Tests

    [Test]
    public void RoadmapParser_V2Schema_SerializationRoundTrip()
    {
        // Arrange
        var roadmap = new Homespun.Features.Roadmap.Roadmap
        {
            Version = "1.1",
            LastUpdated = DateTime.UtcNow,
            Changes =
            [
                new FutureChange
                {
                    Id = "core/feature/base",
                    ShortTitle = "base",
                    Group = "core",
                    Type = ChangeType.Feature,
                    Title = "Base Feature",
                    Description = "Base description",
                    Parents = [],
                    Status = FutureChangeStatus.Pending
                },
                new FutureChange
                {
                    Id = "core/feature/dependent",
                    ShortTitle = "dependent",
                    Group = "core",
                    Type = ChangeType.Feature,
                    Title = "Dependent Feature",
                    Parents = ["core/feature/base"],
                    Status = FutureChangeStatus.InProgress
                }
            ]
        };

        // Act
        var json = RoadmapParser.Serialize(roadmap);
        var parsed = RoadmapParser.Parse(json);

        // Assert
        Assert.That(parsed.Version, Is.EqualTo("1.1"));
        Assert.That(parsed.Changes, Has.Count.EqualTo(2));
        Assert.That(parsed.Changes[0].Id, Is.EqualTo("core/feature/base"));
        Assert.That(parsed.Changes[0].ShortTitle, Is.EqualTo("base"));
        Assert.That(parsed.Changes[0].Status, Is.EqualTo(FutureChangeStatus.Pending));
        Assert.That(parsed.Changes[1].Parents, Has.Count.EqualTo(1));
        Assert.That(parsed.Changes[1].Parents[0], Is.EqualTo("core/feature/base"));
        Assert.That(parsed.Changes[1].Status, Is.EqualTo(FutureChangeStatus.InProgress));
    }

    [Test]
    public void RoadmapParser_V2Schema_SerializesParentsNotChildren()
    {
        // Arrange
        var roadmap = new Homespun.Features.Roadmap.Roadmap
        {
            Version = "1.1",
            Changes =
            [
                new FutureChange
                {
                    Id = "core/feature/test",
                    ShortTitle = "test",
                    Group = "core",
                    Type = ChangeType.Feature,
                    Title = "Test",
                    Parents = ["core/feature/parent"]
                }
            ]
        };

        // Act
        var json = RoadmapParser.Serialize(roadmap);

        // Assert
        Assert.That(json, Does.Contain("\"parents\""));
        Assert.That(json, Does.Not.Contain("\"children\""));
    }

    #endregion

    #region Time Calculation Tests (Flat List)

    [Test]
    public void RoadmapParser_V2Schema_CalculatesTimeFromDependencyDepth()
    {
        // Arrange - In flat list, time is based on dependency chain depth
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/root",
                    "shortTitle": "root",
                    "group": "core",
                    "type": "feature",
                    "title": "Root (no parents)",
                    "parents": []
                },
                {
                    "id": "core/feature/level1",
                    "shortTitle": "level1",
                    "group": "core",
                    "type": "feature",
                    "title": "Level 1 (depends on root)",
                    "parents": ["core/feature/root"]
                },
                {
                    "id": "core/feature/level2",
                    "shortTitle": "level2",
                    "group": "core",
                    "type": "feature",
                    "title": "Level 2 (depends on level1)",
                    "parents": ["core/feature/level1"]
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var changesWithTime = result.GetAllChangesWithTime();

        // Assert
        // Root (depth 0) -> t=2
        // Level1 (depth 1) -> t=3
        // Level2 (depth 2) -> t=4
        var root = changesWithTime.First(c => c.Change.ShortTitle == "root");
        var level1 = changesWithTime.First(c => c.Change.ShortTitle == "level1");
        var level2 = changesWithTime.First(c => c.Change.ShortTitle == "level2");

        Assert.That(root.Time, Is.EqualTo(2));
        Assert.That(level1.Time, Is.EqualTo(3));
        Assert.That(level2.Time, Is.EqualTo(4));
    }

    [Test]
    public void RoadmapParser_V2Schema_MultipleParentsUsesMaxDepth()
    {
        // Arrange - When a change has multiple parents, use the deepest path
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/root-a",
                    "shortTitle": "root-a",
                    "group": "core",
                    "type": "feature",
                    "title": "Root A",
                    "parents": []
                },
                {
                    "id": "core/feature/deep-chain",
                    "shortTitle": "deep-chain",
                    "group": "core",
                    "type": "feature",
                    "title": "Deep Chain",
                    "parents": ["core/feature/root-a"]
                },
                {
                    "id": "core/feature/root-b",
                    "shortTitle": "root-b",
                    "group": "core",
                    "type": "feature",
                    "title": "Root B",
                    "parents": []
                },
                {
                    "id": "core/feature/multi-parent",
                    "shortTitle": "multi-parent",
                    "group": "core",
                    "type": "feature",
                    "title": "Multi Parent (depends on deep-chain at depth 1 and root-b at depth 0)",
                    "parents": ["core/feature/deep-chain", "core/feature/root-b"]
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var changesWithTime = result.GetAllChangesWithTime();

        // Assert
        // multi-parent depends on deep-chain (depth 1) and root-b (depth 0)
        // Should use max depth + 1 = 2, so time = 2 + 2 = 4
        var multiParent = changesWithTime.First(c => c.Change.ShortTitle == "multi-parent");
        Assert.That(multiParent.Time, Is.EqualTo(4)); // depth 2 -> t=4
    }

    #endregion
}
