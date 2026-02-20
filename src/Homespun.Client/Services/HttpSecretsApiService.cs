using System.Net.Http.Json;
using Homespun.Shared.Models.Secrets;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

/// <summary>
/// Client service for managing project secrets via the HTTP API.
/// </summary>
public class HttpSecretsApiService(HttpClient http)
{
    /// <summary>
    /// Gets the list of secret names for a project (values are never returned for security).
    /// </summary>
    public async Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string projectId)
    {
        var response = await http.GetFromJsonAsync<SecretsListResponse>($"api/projects/{projectId}/secrets");
        return response?.Secrets ?? [];
    }

    /// <summary>
    /// Creates a new secret for a project.
    /// </summary>
    public async Task CreateSecretAsync(string projectId, string name, string value)
    {
        var request = new CreateSecretRequest { Name = name, Value = value };
        var response = await http.PostAsJsonAsync($"api/projects/{projectId}/secrets", request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Updates an existing secret's value.
    /// </summary>
    public async Task UpdateSecretAsync(string projectId, string name, string value)
    {
        var request = new UpdateSecretRequest { Value = value };
        var response = await http.PutAsJsonAsync($"api/projects/{projectId}/secrets/{name}", request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Deletes a secret from a project.
    /// </summary>
    public async Task DeleteSecretAsync(string projectId, string name)
    {
        var response = await http.DeleteAsync($"api/projects/{projectId}/secrets/{name}");
        response.EnsureSuccessStatusCode();
    }
}
