using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Search.Controllers;

/// <summary>
/// API endpoints for project file and PR search with caching support.
/// Used for @ and # mention autocomplete in the UI.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/search")]
[Produces("application/json")]
public class ProjectSearchController(
    IProjectFileService fileService,
    ISearchablePrService prService) : ControllerBase
{
    /// <summary>
    /// Get all tracked files in the project's repository.
    /// Returns 304 Not Modified if the client's cached hash matches.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="hash">Optional client-side cached hash for conditional request</param>
    [HttpGet("files")]
    [ProducesResponseType<FileListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFiles(string projectId, [FromQuery] string? hash)
    {
        try
        {
            var result = await fileService.GetFilesAsync(projectId);

            // Return 304 if hash matches
            if (!string.IsNullOrEmpty(hash) && hash == result.Hash)
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            return Ok(new FileListResponse(result.Files, result.Hash));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Get all open and recently merged PRs for the project.
    /// Returns 304 Not Modified if the client's cached hash matches.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="hash">Optional client-side cached hash for conditional request</param>
    [HttpGet("prs")]
    [ProducesResponseType<PrListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrs(string projectId, [FromQuery] string? hash)
    {
        try
        {
            var result = await prService.GetPrsAsync(projectId);

            // Return 304 if hash matches
            if (!string.IsNullOrEmpty(hash) && hash == result.Hash)
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            var prs = result.Prs.Select(p => new SearchablePrResponse(p.Number, p.Title, p.BranchName)).ToList();
            return Ok(new PrListResponse(prs, result.Hash));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

/// <summary>
/// Response DTO for file list endpoint.
/// </summary>
public record FileListResponse(IReadOnlyList<string> Files, string Hash);

/// <summary>
/// Response DTO for PR list endpoint.
/// </summary>
public record PrListResponse(IReadOnlyList<SearchablePrResponse> Prs, string Hash);

/// <summary>
/// DTO for a searchable PR.
/// </summary>
public record SearchablePrResponse(int Number, string Title, string? BranchName);
