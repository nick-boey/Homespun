using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Mock-mode implementation of <see cref="IModelCatalogService"/>. Returns
/// <see cref="ClaudeModelInfo.FallbackModels"/> with the same default-selection
/// rule applied and never constructs or invokes <c>IAnthropicClient</c>.
/// </summary>
public sealed class MockModelCatalogService : IModelCatalogService
{
    private readonly IReadOnlyList<ClaudeModelInfo> _catalog;

    public MockModelCatalogService()
    {
        _catalog = ModelCatalogDefaults.ApplyDefaultMarker(
            ClaudeModelInfo.FallbackModels
                .Select(m => new ClaudeModelInfo
                {
                    Id = m.Id,
                    DisplayName = m.DisplayName,
                    CreatedAt = m.CreatedAt,
                })
                .ToList());
    }

    public Task<IReadOnlyList<ClaudeModelInfo>> ListAsync(CancellationToken ct) => Task.FromResult(_catalog);

    public Task<string> ResolveModelIdAsync(string? requested, CancellationToken ct)
        => Task.FromResult(ModelCatalogDefaults.Resolve(requested, _catalog));
}
