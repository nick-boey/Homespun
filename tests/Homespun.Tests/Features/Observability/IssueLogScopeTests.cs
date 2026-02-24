using Homespun.Features.Observability;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class IssueLogScopeTests
{
    private Mock<ILogger> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger>();
    }

    [Test]
    public void BeginIssueScope_ReturnsScope_WhenIssueIdIsProvided()
    {
        // Arrange
        var capturedScope = new Dictionary<string, object?>();
        _mockLogger.Setup(l => l.BeginScope(It.IsAny<Dictionary<string, object?>>()))
            .Callback<Dictionary<string, object?>>(scope => capturedScope = scope)
            .Returns(Mock.Of<IDisposable>());

        // Act
        var result = IssueLogScope.BeginIssueScope(_mockLogger.Object, "ABC123");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(capturedScope.ContainsKey(IssueLogScope.IssueIdKey), Is.True);
        Assert.That(capturedScope[IssueLogScope.IssueIdKey], Is.EqualTo("ABC123"));
    }

    [Test]
    public void BeginIssueScope_IncludesProjectName_WhenProvided()
    {
        // Arrange
        var capturedScope = new Dictionary<string, object?>();
        _mockLogger.Setup(l => l.BeginScope(It.IsAny<Dictionary<string, object?>>()))
            .Callback<Dictionary<string, object?>>(scope => capturedScope = scope)
            .Returns(Mock.Of<IDisposable>());

        // Act
        var result = IssueLogScope.BeginIssueScope(_mockLogger.Object, "DEF456", "TestProject");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(capturedScope.ContainsKey(IssueLogScope.IssueIdKey), Is.True);
        Assert.That(capturedScope[IssueLogScope.IssueIdKey], Is.EqualTo("DEF456"));
        Assert.That(capturedScope.ContainsKey(IssueLogScope.ProjectNameKey), Is.True);
        Assert.That(capturedScope[IssueLogScope.ProjectNameKey], Is.EqualTo("TestProject"));
    }

    [Test]
    public void BeginIssueScope_ReturnsNull_WhenIssueIdIsNull()
    {
        // Act
        var result = IssueLogScope.BeginIssueScope(_mockLogger.Object, null);

        // Assert
        Assert.That(result, Is.Null);
        _mockLogger.Verify(l => l.BeginScope(It.IsAny<Dictionary<string, object?>>()), Times.Never);
    }

    [Test]
    public void BeginIssueScope_ReturnsNull_WhenIssueIdIsEmpty()
    {
        // Act
        var result = IssueLogScope.BeginIssueScope(_mockLogger.Object, "");

        // Assert
        Assert.That(result, Is.Null);
        _mockLogger.Verify(l => l.BeginScope(It.IsAny<Dictionary<string, object?>>()), Times.Never);
    }

    [Test]
    public void BeginIssueScope_OmitsProjectName_WhenEmpty()
    {
        // Arrange
        var capturedScope = new Dictionary<string, object?>();
        _mockLogger.Setup(l => l.BeginScope(It.IsAny<Dictionary<string, object?>>()))
            .Callback<Dictionary<string, object?>>(scope => capturedScope = scope)
            .Returns(Mock.Of<IDisposable>());

        // Act
        var result = IssueLogScope.BeginIssueScope(_mockLogger.Object, "XYZ789", "");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(capturedScope.ContainsKey(IssueLogScope.IssueIdKey), Is.True);
        Assert.That(capturedScope.ContainsKey(IssueLogScope.ProjectNameKey), Is.False);
    }

    [Test]
    public void IssueIdKey_HasExpectedValue()
    {
        Assert.That(IssueLogScope.IssueIdKey, Is.EqualTo("IssueId"));
    }

    [Test]
    public void ProjectNameKey_HasExpectedValue()
    {
        Assert.That(IssueLogScope.ProjectNameKey, Is.EqualTo("ProjectName"));
    }
}
