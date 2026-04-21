using Anthropic;
using Anthropic.Models.Models;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Live adapter that calls the Anthropic SDK's <c>models.list</c> endpoint
/// and projects the result into <see cref="RawModelInfo"/>.
/// </summary>
internal sealed class AnthropicModelSource : IAnthropicModelSource
{
    private readonly IAnthropicClient _client;

    public AnthropicModelSource(IAnthropicClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<RawModelInfo>> ListAllAsync(CancellationToken ct)
    {
        var page = await _client.Models.List(new ModelListParams(), ct);
        return page.Items
            .Select(m => new RawModelInfo(m.ID, m.DisplayName, m.CreatedAt))
            .ToList();
    }
}
