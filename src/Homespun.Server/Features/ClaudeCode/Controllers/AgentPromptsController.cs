using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.ClaudeCode.Controllers;

[ApiController]
[Route("api/agent-prompts")]
[Produces("application/json")]
public class AgentPromptsController(IAgentPromptService agentPromptService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AgentPrompt>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AgentPrompt>> GetAll()
    {
        var prompts = agentPromptService.GetAllPrompts();
        return Ok(prompts);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<AgentPrompt>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AgentPrompt> GetById(string id)
    {
        var prompt = agentPromptService.GetPrompt(id);
        if (prompt == null)
        {
            return NotFound();
        }
        return Ok(prompt);
    }

    [HttpGet("project/{projectId}")]
    [ProducesResponseType<IReadOnlyList<AgentPrompt>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AgentPrompt>> GetByProject(string projectId)
    {
        var prompts = agentPromptService.GetPromptsForProject(projectId);
        return Ok(prompts);
    }

    [HttpGet("available-for-project/{projectId}")]
    [ProducesResponseType<IReadOnlyList<AgentPrompt>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AgentPrompt>> GetAvailableForProject(string projectId)
    {
        var prompts = agentPromptService.GetGlobalPromptsNotOverridden(projectId);
        return Ok(prompts);
    }

    [HttpPost]
    [ProducesResponseType<AgentPrompt>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AgentPrompt>> Create([FromBody] CreateAgentPromptRequest request)
    {
        var prompt = await agentPromptService.CreatePromptAsync(
            request.Name,
            request.InitialMessage,
            request.Mode,
            request.ProjectId);

        return CreatedAtAction(nameof(GetById), new { id = prompt.Id }, prompt);
    }

    [HttpPut("{id}")]
    [ProducesResponseType<AgentPrompt>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentPrompt>> Update(string id, [FromBody] UpdateAgentPromptRequest request)
    {
        var existing = agentPromptService.GetPrompt(id);
        if (existing == null)
        {
            return NotFound();
        }

        var prompt = await agentPromptService.UpdatePromptAsync(id, request.Name, request.InitialMessage, request.Mode);
        return Ok(prompt);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id)
    {
        var existing = agentPromptService.GetPrompt(id);
        if (existing == null)
        {
            return NotFound();
        }

        await agentPromptService.DeletePromptAsync(id);
        return NoContent();
    }

    [HttpPost("ensure-defaults")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EnsureDefaults()
    {
        await agentPromptService.EnsureDefaultPromptsAsync();
        return NoContent();
    }
}

public class CreateAgentPromptRequest
{
    public required string Name { get; set; }
    public string? InitialMessage { get; set; }
    public SessionMode Mode { get; set; }
    public string? ProjectId { get; set; }
}

public class UpdateAgentPromptRequest
{
    public required string Name { get; set; }
    public string? InitialMessage { get; set; }
    public SessionMode Mode { get; set; }
}
