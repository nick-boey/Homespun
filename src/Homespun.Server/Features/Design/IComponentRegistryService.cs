namespace Homespun.Features.Design;

/// <summary>
/// Service for accessing component metadata for the design system.
/// </summary>
public interface IComponentRegistryService
{
    /// <summary>
    /// Gets all registered components.
    /// </summary>
    IReadOnlyList<ComponentMetadata> GetAllComponents();

    /// <summary>
    /// Gets a component by its ID/slug.
    /// </summary>
    ComponentMetadata? GetComponent(string id);

    /// <summary>
    /// Gets components filtered by category.
    /// </summary>
    IReadOnlyList<ComponentMetadata> GetComponentsByCategory(string category);

    /// <summary>
    /// Gets all unique categories.
    /// </summary>
    IReadOnlyList<string> GetCategories();
}
