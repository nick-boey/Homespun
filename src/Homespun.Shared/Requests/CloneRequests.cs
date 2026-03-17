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

/// <summary>
/// Request model for creating a branch session.
/// </summary>
public class CreateBranchSessionRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The branch name for the session.
    /// </summary>
    public required string BranchName { get; set; }

    /// <summary>
    /// Base branch for the new branch. Defaults to project's default branch.
    /// </summary>
    public string? BaseBranch { get; set; }
}

/// <summary>
/// Response model for creating a branch session.
/// </summary>
public class CreateBranchSessionResponse
{
    /// <summary>
    /// The created session ID.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public required string BranchName { get; set; }

    /// <summary>
    /// The path to the clone.
    /// </summary>
    public required string ClonePath { get; set; }
}
