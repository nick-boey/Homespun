using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Client.Services;

public record CreateAgentPromptRequest(string Name, string? InitialMessage, SessionMode Mode, string? ProjectId = null);
public record UpdateAgentPromptRequest(string Name, string? InitialMessage, SessionMode Mode);

public partial class HttpAgentPromptApiService(HttpClient http)
{
    private const string BaseUrl = "api/agent-prompts";

    public async Task<List<AgentPrompt>> GetAllPromptsAsync()
    {
        return await http.GetFromJsonAsync<List<AgentPrompt>>(BaseUrl) ?? [];
    }

    public async Task<AgentPrompt?> GetPromptAsync(string id)
    {
        var response = await http.GetAsync($"{BaseUrl}/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentPrompt>();
    }

    public async Task<List<AgentPrompt>> GetProjectPromptsAsync(string projectId)
    {
        return await http.GetFromJsonAsync<List<AgentPrompt>>($"{BaseUrl}/project/{projectId}") ?? [];
    }

    public async Task<AgentPrompt?> CreatePromptAsync(CreateAgentPromptRequest request)
    {
        var response = await http.PostAsJsonAsync(BaseUrl, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentPrompt>();
    }

    public async Task<AgentPrompt?> CreatePromptAsync(string name, string? initialMessage, SessionMode mode, string? projectId = null)
    {
        return await CreatePromptAsync(new CreateAgentPromptRequest(name, initialMessage, mode, projectId));
    }

    public async Task<AgentPrompt?> UpdatePromptAsync(string id, UpdateAgentPromptRequest request)
    {
        var response = await http.PutAsJsonAsync($"{BaseUrl}/{id}", request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentPrompt>();
    }

    public async Task<AgentPrompt?> UpdatePromptAsync(string id, string name, string? initialMessage, SessionMode mode)
    {
        return await UpdatePromptAsync(id, new UpdateAgentPromptRequest(name, initialMessage, mode));
    }

    public async Task DeletePromptAsync(string id)
    {
        var response = await http.DeleteAsync($"{BaseUrl}/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task EnsureDefaultPromptsAsync()
    {
        var response = await http.PostAsync($"{BaseUrl}/ensure-defaults", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Client-side template rendering for prompt placeholders.
    /// </summary>
    public static string? RenderTemplate(string? template, PromptContext context)
    {
        if (template == null)
            return null;

        return PlaceholderRegex().Replace(template, match =>
        {
            var placeholder = match.Groups[1].Value.ToLowerInvariant();
            return placeholder switch
            {
                "title" => context.Title,
                "id" => context.Id,
                "description" => context.Description ?? string.Empty,
                "branch" => context.Branch,
                "type" => context.Type,
                _ => match.Value
            };
        });
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();
}
