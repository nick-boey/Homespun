using System.Net.Http.Json;
using Homespun.Shared.Models.Notifications;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

public class HttpNotificationApiService(HttpClient http)
{
    public async Task<List<NotificationDto>> GetNotificationsAsync(string? projectId = null)
    {
        var url = "api/notifications";
        if (!string.IsNullOrEmpty(projectId))
            url += $"?projectId={projectId}";
        return await http.GetFromJsonAsync<List<NotificationDto>>(url) ?? [];
    }

    public async Task<NotificationDto?> CreateNotificationAsync(CreateNotificationRequest request)
    {
        var response = await http.PostAsJsonAsync("api/notifications", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotificationDto>();
    }

    public async Task DismissNotificationAsync(string id)
    {
        var response = await http.DeleteAsync($"api/notifications/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task DismissNotificationByKeyAsync(string key)
    {
        var response = await http.DeleteAsync($"api/notifications/by-key/{key}");
        response.EnsureSuccessStatusCode();
    }
}
