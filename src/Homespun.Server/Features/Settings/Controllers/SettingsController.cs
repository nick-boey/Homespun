using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Settings.Controllers;

/// <summary>
/// API endpoints for managing user settings.
/// </summary>
[ApiController]
[Route("api/settings")]
[Produces("application/json")]
public class SettingsController(IDataStore dataStore) : ControllerBase
{
    /// <summary>
    /// Get user settings.
    /// </summary>
    [HttpGet("user")]
    [ProducesResponseType<UserSettingsResponse>(StatusCodes.Status200OK)]
    public ActionResult<UserSettingsResponse> GetUserSettings()
    {
        return Ok(new UserSettingsResponse
        {
            UserEmail = dataStore.UserEmail
        });
    }

    /// <summary>
    /// Update user email.
    /// </summary>
    [HttpPut("user/email")]
    [ProducesResponseType<UserSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserSettingsResponse>> UpdateUserEmail([FromBody] UpdateUserEmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Email is required");
        }

        // Basic email format validation
        if (!request.Email.Contains('@') || !request.Email.Contains('.'))
        {
            return BadRequest("Invalid email format");
        }

        await dataStore.SetUserEmailAsync(request.Email.Trim());

        return Ok(new UserSettingsResponse
        {
            UserEmail = dataStore.UserEmail
        });
    }
}
