using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.GitHub.Controllers;

[ApiController]
[Route("api/issue-pr-status")]
[Produces("application/json")]
public class IssuePrStatusController(IIssuePrStatusService issuePrStatusService) : ControllerBase
{
    [HttpGet("{projectId}/{issueId}")]
    [ProducesResponseType<IssuePullRequestStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssuePullRequestStatus>> GetStatus(string projectId, string issueId)
    {
        var status = await issuePrStatusService.GetPullRequestStatusForIssueAsync(projectId, issueId);
        if (status == null)
        {
            return NotFound();
        }
        return Ok(status);
    }
}
