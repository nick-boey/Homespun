namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents information about a file that has changed in the current branch.
/// </summary>
public class FileChangeInfo
{
    /// <summary>
    /// The relative path to the changed file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The number of lines added to the file.
    /// </summary>
    public int Additions { get; init; }

    /// <summary>
    /// The number of lines deleted from the file.
    /// </summary>
    public int Deletions { get; init; }

    /// <summary>
    /// The type of change made to the file.
    /// </summary>
    public FileChangeStatus Status { get; init; }
}

/// <summary>
/// Status values indicating the type of change to a file.
/// </summary>
public enum FileChangeStatus
{
    /// <summary>
    /// A new file was added.
    /// </summary>
    Added,

    /// <summary>
    /// An existing file was modified.
    /// </summary>
    Modified,

    /// <summary>
    /// A file was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// A file was renamed.
    /// </summary>
    Renamed
}
