namespace Homespun.Features.ClaudeCode.Exceptions;

/// <summary>
/// Base exception for agent execution errors.
/// </summary>
public class AgentExecutionException : Exception
{
    /// <summary>
    /// The session ID associated with this error, if known.
    /// </summary>
    public string? SessionId { get; }

    /// <summary>
    /// Whether this error is potentially recoverable by retrying.
    /// </summary>
    public bool IsRetryable { get; }

    public AgentExecutionException(string message, string? sessionId = null, bool isRetryable = false)
        : base(message)
    {
        SessionId = sessionId;
        IsRetryable = isRetryable;
    }

    public AgentExecutionException(string message, Exception innerException, string? sessionId = null, bool isRetryable = false)
        : base(message, innerException)
    {
        SessionId = sessionId;
        IsRetryable = isRetryable;
    }
}

/// <summary>
/// Exception thrown when an agent container fails to start.
/// </summary>
public class AgentStartupException : AgentExecutionException
{
    public AgentStartupException(string message, string? sessionId = null)
        : base(message, sessionId, isRetryable: true)
    {
    }

    public AgentStartupException(string message, Exception innerException, string? sessionId = null)
        : base(message, innerException, sessionId, isRetryable: true)
    {
    }
}

/// <summary>
/// Exception thrown when connection to an agent is lost.
/// </summary>
public class AgentConnectionLostException : AgentExecutionException
{
    public AgentConnectionLostException(string message, string? sessionId = null)
        : base(message, sessionId, isRetryable: true)
    {
    }

    public AgentConnectionLostException(string message, Exception innerException, string? sessionId = null)
        : base(message, innerException, sessionId, isRetryable: true)
    {
    }
}

/// <summary>
/// Exception thrown when an agent session times out.
/// </summary>
public class AgentTimeoutException : AgentExecutionException
{
    /// <summary>
    /// The timeout duration that was exceeded.
    /// </summary>
    public TimeSpan Timeout { get; }

    public AgentTimeoutException(string message, TimeSpan timeout, string? sessionId = null)
        : base(message, sessionId, isRetryable: false)
    {
        Timeout = timeout;
    }
}

/// <summary>
/// Exception thrown when the Claude CLI returns an error.
/// </summary>
public class ClaudeCliException : AgentExecutionException
{
    /// <summary>
    /// The exit code from the CLI, if available.
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>
    /// The stderr output from the CLI, if available.
    /// </summary>
    public string? StdErr { get; }

    public ClaudeCliException(string message, int? exitCode = null, string? stdErr = null, string? sessionId = null)
        : base(message, sessionId, isRetryable: false)
    {
        ExitCode = exitCode;
        StdErr = stdErr;
    }

    public ClaudeCliException(string message, Exception innerException, int? exitCode = null, string? stdErr = null, string? sessionId = null)
        : base(message, innerException, sessionId, isRetryable: false)
    {
        ExitCode = exitCode;
        StdErr = stdErr;
    }
}

/// <summary>
/// Exception thrown when a session is not found.
/// </summary>
public class AgentSessionNotFoundException : AgentExecutionException
{
    public AgentSessionNotFoundException(string sessionId)
        : base($"Session {sessionId} not found", sessionId, isRetryable: false)
    {
    }
}

/// <summary>
/// Exception thrown when a session is in an invalid state for the requested operation.
/// </summary>
public class AgentSessionStateException : AgentExecutionException
{
    /// <summary>
    /// The current state of the session.
    /// </summary>
    public string CurrentState { get; }

    /// <summary>
    /// The expected state for the operation.
    /// </summary>
    public string ExpectedState { get; }

    public AgentSessionStateException(string sessionId, string currentState, string expectedState)
        : base($"Session {sessionId} is in state '{currentState}' but expected '{expectedState}'", sessionId, isRetryable: false)
    {
        CurrentState = currentState;
        ExpectedState = expectedState;
    }
}
