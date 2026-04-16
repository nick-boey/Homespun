using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Reads and writes <c>.homespun.yaml</c> sidecars that link OpenSpec changes to Fleece issues.
/// </summary>
public class SidecarService(ILogger<SidecarService> logger) : ISidecarService
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <inheritdoc />
    public async Task<ChangeSidecar?> ReadSidecarAsync(string changeDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(changeDirectory, ISidecarService.SidecarFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        string yaml;
        try
        {
            yaml = await File.ReadAllTextAsync(path, ct);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read sidecar {Path}", path);
            return null;
        }

        try
        {
            var sidecar = Deserializer.Deserialize<SidecarYaml?>(yaml);
            if (sidecar is null || string.IsNullOrWhiteSpace(sidecar.FleeceId) || string.IsNullOrWhiteSpace(sidecar.CreatedBy))
            {
                logger.LogWarning("Sidecar at {Path} is missing required fields (fleeceId, createdBy)", path);
                return null;
            }

            return new ChangeSidecar
            {
                FleeceId = sidecar.FleeceId,
                CreatedBy = sidecar.CreatedBy
            };
        }
        catch (YamlException ex)
        {
            logger.LogWarning(ex, "Sidecar at {Path} is malformed YAML", path);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteSidecarAsync(string changeDirectory, ChangeSidecar sidecar, CancellationToken ct = default)
    {
        if (!Directory.Exists(changeDirectory))
        {
            throw new DirectoryNotFoundException($"Change directory does not exist: {changeDirectory}");
        }

        var path = Path.Combine(changeDirectory, ISidecarService.SidecarFileName);
        var yaml = Serializer.Serialize(new SidecarYaml
        {
            FleeceId = sidecar.FleeceId,
            CreatedBy = sidecar.CreatedBy
        });

        await File.WriteAllTextAsync(path, yaml, ct);
    }

    /// <summary>
    /// YAML-facing DTO. Mutable fields allow YamlDotNet to populate via property setters.
    /// </summary>
    private sealed class SidecarYaml
    {
        public string? FleeceId { get; set; }
        public string? CreatedBy { get; set; }
    }
}
