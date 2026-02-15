using System.Net.Http.Json;
using Homespun.Shared;
using Homespun.Shared.Models.Containers;

namespace Homespun.Client.Services;

/// <summary>
/// Client-side HTTP service for interacting with the containers API.
/// </summary>
public class HttpContainerApiService(HttpClient http)
{
    /// <summary>
    /// Gets all worker containers from the server.
    /// </summary>
    public async Task<List<WorkerContainerDto>> GetAllContainersAsync()
    {
        var result = await http.GetFromJsonAsync<List<WorkerContainerDto>>(ApiRoutes.Containers);
        return result ?? [];
    }

    /// <summary>
    /// Stops a container by ID.
    /// </summary>
    /// <param name="containerId">The container ID to stop.</param>
    /// <returns>True if successful.</returns>
    public async Task<bool> StopContainerAsync(string containerId)
    {
        var response = await http.DeleteAsync($"{ApiRoutes.Containers}/{Uri.EscapeDataString(containerId)}");
        return response.IsSuccessStatusCode;
    }
}
