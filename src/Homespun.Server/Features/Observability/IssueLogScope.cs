namespace Homespun.Features.Observability;

/// <summary>
/// Helper class for creating logging scopes that include issue context.
/// Use with ILogger.BeginScope to include IssueId and ProjectName in log entries.
/// </summary>
public static class IssueLogScope
{
    /// <summary>
    /// Key used to store the IssueId in the logging scope.
    /// </summary>
    public const string IssueIdKey = "IssueId";

    /// <summary>
    /// Key used to store the ProjectName in the logging scope.
    /// </summary>
    public const string ProjectNameKey = "ProjectName";

    /// <summary>
    /// Creates a logging scope with issue context that will be included in all log entries
    /// within the scope.
    /// </summary>
    /// <param name="logger">The logger to create the scope on.</param>
    /// <param name="issueId">The issue ID to include in logs. If null or empty, the scope is still created but without IssueId.</param>
    /// <param name="projectName">Optional project name to include in logs.</param>
    /// <returns>A disposable scope that should be used in a using statement.</returns>
    /// <example>
    /// <code>
    /// using (IssueLogScope.BeginIssueScope(_logger, issueId, projectName))
    /// {
    ///     _logger.LogInformation("Processing issue");
    ///     // All logs within this block will include IssueId and ProjectName
    /// }
    /// </code>
    /// </example>
    public static IDisposable? BeginIssueScope(ILogger logger, string? issueId, string? projectName = null)
    {
        var scopeState = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(issueId))
        {
            scopeState[IssueIdKey] = issueId;
        }

        if (!string.IsNullOrEmpty(projectName))
        {
            scopeState[ProjectNameKey] = projectName;
        }

        if (scopeState.Count == 0)
        {
            return null;
        }

        return logger.BeginScope(scopeState);
    }
}
