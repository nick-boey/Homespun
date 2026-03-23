using Homespun.Features.Workflows.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class WorkflowSessionCallbackTests
{
    private WorkflowSessionCallback _callback = null!;
    private Mock<IWorkflowExecutionService> _mockExecutionService = null!;
    private Mock<ILogger<WorkflowSessionCallback>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockExecutionService = new Mock<IWorkflowExecutionService>();
        _mockLogger = new Mock<ILogger<WorkflowSessionCallback>>();
        _callback = new WorkflowSessionCallback(_mockExecutionService.Object, _mockLogger.Object);
    }

    #region RegisterSession / GetSessionContext Tests

    [Test]
    public void RegisterSession_StoresContext()
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        _callback.RegisterSession("session-1", context);

        // Assert
        var result = _callback.GetSessionContext("session-1");
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ExecutionId, Is.EqualTo("exec-1"));
            Assert.That(result.StepId, Is.EqualTo("step-1"));
            Assert.That(result.WorkflowId, Is.EqualTo("workflow-1"));
            Assert.That(result.ProjectPath, Is.EqualTo("/test/project"));
        });
    }

    [Test]
    public void GetSessionContext_UnregisteredSession_ReturnsNull()
    {
        // Act
        var result = _callback.GetSessionContext("non-existent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void UnregisterSession_RemovesContext()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        // Act
        _callback.UnregisterSession("session-1");

        // Assert
        Assert.That(_callback.GetSessionContext("session-1"), Is.Null);
    }

    [Test]
    public void IsWorkflowSession_RegisteredSession_ReturnsTrue()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        // Act & Assert
        Assert.That(_callback.IsWorkflowSession("session-1"), Is.True);
    }

    [Test]
    public void IsWorkflowSession_UnregisteredSession_ReturnsFalse()
    {
        // Act & Assert
        Assert.That(_callback.IsWorkflowSession("non-existent"), Is.False);
    }

    #endregion

    #region HandleWorkflowSignalAsync Tests

    [Test]
    public async Task HandleWorkflowSignalAsync_SuccessSignal_CallsOnStepCompleted()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        var signal = new WorkflowSignalResult
        {
            Status = "success",
            Data = new Dictionary<string, object> { ["result"] = "done" },
            Message = "Step completed successfully"
        };

        // Act
        await _callback.HandleWorkflowSignalAsync("session-1", signal);

        // Assert
        _mockExecutionService.Verify(s => s.OnStepCompletedAsync(
            "/test/project",
            "exec-1",
            "step-1",
            It.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("result") && d["result"].ToString() == "done" &&
                d.ContainsKey("message") && d["message"].ToString() == "Step completed successfully"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleWorkflowSignalAsync_FailSignal_CallsOnStepFailed()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        var signal = new WorkflowSignalResult
        {
            Status = "fail",
            Message = "Something went wrong"
        };

        // Act
        await _callback.HandleWorkflowSignalAsync("session-1", signal);

        // Assert
        _mockExecutionService.Verify(s => s.OnStepFailedAsync(
            "/test/project",
            "exec-1",
            "step-1",
            "Something went wrong",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleWorkflowSignalAsync_FailSignalWithoutMessage_UsesDefaultMessage()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        var signal = new WorkflowSignalResult
        {
            Status = "fail"
        };

        // Act
        await _callback.HandleWorkflowSignalAsync("session-1", signal);

        // Assert
        _mockExecutionService.Verify(s => s.OnStepFailedAsync(
            "/test/project",
            "exec-1",
            "step-1",
            "Step reported failure via workflow_signal",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleWorkflowSignalAsync_SuccessWithNullData_PassesMessageOnly()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        var signal = new WorkflowSignalResult
        {
            Status = "success",
            Message = "Done"
        };

        // Act
        await _callback.HandleWorkflowSignalAsync("session-1", signal);

        // Assert
        _mockExecutionService.Verify(s => s.OnStepCompletedAsync(
            "/test/project",
            "exec-1",
            "step-1",
            It.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("message") && d["message"].ToString() == "Done"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleWorkflowSignalAsync_UnregisteredSession_DoesNotCallExecutionService()
    {
        // Arrange
        var signal = new WorkflowSignalResult { Status = "success" };

        // Act
        await _callback.HandleWorkflowSignalAsync("non-existent", signal);

        // Assert
        _mockExecutionService.Verify(s => s.OnStepCompletedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockExecutionService.Verify(s => s.OnStepFailedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleWorkflowSignalAsync_UnregistersSessionAfterSignal()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        var signal = new WorkflowSignalResult { Status = "success" };

        // Act
        await _callback.HandleWorkflowSignalAsync("session-1", signal);

        // Assert - session should be unregistered after signaling
        Assert.That(_callback.IsWorkflowSession("session-1"), Is.False);
    }

    #endregion

    #region HandleSessionCompletedAsync Tests

    [Test]
    public async Task HandleSessionCompletedAsync_WorkflowSession_CallsOnStepCompleted()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        // Act
        await _callback.HandleSessionCompletedAsync("session-1");

        // Assert
        _mockExecutionService.Verify(s => s.OnStepCompletedAsync(
            "/test/project",
            "exec-1",
            "step-1",
            It.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("message") && d["message"].ToString()!.Contains("completed normally")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleSessionCompletedAsync_NonWorkflowSession_DoesNothing()
    {
        // Act
        await _callback.HandleSessionCompletedAsync("non-existent");

        // Assert
        _mockExecutionService.Verify(s => s.OnStepCompletedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleSessionCompletedAsync_UnregistersSessionAfter()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        // Act
        await _callback.HandleSessionCompletedAsync("session-1");

        // Assert
        Assert.That(_callback.IsWorkflowSession("session-1"), Is.False);
    }

    [Test]
    public async Task HandleSessionCompletedAsync_AlreadySignaled_DoesNotCallAgain()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        // Signal first
        var signal = new WorkflowSignalResult { Status = "success" };
        await _callback.HandleWorkflowSignalAsync("session-1", signal);

        // Act - session completed after signal already sent
        await _callback.HandleSessionCompletedAsync("session-1");

        // Assert - OnStepCompleted should only be called once (from the signal)
        _mockExecutionService.Verify(s => s.OnStepCompletedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region HandleSessionFailedAsync Tests

    [Test]
    public async Task HandleSessionFailedAsync_WorkflowSession_CallsOnStepFailed()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        // Act
        await _callback.HandleSessionFailedAsync("session-1", "Session crashed");

        // Assert
        _mockExecutionService.Verify(s => s.OnStepFailedAsync(
            "/test/project",
            "exec-1",
            "step-1",
            "Session crashed",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleSessionFailedAsync_NonWorkflowSession_DoesNothing()
    {
        // Act
        await _callback.HandleSessionFailedAsync("non-existent", "Error");

        // Assert
        _mockExecutionService.Verify(s => s.OnStepFailedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleSessionFailedAsync_UnregistersSessionAfter()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        // Act
        await _callback.HandleSessionFailedAsync("session-1", "Error");

        // Assert
        Assert.That(_callback.IsWorkflowSession("session-1"), Is.False);
    }

    [Test]
    public async Task HandleSessionFailedAsync_AlreadySignaled_DoesNotCallAgain()
    {
        // Arrange
        _callback.RegisterSession("session-1", CreateTestContext());

        // Signal success first
        var signal = new WorkflowSignalResult { Status = "success" };
        await _callback.HandleWorkflowSignalAsync("session-1", signal);

        // Act - session fails after signal already sent
        await _callback.HandleSessionFailedAsync("session-1", "Late error");

        // Assert - OnStepFailed should not be called (signal already handled it)
        _mockExecutionService.Verify(s => s.OnStepFailedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Helper Methods

    private static WorkflowSessionContext CreateTestContext()
    {
        return new WorkflowSessionContext
        {
            ExecutionId = "exec-1",
            StepId = "step-1",
            WorkflowId = "workflow-1",
            ProjectPath = "/test/project"
        };
    }

    #endregion
}
