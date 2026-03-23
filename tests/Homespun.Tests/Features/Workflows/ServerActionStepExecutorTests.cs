using System.Text.Json;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class ServerActionStepExecutorTests
{
    private Mock<ILogger<ServerActionStepExecutor>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<ServerActionStepExecutor>>();
    }

    [Test]
    public async Task ExecuteAsync_NoConfig_CompletesImmediately()
    {
        // Arrange
        var executor = new ServerActionStepExecutor([], _mockLogger.Object);
        var step = new WorkflowStep { Id = "step-1", Name = "No Config" };
        var context = new WorkflowContext();

        // Act
        var result = await executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_NullConfig_CompletesImmediately()
    {
        // Arrange
        var executor = new ServerActionStepExecutor([], _mockLogger.Object);
        var step = new WorkflowStep { Id = "step-1", Name = "Null Config", Config = null };
        var context = new WorkflowContext();

        // Act
        var result = await executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_ConfigWithoutActionType_CompletesImmediately()
    {
        // Arrange
        var executor = new ServerActionStepExecutor([], _mockLogger.Object);
        var step = new WorkflowStep
        {
            Id = "step-1",
            Name = "No Action Type",
            Config = JsonSerializer.SerializeToElement(new { someOtherProp = "value" })
        };
        var context = new WorkflowContext();

        // Act
        var result = await executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_KnownActionType_DelegatesToHandler()
    {
        // Arrange
        var mockHandler = new Mock<IServerActionHandler>();
        mockHandler.Setup(h => h.ActionType).Returns("test_action");
        mockHandler.Setup(h => h.ExecuteAsync(It.IsAny<WorkflowStep>(), It.IsAny<WorkflowContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StepResult.Completed(new Dictionary<string, object> { ["handled"] = true }));

        var executor = new ServerActionStepExecutor([mockHandler.Object], _mockLogger.Object);
        var step = new WorkflowStep
        {
            Id = "step-1",
            Name = "Test Action",
            Config = JsonSerializer.SerializeToElement(new { actionType = "test_action" })
        };
        var context = new WorkflowContext();

        // Act
        var result = await executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Output!["handled"], Is.EqualTo(true));
        });

        mockHandler.Verify(h => h.ExecuteAsync(step, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_UnknownActionType_ReturnsFailed()
    {
        // Arrange
        var mockHandler = new Mock<IServerActionHandler>();
        mockHandler.Setup(h => h.ActionType).Returns("known_action");

        var executor = new ServerActionStepExecutor([mockHandler.Object], _mockLogger.Object);
        var step = new WorkflowStep
        {
            Id = "step-1",
            Name = "Unknown Action",
            Config = JsonSerializer.SerializeToElement(new { actionType = "unknown_action" })
        };
        var context = new WorkflowContext();

        // Act
        var result = await executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Unknown server action type 'unknown_action'"));
            Assert.That(result.ErrorMessage, Does.Contain("known_action"));
        });
    }

    [Test]
    public async Task ExecuteAsync_ActionTypeCaseInsensitive_DelegatesToHandler()
    {
        // Arrange
        var mockHandler = new Mock<IServerActionHandler>();
        mockHandler.Setup(h => h.ActionType).Returns("ci_merge");
        mockHandler.Setup(h => h.ExecuteAsync(It.IsAny<WorkflowStep>(), It.IsAny<WorkflowContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StepResult.Completed());

        var executor = new ServerActionStepExecutor([mockHandler.Object], _mockLogger.Object);
        var step = new WorkflowStep
        {
            Id = "step-1",
            Name = "CI Merge",
            Config = JsonSerializer.SerializeToElement(new { actionType = "CI_MERGE" })
        };
        var context = new WorkflowContext();

        // Act
        var result = await executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        mockHandler.Verify(h => h.ExecuteAsync(step, context, It.IsAny<CancellationToken>()), Times.Once);
    }
}
