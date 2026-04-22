using System.Reflection;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Tests.Features.Fleece;

/// <summary>
/// Guards the patch-vs-topology whitelist against drift. Every non-immutable
/// property on <see cref="IssueResponse"/> must be classified as exactly one of
/// <see cref="PatchableFieldAttribute"/> or <see cref="TopologyFieldAttribute"/>.
/// Adding a property without classifying it fails this test with a message
/// naming the property — preventing a silent bug where a new field lands on the
/// patch path (desyncing derived data) or the topology path (needless rebuilds).
/// </summary>
[TestFixture]
public class IssueResponseFieldClassificationTests
{
    private static readonly HashSet<string> ImmutableProperties = new(StringComparer.Ordinal)
    {
        nameof(IssueResponse.Id),
        nameof(IssueResponse.CreatedAt),
    };

    [Test]
    public void EveryMutableProperty_IsClassified_AsExactlyOneOfPatchableOrTopology()
    {
        var unclassified = new List<string>();
        var doubleClassified = new List<string>();

        foreach (var prop in typeof(IssueResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ImmutableProperties.Contains(prop.Name))
            {
                continue;
            }

            var patchable = prop.GetCustomAttribute<PatchableFieldAttribute>() is not null;
            var topology = prop.GetCustomAttribute<TopologyFieldAttribute>() is not null;

            if (!patchable && !topology)
            {
                unclassified.Add(prop.Name);
            }
            else if (patchable && topology)
            {
                doubleClassified.Add(prop.Name);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(unclassified, Is.Empty,
                $"IssueResponse properties without [PatchableField] or [TopologyField]: " +
                $"{string.Join(", ", unclassified)}. " +
                $"Classify them in Homespun.Shared/Models/Fleece/IssueDto.cs — patchable if the field " +
                $"is structure-preserving, topology otherwise.");
            Assert.That(doubleClassified, Is.Empty,
                $"IssueResponse properties with BOTH attributes: {string.Join(", ", doubleClassified)}.");
        });
    }
}
