using Microsoft.AspNetCore.Mvc;

namespace Homespun.AgentWorker.Controllers;

/// <summary>
/// API controller for reading files from the agent worker container's filesystem.
/// This enables the parent application to retrieve files that exist only inside
/// the agent container (e.g., plan files written to ~/.claude/plans/).
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly ILogger<FilesController> _logger;

    /// <summary>
    /// Allowed directory prefixes for file access.
    /// Restricts reads to safe locations to prevent unauthorized filesystem access.
    /// </summary>
    private static readonly string[] AllowedPrefixes =
    [
        "/home/homespun/.claude/plans/",
        "/home/homespun/.claude/plan.md",
        "/home/homespun/.claude/PLAN.md"
    ];

    public FilesController(ILogger<FilesController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reads the content of a file from the agent container's filesystem.
    /// Only allows reading from approved directories (e.g., ~/.claude/plans/).
    /// </summary>
    /// <param name="request">The file read request containing the file path.</param>
    /// <returns>The file content as a string, or 404 if not found.</returns>
    [HttpPost("read")]
    public async Task<IActionResult> ReadFile([FromBody] FileReadRequest request)
    {
        if (string.IsNullOrEmpty(request.FilePath))
        {
            return BadRequest(new { message = "FilePath is required" });
        }

        // Resolve the path to prevent directory traversal attacks
        var resolvedPath = Path.GetFullPath(request.FilePath);

        // Check if the path is within allowed directories
        if (!IsAllowedPath(resolvedPath))
        {
            _logger.LogWarning("File read denied for path outside allowed directories: {Path}", resolvedPath);
            return Forbid();
        }

        if (!System.IO.File.Exists(resolvedPath))
        {
            _logger.LogDebug("File not found: {Path}", resolvedPath);
            return NotFound(new { message = $"File not found: {resolvedPath}" });
        }

        try
        {
            var content = await System.IO.File.ReadAllTextAsync(resolvedPath);
            _logger.LogInformation("Successfully read file {Path} ({Length} chars)", resolvedPath, content.Length);
            return Ok(new FileReadResponse { FilePath = resolvedPath, Content = content });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file {Path}", resolvedPath);
            return StatusCode(500, new { message = $"Error reading file: {ex.Message}" });
        }
    }

    /// <summary>
    /// Lists plan files available in the agent container's ~/.claude/plans/ directory.
    /// </summary>
    [HttpGet("plans")]
    public IActionResult ListPlanFiles()
    {
        var plansDir = "/home/homespun/.claude/plans";

        if (!Directory.Exists(plansDir))
        {
            return Ok(new { files = Array.Empty<string>() });
        }

        try
        {
            var files = Directory.GetFiles(plansDir, "*.md")
                .Select(f => new { path = f, name = Path.GetFileName(f) })
                .ToArray();

            return Ok(new { files });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing plan files");
            return StatusCode(500, new { message = $"Error listing plans: {ex.Message}" });
        }
    }

    private static bool IsAllowedPath(string resolvedPath)
    {
        return AllowedPrefixes.Any(prefix =>
            resolvedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Request to read a file from the agent container's filesystem.
/// </summary>
public class FileReadRequest
{
    /// <summary>
    /// Absolute path to the file to read.
    /// </summary>
    public required string FilePath { get; set; }
}

/// <summary>
/// Response containing the file content.
/// </summary>
public class FileReadResponse
{
    /// <summary>
    /// The resolved file path.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// The file content as a string.
    /// </summary>
    public required string Content { get; set; }
}
