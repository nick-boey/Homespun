using Homespun.Features.Roadmap;

namespace Homespun.Tests.Features.Roadmap;

[TestFixture]
public class RoadmapParserTests
{
    [Test]
    public void RoadmapParser_ValidJson_ParsesAllChanges()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "lastUpdated": "2024-01-15T10:00:00Z",
            "changes": [
                {
                    "id": "core/feature/feature-one",
                    "shortTitle": "feature-one",
                    "group": "core",
                    "type": "feature",
                    "title": "First Feature",
                    "parents": []
                },
                {
                    "id": "web/bug/bug-fix",
                    "shortTitle": "bug-fix",
                    "group": "web",
                    "type": "bug",
                    "title": "Bug Fix",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Version, Is.EqualTo("1.1"));
        Assert.That(result.Changes, Has.Count.EqualTo(2));
        Assert.That(result.Changes[0].Id, Is.EqualTo("core/feature/feature-one"));
        Assert.That(result.Changes[0].ShortTitle, Is.EqualTo("feature-one"));
        Assert.That(result.Changes[0].Group, Is.EqualTo("core"));
        Assert.That(result.Changes[0].Type, Is.EqualTo(ChangeType.Feature));
        Assert.That(result.Changes[0].Title, Is.EqualTo("First Feature"));
        Assert.That(result.Changes[1].Id, Is.EqualTo("web/bug/bug-fix"));
        Assert.That(result.Changes[1].Group, Is.EqualTo("web"));
        Assert.That(result.Changes[1].Type, Is.EqualTo(ChangeType.Bug));
    }

