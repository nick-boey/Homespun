using System.Collections.Frozen;
using System.Text;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Scans <c>.claude/skills/**/SKILL.md</c> under a clone and returns categorised
/// skill descriptors. OpenSpec skills are identified by a hard-coded name list;
/// Homespun skills are identified by <c>homespun: true</c> in frontmatter;
/// everything else is returned as a general skill.
/// </summary>
public class SkillDiscoveryService : ISkillDiscoveryService
{
    /// <summary>
    /// The fixed set of OpenSpec skills. The OpenSpec plugin never generates
    /// additional skills regardless of the active schema, so matching by name
    /// is sufficient and stable.
    /// </summary>
    internal static readonly FrozenSet<string> OpenSpecSkillNames = new[]
    {
        "openspec-explore",
        "openspec-new-change",
        "openspec-propose",
        "openspec-continue-change",
        "openspec-apply-change",
        "openspec-verify-change",
        "openspec-sync-specs",
        "openspec-archive-change",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<SkillDiscoveryService> _logger;

    public SkillDiscoveryService(ILogger<SkillDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<DiscoveredSkills> DiscoverSkillsAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var skillsRoot = Path.Combine(projectPath, ".claude", "skills");
        if (!Directory.Exists(skillsRoot))
        {
            _logger.LogDebug("No .claude/skills directory at {Path}", skillsRoot);
            return new DiscoveredSkills();
        }

        var openSpec = new List<SkillDescriptor>();
        var homespun = new List<SkillDescriptor>();
        var general = new List<SkillDescriptor>();

        foreach (var skillDir in Directory.EnumerateDirectories(skillsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var descriptor = await TryReadSkillAsync(skillDir, cancellationToken).ConfigureAwait(false);
            if (descriptor is null)
            {
                continue;
            }

            switch (descriptor.Category)
            {
                case SkillCategory.OpenSpec:
                    openSpec.Add(descriptor);
                    break;
                case SkillCategory.Homespun:
                    homespun.Add(descriptor);
                    break;
                default:
                    general.Add(descriptor);
                    break;
            }
        }

        openSpec.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        homespun.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        general.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return new DiscoveredSkills
        {
            OpenSpec = openSpec,
            Homespun = homespun,
            General = general,
        };
    }

    public async Task<SkillDescriptor?> GetSkillAsync(
        string projectPath,
        string skillName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return null;
        }

        var skillDir = Path.Combine(projectPath, ".claude", "skills", skillName);
        if (!Directory.Exists(skillDir))
        {
            return null;
        }

        return await TryReadSkillAsync(skillDir, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SkillDescriptor?> TryReadSkillAsync(
        string skillDir,
        CancellationToken cancellationToken)
    {
        var skillFile = Path.Combine(skillDir, "SKILL.md");
        if (!File.Exists(skillFile))
        {
            return null;
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(skillFile, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read skill file {Path}", skillFile);
            return null;
        }

        if (!TrySplitFrontmatter(content, out var frontmatter, out var body))
        {
            _logger.LogDebug("Skill {Path} has no frontmatter; skipping", skillFile);
            return null;
        }

        YamlMappingNode? mapping;
        try
        {
            mapping = ParseFrontmatter(frontmatter);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malformed frontmatter in {Path}; skipping", skillFile);
            return null;
        }

        if (mapping is null)
        {
            return null;
        }

        var name = GetString(mapping, "name") ?? Path.GetFileName(skillDir);
        var description = GetString(mapping, "description") ?? string.Empty;
        var isHomespun = GetBool(mapping, "homespun") ?? false;

        var descriptor = new SkillDescriptor
        {
            Name = name,
            Description = description,
            SkillBody = body,
        };

        if (OpenSpecSkillNames.Contains(name))
        {
            descriptor.Category = SkillCategory.OpenSpec;
        }
        else if (isHomespun)
        {
            descriptor.Category = SkillCategory.Homespun;
            descriptor.Mode = ParseMode(GetString(mapping, "homespun-mode"));
            descriptor.Args = ParseArgs(mapping);
        }
        else
        {
            descriptor.Category = SkillCategory.General;
        }

        return descriptor;
    }

    private static bool TrySplitFrontmatter(string content, out string frontmatter, out string body)
    {
        frontmatter = string.Empty;
        body = content;

        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        // Accept either "---\n" or "---\r\n" at the very start of the file.
        var stripped = content.TrimStart('\uFEFF');
        if (!stripped.StartsWith("---", StringComparison.Ordinal))
        {
            return false;
        }

        // Find the first newline after the opening delimiter, then find the
        // next "---" line.
        var afterOpen = stripped.IndexOf('\n');
        if (afterOpen < 0)
        {
            return false;
        }

        var searchStart = afterOpen + 1;
        // Look for a line that starts with "---".
        while (searchStart < stripped.Length)
        {
            var nextNewline = stripped.IndexOf('\n', searchStart);
            var lineEnd = nextNewline < 0 ? stripped.Length : nextNewline;
            var line = stripped[searchStart..lineEnd].TrimEnd('\r');

            if (line == "---" || line == "...")
            {
                frontmatter = stripped[(afterOpen + 1)..searchStart].TrimEnd('\r', '\n');
                body = nextNewline < 0 ? string.Empty : stripped[(nextNewline + 1)..];
                // Trim a single leading blank line from the body for neatness.
                body = body.TrimStart('\r', '\n');
                return true;
            }

            if (nextNewline < 0)
            {
                break;
            }
            searchStart = nextNewline + 1;
        }

        return false;
    }

    private static YamlMappingNode? ParseFrontmatter(string frontmatter)
    {
        using var reader = new StringReader(frontmatter);
        var yaml = new YamlStream();
        yaml.Load(reader);
        if (yaml.Documents.Count == 0)
        {
            return null;
        }
        return yaml.Documents[0].RootNode as YamlMappingNode;
    }

    private static string? GetString(YamlMappingNode mapping, string key)
    {
        if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node)
            && node is YamlScalarNode scalar)
        {
            return scalar.Value;
        }
        return null;
    }

    private static bool? GetBool(YamlMappingNode mapping, string key)
    {
        var raw = GetString(mapping, key);
        if (raw is null)
        {
            return null;
        }
        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static SessionMode? ParseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "plan" => SessionMode.Plan,
            "build" => SessionMode.Build,
            _ => null,
        };
    }

    private static IReadOnlyList<SkillArgDescriptor> ParseArgs(YamlMappingNode mapping)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode("homespun-args"), out var node)
            || node is not YamlSequenceNode sequence)
        {
            return Array.Empty<SkillArgDescriptor>();
        }

        var result = new List<SkillArgDescriptor>();
        foreach (var item in sequence.Children)
        {
            if (item is not YamlMappingNode argMap)
            {
                continue;
            }

            var name = GetString(argMap, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new SkillArgDescriptor
            {
                Name = name,
                Kind = ParseArgKind(GetString(argMap, "kind")),
                Label = GetString(argMap, "label"),
                Description = GetString(argMap, "description"),
            });
        }

        return result;
    }

    private static SkillArgKind ParseArgKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SkillArgKind.FreeText;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "issue" => SkillArgKind.Issue,
            "change" => SkillArgKind.Change,
            "phase-list" or "phaselist" => SkillArgKind.PhaseList,
            "free-text" or "freetext" or "text" => SkillArgKind.FreeText,
            _ => SkillArgKind.FreeText,
        };
    }
}
