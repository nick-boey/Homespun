using System.Net.Http.Json;
using Homespun.Shared;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

public class HttpProjectApiService(HttpClient http)
{
    public async Task<List<Project>> GetAllProjectsAsync()
    {
        return await http.GetFromJsonAsync<List<Project>>(ApiRoutes.Projects) ?? [];
    }

    public async Task<Project?> GetProjectAsync(string id)
    {
        var response = await http.GetAsync($"{ApiRoutes.Projects}/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Project>();
    }

    public async Task<Project?> CreateProjectAsync(CreateProjectRequest request)
    {
        var response = await http.PostAsJsonAsync(ApiRoutes.Projects, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Project>();
    }

    public async Task<Project?> UpdateProjectAsync(string id, UpdateProjectRequest request)
    {
        var response = await http.PutAsJsonAsync($"{ApiRoutes.Projects}/{id}", request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Project>();
    }

    public async Task DeleteProjectAsync(string id)
    {
        var response = await http.DeleteAsync($"{ApiRoutes.Projects}/{id}");
        response.EnsureSuccessStatusCode();
    }
}
