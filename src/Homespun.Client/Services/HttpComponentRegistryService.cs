using System.Net.Http.Json;
using Homespun.Shared.Models.Design;

namespace Homespun.Client.Services;

public interface IComponentRegistryService
{
    IReadOnlyList<ComponentMetadata> GetAllComponents();
    ComponentMetadata? GetComponent(string id);
    IReadOnlyList<ComponentMetadata> GetComponentsByCategory(string category);
    IReadOnlyList<string> GetCategories();
}

public class HttpComponentRegistryService(HttpClient http) : IComponentRegistryService
{
    private List<ComponentMetadata>? _cachedComponents;
    private List<string>? _cachedCategories;

    public IReadOnlyList<ComponentMetadata> GetAllComponents()
    {
        if (_cachedComponents != null)
            return _cachedComponents.AsReadOnly();

        // Load synchronously from cache or return empty - use InitializeAsync for first load
        return Array.Empty<ComponentMetadata>();
    }

    public ComponentMetadata? GetComponent(string id)
    {
        return _cachedComponents?.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ComponentMetadata> GetComponentsByCategory(string category)
    {
        return _cachedComponents?
            .Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly() ?? (IReadOnlyList<ComponentMetadata>)Array.Empty<ComponentMetadata>();
    }

    public IReadOnlyList<string> GetCategories()
    {
        return _cachedCategories?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public async Task InitializeAsync()
    {
        _cachedComponents = await http.GetFromJsonAsync<List<ComponentMetadata>>("api/design/components") ?? [];
        _cachedCategories = await http.GetFromJsonAsync<List<string>>("api/design/categories") ?? [];
    }
}
