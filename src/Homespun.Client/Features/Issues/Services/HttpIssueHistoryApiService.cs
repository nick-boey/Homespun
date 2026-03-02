using System.Net.Http.Json;
using Homespun.Shared;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Client.Services;

/// <summary>
/// Client service for interacting with the issue history API.
/// Provides undo/redo functionality for issue changes.
/// </summary>
public class HttpIssueHistoryApiService(HttpClient http)
{
    /// <summary>
    /// Gets the current history state for a project.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>The current history state including undo/redo availability</returns>
    public async Task<IssueHistoryState> GetStateAsync(string projectId)
    {
        return await http.GetFromJsonAsync<IssueHistoryState>(
            $"{ApiRoutes.Projects}/{projectId}/issues/history/state") ?? new IssueHistoryState();
    }

    /// <summary>
    /// Undoes the last change to issues.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>The operation response including updated state</returns>
    public async Task<IssueHistoryOperationResponse> UndoAsync(string projectId)
    {
        var response = await http.PostAsync(
            $"{ApiRoutes.Projects}/{projectId}/issues/history/undo",
            null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssueHistoryOperationResponse>()
            ?? new IssueHistoryOperationResponse { Success = false, ErrorMessage = "Invalid response" };
    }

    /// <summary>
    /// Redoes a previously undone change.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>The operation response including updated state</returns>
    public async Task<IssueHistoryOperationResponse> RedoAsync(string projectId)
    {
        var response = await http.PostAsync(
            $"{ApiRoutes.Projects}/{projectId}/issues/history/redo",
            null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssueHistoryOperationResponse>()
            ?? new IssueHistoryOperationResponse { Success = false, ErrorMessage = "Invalid response" };
    }
}
