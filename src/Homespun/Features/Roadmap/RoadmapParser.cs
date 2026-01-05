using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Parses and validates ROADMAP.json files.
/// Uses flat list structure with parent references (DAG).
/// </summary>
public static partial class RoadmapParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    // Pattern for valid shortTitle: lowercase alphanumeric and hyphens only
    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.Compiled)]
    private static partial Regex ShortTitlePattern();

    // Pattern for valid ID: group/type/shortTitle (allows slashes)
    [GeneratedRegex("^[a-z0-9-]+/[a-z]+/[a-z0-9-]+$", RegexOptions.Compiled)]
    private static partial Regex IdPattern();

    /// <summary>
    /// Parses a ROADMAP.json string into a Roadmap object.
    /// </summary>
    /// <param name="json">The JSON string to parse</param>
    /// <returns>The parsed Roadmap</returns>
    /// <exception cref="RoadmapValidationException">Thrown when validation fails</exception>
    public static Roadmap Parse(string json)
    {
        Roadmap? roadmap;

        try
        {
            roadmap = JsonSerializer.Deserialize<Roadmap>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new RoadmapValidationException($"Invalid JSON: {ex.Message}", ex);
        }

        if (roadmap == null)
        {
            throw new RoadmapValidationException("Failed to parse roadmap: null result");
        }

        Validate(roadmap);
        return roadmap;
    }

    /// <summary>
    /// Serializes a Roadmap object to JSON string.
    /// </summary>
    public static string Serialize(Roadmap roadmap)
    {
        return JsonSerializer.Serialize(roadmap, SerializerOptions);
    }

    /// <summary>
    /// Loads and parses a ROADMAP.json file.
    /// </summary>
    /// <param name="filePath">Path to the ROADMAP.json file</param>
    /// <returns>The parsed Roadmap</returns>
    public static async Task<Roadmap> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new RoadmapValidationException($"ROADMAP.json file not found: {filePath}");
        }

        var json = await File.ReadAllTextAsync(filePath);
        return Parse(json);
    }

    /// <summary>
    /// Saves a Roadmap to a file.
    /// </summary>
    public static async Task SaveAsync(Roadmap roadmap, string filePath)
    {
        roadmap.LastUpdated = DateTime.UtcNow;
        var json = Serialize(roadmap);
        await File.WriteAllTextAsync(filePath, json);
    }

    private static void Validate(Roadmap roadmap)
    {
        if (string.IsNullOrWhiteSpace(roadmap.Version))
        {
            throw new RoadmapValidationException("Missing required field: version");
        }

        ValidateChanges(roadmap.Changes);
    }

    /// <summary>
    /// Validates changes using flat list schema with parents.
    /// </summary>
    private static void ValidateChanges(List<FutureChange> changes)
    {
        // Collect all IDs first for parent validation
        var allIds = new HashSet<string>();
        foreach (var change in changes)
        {
            if (!string.IsNullOrWhiteSpace(change.Id))
            {
                allIds.Add(change.Id);
            }
        }

        var seenIds = new HashSet<string>();

        for (int i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            var changePath = $"changes[{i}]";

            // Validate required fields
            if (string.IsNullOrWhiteSpace(change.Id))
            {
                throw new RoadmapValidationException($"Missing required field: id at {changePath}");
            }

            // Validate shortTitle is present
            if (string.IsNullOrWhiteSpace(change.ShortTitle))
            {
                throw new RoadmapValidationException($"Missing required field: shortTitle at {changePath}");
            }

            // Validate shortTitle pattern (lowercase alphanumeric + hyphens, no slashes)
            if (!ShortTitlePattern().IsMatch(change.ShortTitle))
            {
                throw new RoadmapValidationException(
                    $"Invalid shortTitle pattern at {changePath}: '{change.ShortTitle}'. " +
                    "shortTitle must contain only lowercase letters, numbers, and hyphens.");
            }

            // Validate ID format: group/type/shortTitle
            if (!IdPattern().IsMatch(change.Id))
            {
                throw new RoadmapValidationException(
                    $"Invalid id format at {changePath}: '{change.Id}'. " +
                    "ID must follow the pattern: group/type/shortTitle (e.g., 'core/feature/add-auth').");
            }

            // Validate ID matches group/type/shortTitle
            var expectedId = $"{change.Group}/{change.Type.ToString().ToLowerInvariant()}/{change.ShortTitle}";
            if (change.Id != expectedId)
            {
                throw new RoadmapValidationException(
                    $"ID mismatch at {changePath}: id is '{change.Id}' but should be '{expectedId}' " +
                    $"based on group='{change.Group}', type='{change.Type}', shortTitle='{change.ShortTitle}'.");
            }

            // Check for duplicate IDs
            if (seenIds.Contains(change.Id))
            {
                throw new RoadmapValidationException($"Duplicate id at {changePath}: '{change.Id}'");
            }
            seenIds.Add(change.Id);

            // Validate group
            if (string.IsNullOrWhiteSpace(change.Group))
            {
                throw new RoadmapValidationException($"Missing required field: group at {changePath}");
            }

            // Validate title
            if (string.IsNullOrWhiteSpace(change.Title))
            {
                throw new RoadmapValidationException($"Missing required field: title at {changePath}");
            }

            // Validate type
            if (!Enum.IsDefined(typeof(ChangeType), change.Type))
            {
                throw new RoadmapValidationException($"Invalid type at {changePath}");
            }

            // Validate parent references exist
            foreach (var parentId in change.Parents)
            {
                if (!allIds.Contains(parentId))
                {
                    throw new RoadmapValidationException(
                        $"Invalid parent reference at {changePath}: '{parentId}' does not exist in the roadmap.");
                }
            }
        }
    }
}
