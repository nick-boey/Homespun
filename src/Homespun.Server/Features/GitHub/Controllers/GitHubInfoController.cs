using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.GitHub.Controllers;

[ApiController]
[Route("api/github")]
[Produces("application/json")]
public class GitHubInfoController(IGitHubEnvironmentService gitHubEnvironmentService) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<GitHubStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<GitHubStatusResponse> GetStatus()
    {
        return Ok(new GitHubStatusResponse
        {
            IsConfigured = gitHubEnvironmentService.IsConfigured,
            MaskedToken = gitHubEnvironmentService.GetMaskedToken()
        });
    }

    [HttpGet("auth-status")]
    [ProducesResponseType<GitHubAuthStatus>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitHubAuthStatus>> GetAuthStatus(CancellationToken ct)
    {
        var status = await gitHubEnvironmentService.CheckGhAuthStatusAsync(ct);
        return Ok(status);
    }
}

public class GitHubStatusResponse
{
    public bool IsConfigured { get; set; }
    public string? MaskedToken { get; set; }
}
