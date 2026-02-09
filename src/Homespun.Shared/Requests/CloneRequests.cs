namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating a clone.
/// </summary>
public class CreateCloneRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public required string BranchName { get; set; }

    /// <summary>
    /// Whether to create a new branch.
    /// </summary>
    public bool CreateBranch { get; set; }

    /// <summary>
    /// Base branch for the new branch (if creating).
    /// </summary>
    public string? BaseBranch { get; set; }
}

/// <summary>
/// Response model for creating a clone.
/// </summary>
public class CreateCloneResponse
{
    /// <summary>
    /// The path to the created clone.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public required string BranchName { get; set; }
}

/// <summary>
/// Response model for checking clone existence.
/// </summary>
public class CloneExistsResponse
{
    /// <summary>
    /// Whether the clone exists.
    /// </summary>
    public bool Exists { get; set; }
}