    [Test]
    public void RoadmapParser_InvalidJson_ThrowsValidationException()
    {
        // Arrange
        var invalidJson = "{ this is not valid json }";

        // Act & Assert
        Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(invalidJson));
    }

    [Test]
    public void RoadmapParser_MissingRequiredFields_ThrowsValidationException()
    {
        // Arrange - Missing 'id' field
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "shortTitle": "test",
                    "group": "core",
                    "type": "feature",
                    "title": "Missing ID",
                    "parents": []
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("id"));
    }

    [Test]
    public void RoadmapParser_MissingVersion_ThrowsValidationException()
    {
        // Arrange
        var json = """
        {
            "changes": [
                {
                    "id": "core/feature/test",
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
        Assert.That(ex.Message, Does.Contain("version"));
    }

    [Test]
    public void RoadmapParser_ParsesParentReferences()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/parent",
                    "shortTitle": "parent",
                    "group": "core",
                    "type": "feature",
                    "title": "Parent Feature",
                    "parents": []
                },
                {
                    "id": "core/feature/child-1",
                    "shortTitle": "child-1",
                    "group": "core",
                    "type": "feature",
                    "title": "Child 1",
                    "parents": ["core/feature/parent"]
                },
                {
                    "id": "core/bug/child-2",
                    "shortTitle": "child-2",
                    "group": "core",
                    "type": "bug",
                    "title": "Child 2",
                    "parents": ["core/feature/parent"]
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes, Has.Count.EqualTo(3));

        var parent = result.Changes[0];
        Assert.That(parent.Id, Is.EqualTo("core/feature/parent"));
        Assert.That(parent.Parents, Is.Empty);

        var child1 = result.Changes[1];
        Assert.That(child1.Id, Is.EqualTo("core/feature/child-1"));
        Assert.That(child1.Parents, Has.Count.EqualTo(1));
        Assert.That(child1.Parents[0], Is.EqualTo("core/feature/parent"));

        var child2 = result.Changes[2];
        Assert.That(child2.Id, Is.EqualTo("core/bug/child-2"));
        Assert.That(child2.Type, Is.EqualTo(ChangeType.Bug));
    }

    [Test]
    public void RoadmapParser_CalculatesTimeFromDependencyDepth()
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
                },
                {
                    "id": "core/feature/child",
                    "shortTitle": "child",
                    "group": "core",
                    "type": "feature",
                    "title": "Child",
                    "parents": ["core/feature/root"]
                },
                {
                    "id": "core/feature/grandchild",
                    "shortTitle": "grandchild",
                    "group": "core",
                    "type": "feature",
                    "title": "Grandchild",
                    "parents": ["core/feature/child"]
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var flatChanges = result.GetAllChangesWithTime();

        // Assert - Root at depth 0 -> t=2, child at depth 1 -> t=3, grandchild at depth 2 -> t=4
        Assert.That(flatChanges.First(c => c.Change.ShortTitle == "root").Time, Is.EqualTo(2));
        Assert.That(flatChanges.First(c => c.Change.ShortTitle == "child").Time, Is.EqualTo(3));
        Assert.That(flatChanges.First(c => c.Change.ShortTitle == "grandchild").Time, Is.EqualTo(4));
    }

    [Test]
    public void RoadmapParser_ParsesOptionalFields()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/full-feature",
                    "shortTitle": "full-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Full Feature",
                    "description": "A detailed description",
                    "instructions": "Implementation instructions for the agent",
                    "priority": "High",
                    "estimatedComplexity": "Large",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var change = result.Changes[0];

        // Assert
        Assert.That(change.Description, Is.EqualTo("A detailed description"));
        Assert.That(change.Instructions, Is.EqualTo("Implementation instructions for the agent"));
        Assert.That(change.Priority, Is.EqualTo(Priority.High));
        Assert.That(change.EstimatedComplexity, Is.EqualTo(Complexity.Large));
    }

    [Test]
    public void RoadmapParser_InvalidShortTitlePattern_ThrowsValidationException()
    {
        // Arrange - shortTitle with invalid characters (uppercase)
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/Invalid-ID",
                    "shortTitle": "Invalid-ID",
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
        Assert.That(ex.Message, Does.Contain("shortTitle").Or.Contain("pattern"));
    }

    [Test]
    public void RoadmapParser_InvalidType_ThrowsValidationException()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/invalid-type/test",
                    "shortTitle": "test",
                    "group": "core",
                    "type": "invalid-type",
                    "title": "Test",
                    "parents": []
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("type").Or.Contain("JSON"));
    }

    [Test]
    public void RoadmapParser_EmptyChanges_IsValid()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": []
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes, Is.Empty);
    }

    [Test]
    public void RoadmapParser_IdIsBranchName()
    {
        // Arrange
        var json = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/pr-time-dimension",
                    "shortTitle": "pr-time-dimension",
                    "group": "core",
                    "type": "feature",
                    "title": "PR Time Dimension",
                    "parents": []
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var change = result.Changes[0];

        // Assert - The Id IS the branch name
        Assert.That(change.Id, Is.EqualTo("core/feature/pr-time-dimension"));
    }

    [Test]
    public void RoadmapParser_Serialize_RoundTrips()
    {
        // Arrange
        var roadmap = new Homespun.Features.Roadmap.Roadmap
        {
            Version = "1.1",
            LastUpdated = DateTime.UtcNow,
            Changes =
            [
                new RoadmapChange
                {
                    Id = "core/feature/test-feature",
                    ShortTitle = "test-feature",
                    Group = "core",
                    Type = ChangeType.Feature,
                    Title = "Test Feature",
                    Description = "Description",
                    Priority = Priority.High,
                    Parents = []
                },
                new RoadmapChange
                {
                    Id = "core/bug/child-feature",
                    ShortTitle = "child-feature",
                    Group = "core",
                    Type = ChangeType.Bug,
                    Title = "Child Feature",
                    Parents = ["core/feature/test-feature"]
                }
            ]
        };

        // Act
        var json = RoadmapParser.Serialize(roadmap);
        var parsed = RoadmapParser.Parse(json);

        // Assert
        Assert.That(parsed.Version, Is.EqualTo(roadmap.Version));
        Assert.That(parsed.Changes, Has.Count.EqualTo(2));
        Assert.That(parsed.Changes[0].Id, Is.EqualTo("core/feature/test-feature"));
        Assert.That(parsed.Changes[1].Id, Is.EqualTo("core/bug/child-feature"));
        Assert.That(parsed.Changes[1].Parents, Has.Count.EqualTo(1));
        Assert.That(parsed.Changes[1].Parents[0], Is.EqualTo("core/feature/test-feature"));
    }
}
