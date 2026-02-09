namespace Homespun.Features.Testing;

/// <summary>
/// Configuration options for mock mode.
/// </summary>
public class MockModeOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "MockMode";

    /// <summary>
    /// Gets or sets whether mock mode is enabled.
    /// When true, mock services are used instead of real implementations.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to seed demo data on startup.
    /// </summary>
    public bool SeedData { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to use live Claude Code sessions in mock mode.
    /// When true, the real ClaudeSessionService is used with a test working directory,
    /// allowing testing of Claude interactions while keeping other services mocked.
    /// </summary>
    public bool UseLiveClaudeSessions { get; set; } = false;

    /// <summary>
    /// Gets or sets the working directory to use for live Claude sessions.
    /// Defaults to a "test-workspace" directory in the current working directory.
    /// </summary>
    public string? LiveClaudeSessionsWorkingDirectory { get; set; }
}
