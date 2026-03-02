using Homespun.Shared.Models.Design;

namespace Homespun.Client.Features.Design.Services;

public interface IComponentRegistryService
{
    IReadOnlyList<ComponentMetadata> GetAllComponents();
    ComponentMetadata? GetComponent(string id);
    IReadOnlyList<ComponentMetadata> GetComponentsByCategory(string category);
    IReadOnlyList<string> GetCategories();
}
