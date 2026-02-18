using Homespun.Shared.Models.Secrets;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Secrets.Controllers;

/// <summary>
/// API endpoints for managing project secrets (environment variables for worker containers).
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/secrets")]
[Produces("application/json")]
public class SecretsController(ISecretsService secretsService, ILogger<SecretsController> logger) : ControllerBase
{
    /// <summary>
    /// Get all secrets for a project (names only, never values for security).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SecretsListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SecretsListResponse>> GetSecrets(string projectId)
    {
        var secrets = await secretsService.GetSecretsAsync(projectId);
        return Ok(new SecretsListResponse { Secrets = secrets });
    }

    /// <summary>
    /// Create a new secret for a project.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSecret(string projectId, [FromBody] CreateSecretRequest request)
    {
        try
        {
            var success = await secretsService.SetSecretAsync(projectId, request.Name, request.Value);
            if (!success)
            {
                return NotFound($"Project '{projectId}' not found.");
            }

            logger.LogInformation("Created secret {SecretName} for project {ProjectId}", request.Name, projectId);
            return CreatedAtAction(nameof(GetSecrets), new { projectId }, null);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid secret name: {SecretName}", request.Name);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Update an existing secret's value.
    /// </summary>
    [HttpPut("{name}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSecret(string projectId, string name, [FromBody] UpdateSecretRequest request)
    {
        try
        {
            var success = await secretsService.SetSecretAsync(projectId, name, request.Value);
            if (!success)
            {
                return NotFound($"Project '{projectId}' not found.");
            }

            logger.LogInformation("Updated secret {SecretName} for project {ProjectId}", name, projectId);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid secret name: {SecretName}", name);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Delete a secret from a project.
    /// </summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSecret(string projectId, string name)
    {
        var deleted = await secretsService.DeleteSecretAsync(projectId, name);
        if (!deleted)
        {
            return NotFound($"Secret '{name}' not found in project '{projectId}'.");
        }

        logger.LogInformation("Deleted secret {SecretName} from project {ProjectId}", name, projectId);
        return NoContent();
    }
}
