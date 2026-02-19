using Homespun.Features.GitHub;
using Homespun.Shared.Models.GitHub;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IPRStatusResolver for mock mode.
/// Does nothing since mock mode doesn't need to resolve PR statuses.
/// </summary>
public class MockPRStatusResolver : IPRStatusResolver
{
    /// <inheritdoc />
    public Task ResolveClosedPRStatusesAsync(string projectId, List<RemovedPrInfo> removedPrs)
    {
        // In mock mode, we don't need to resolve PR statuses
        // The mock services handle this differently
        return Task.CompletedTask;
    }
}
