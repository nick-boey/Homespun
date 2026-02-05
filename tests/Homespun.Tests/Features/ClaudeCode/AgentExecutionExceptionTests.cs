using Homespun.Features.ClaudeCode.Exceptions;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for agent execution exception types.
/// </summary>
[TestFixture]
public class AgentExecutionExceptionTests
{
    #region AgentExecutionException Tests

    [Test]
    public void AgentExecutionException_WithMessage_SetsProperties()
    {
        // Act
        var ex = new AgentExecutionException("Test error");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Test error"));
            Assert.That(ex.SessionId, Is.Null);
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    [Test]
    public void AgentExecutionException_WithSessionId_SetsProperties()
    {
        // Act
        var ex = new AgentExecutionException("Test error", "session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Test error"));
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    [Test]
    public void AgentExecutionException_WithRetryable_SetsProperties()
    {
        // Act
        var ex = new AgentExecutionException("Test error", "session-123", isRetryable: true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Test error"));
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.IsRetryable, Is.True);
        });
    }

    [Test]
    public void AgentExecutionException_WithInnerException_SetsProperties()
    {
        // Arrange
        var innerEx = new InvalidOperationException("Inner error");

        // Act
        var ex = new AgentExecutionException("Test error", innerEx, "session-123", isRetryable: true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Test error"));
            Assert.That(ex.InnerException, Is.EqualTo(innerEx));
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.IsRetryable, Is.True);
        });
    }

    #endregion

    #region AgentStartupException Tests

    [Test]
    public void AgentStartupException_IsRetryable()
    {
        // Act
        var ex = new AgentStartupException("Container failed to start");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Container failed to start"));
            Assert.That(ex.IsRetryable, Is.True);
        });
    }

    [Test]
    public void AgentStartupException_WithSessionId_SetsProperties()
    {
        // Act
        var ex = new AgentStartupException("Container failed to start", "session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.IsRetryable, Is.True);
        });
    }

    [Test]
    public void AgentStartupException_WithInnerException_SetsProperties()
    {
        // Arrange
        var innerEx = new Exception("Docker error");

        // Act
        var ex = new AgentStartupException("Container failed to start", innerEx, "session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.InnerException, Is.EqualTo(innerEx));
            Assert.That(ex.IsRetryable, Is.True);
        });
    }

    #endregion

    #region AgentConnectionLostException Tests

    [Test]
    public void AgentConnectionLostException_IsRetryable()
    {
        // Act
        var ex = new AgentConnectionLostException("Connection lost");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Connection lost"));
            Assert.That(ex.IsRetryable, Is.True);
        });
    }

    [Test]
    public void AgentConnectionLostException_WithSessionId_SetsProperties()
    {
        // Act
        var ex = new AgentConnectionLostException("Connection lost", "session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.IsRetryable, Is.True);
        });
    }

    [Test]
    public void AgentConnectionLostException_WithInnerException_SetsProperties()
    {
        // Arrange
        var innerEx = new Exception("Network error");

        // Act
        var ex = new AgentConnectionLostException("Connection lost", innerEx, "session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.InnerException, Is.EqualTo(innerEx));
            Assert.That(ex.IsRetryable, Is.True);
        });
    }

    #endregion

    #region AgentTimeoutException Tests

    [Test]
    public void AgentTimeoutException_IsNotRetryable()
    {
        // Act
        var ex = new AgentTimeoutException("Session timed out", TimeSpan.FromMinutes(30));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Session timed out"));
            Assert.That(ex.IsRetryable, Is.False);
            Assert.That(ex.Timeout, Is.EqualTo(TimeSpan.FromMinutes(30)));
        });
    }

    [Test]
    public void AgentTimeoutException_WithSessionId_SetsProperties()
    {
        // Act
        var ex = new AgentTimeoutException("Session timed out", TimeSpan.FromMinutes(30), "session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.Timeout, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    #endregion

    #region ClaudeCliException Tests

    [Test]
    public void ClaudeCliException_IsNotRetryable()
    {
        // Act
        var ex = new ClaudeCliException("Claude CLI error");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Claude CLI error"));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    [Test]
    public void ClaudeCliException_WithExitCode_SetsProperties()
    {
        // Act
        var ex = new ClaudeCliException("Claude CLI error", exitCode: 1, stdErr: "Error output");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.ExitCode, Is.EqualTo(1));
            Assert.That(ex.StdErr, Is.EqualTo("Error output"));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    [Test]
    public void ClaudeCliException_WithAllProperties_SetsProperties()
    {
        // Act
        var ex = new ClaudeCliException(
            "Claude CLI error",
            exitCode: 127,
            stdErr: "Command not found",
            sessionId: "session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Claude CLI error"));
            Assert.That(ex.ExitCode, Is.EqualTo(127));
            Assert.That(ex.StdErr, Is.EqualTo("Command not found"));
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    [Test]
    public void ClaudeCliException_WithInnerException_SetsProperties()
    {
        // Arrange
        var innerEx = new Exception("Process error");

        // Act
        var ex = new ClaudeCliException(
            "Claude CLI error",
            innerEx,
            exitCode: 1,
            stdErr: "Error",
            sessionId: "session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.InnerException, Is.EqualTo(innerEx));
            Assert.That(ex.ExitCode, Is.EqualTo(1));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    #endregion

    #region AgentSessionNotFoundException Tests

    [Test]
    public void AgentSessionNotFoundException_FormatsMessage()
    {
        // Act
        var ex = new AgentSessionNotFoundException("session-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Session session-123 not found"));
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    #endregion

    #region AgentSessionStateException Tests

    [Test]
    public void AgentSessionStateException_FormatsMessage()
    {
        // Act
        var ex = new AgentSessionStateException("session-123", "Stopped", "Running");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("Session session-123 is in state 'Stopped' but expected 'Running'"));
            Assert.That(ex.SessionId, Is.EqualTo("session-123"));
            Assert.That(ex.CurrentState, Is.EqualTo("Stopped"));
            Assert.That(ex.ExpectedState, Is.EqualTo("Running"));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    #endregion

    #region Exception Hierarchy Tests

    [Test]
    public void AgentStartupException_IsAgentExecutionException()
    {
        // Act
        var ex = new AgentStartupException("Error");

        // Assert
        Assert.That(ex, Is.InstanceOf<AgentExecutionException>());
    }

    [Test]
    public void AgentConnectionLostException_IsAgentExecutionException()
    {
        // Act
        var ex = new AgentConnectionLostException("Error");

        // Assert
        Assert.That(ex, Is.InstanceOf<AgentExecutionException>());
    }

    [Test]
    public void AgentTimeoutException_IsAgentExecutionException()
    {
        // Act
        var ex = new AgentTimeoutException("Error", TimeSpan.Zero);

        // Assert
        Assert.That(ex, Is.InstanceOf<AgentExecutionException>());
    }

    [Test]
    public void ClaudeCliException_IsAgentExecutionException()
    {
        // Act
        var ex = new ClaudeCliException("Error");

        // Assert
        Assert.That(ex, Is.InstanceOf<AgentExecutionException>());
    }

    [Test]
    public void AgentSessionNotFoundException_IsAgentExecutionException()
    {
        // Act
        var ex = new AgentSessionNotFoundException("session-123");

        // Assert
        Assert.That(ex, Is.InstanceOf<AgentExecutionException>());
    }

    [Test]
    public void AgentSessionStateException_IsAgentExecutionException()
    {
        // Act
        var ex = new AgentSessionStateException("session-123", "A", "B");

        // Assert
        Assert.That(ex, Is.InstanceOf<AgentExecutionException>());
    }

    #endregion
}
