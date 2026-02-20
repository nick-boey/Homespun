using System.Net.Http.Json;
using Homespun.Shared;
using Homespun.Shared.Models.Plans;

namespace Homespun.Client.Services;

/// <summary>
/// HTTP client service for accessing plan files API.
/// </summary>
public class HttpPlansApiService(HttpClient http)
{
    /// <summary>
    /// Gets all plan files for a working directory.
    /// </summary>
    /// <param name="workingDirectory">The working directory containing the .claude/plans folder.</param>
    /// <returns>A list of plan file information.</returns>
    public async Task<List<PlanFileInfo>> GetPlanFilesAsync(string workingDirectory)
    {
        return await http.GetFromJsonAsync<List<PlanFileInfo>>(
            $"{ApiRoutes.Plans}?workingDirectory={Uri.EscapeDataString(workingDirectory)}") ?? [];
    }

    /// <summary>
    /// Gets the content of a specific plan file.
    /// </summary>
    /// <param name="workingDirectory">The working directory containing the .claude/plans folder.</param>
    /// <param name="fileName">The name of the plan file.</param>
    /// <returns>The content of the plan file, or null if not found.</returns>
    public async Task<string?> GetPlanContentAsync(string workingDirectory, string fileName)
    {
        var response = await http.GetAsync(
            $"{ApiRoutes.Plans}/content?workingDirectory={Uri.EscapeDataString(workingDirectory)}&fileName={Uri.EscapeDataString(fileName)}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync();
    }
}
