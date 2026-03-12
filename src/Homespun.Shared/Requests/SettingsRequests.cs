namespace Homespun.Shared.Requests;

/// <summary>
/// Response model for user settings.
/// </summary>
public class UserSettingsResponse
{
    /// <summary>
    /// The user email used for issue assignment.
    /// </summary>
    public string? UserEmail { get; set; }
}

/// <summary>
/// Request model for updating the user email.
/// </summary>
public class UpdateUserEmailRequest
{
    /// <summary>
    /// The email to set for issue assignment.
    /// </summary>
    public required string Email { get; set; }
}
