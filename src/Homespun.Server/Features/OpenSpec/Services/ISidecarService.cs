using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Service for reading and writing <c>.homespun.yaml</c> sidecar files that link
/// OpenSpec change directories to Fleece issues.
/// </summary>
public interface ISidecarService
{
    /// <summary>
    /// The sidecar filename convention: <c>.homespun.yaml</c>.
    /// </summary>
    public const string SidecarFileName = ".homespun.yaml";

    /// <summary>
    /// Reads a sidecar from the given change directory.
    /// </summary>
    /// <param name="changeDirectory">The absolute path to an OpenSpec change directory (e.g. <c>openspec/changes/my-change</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed sidecar, or <c>null</c> if the file is missing or malformed.</returns>
    Task<ChangeSidecar?> ReadSidecarAsync(string changeDirectory, CancellationToken ct = default);

    /// <summary>
    /// Writes a sidecar to the given change directory, overwriting any existing file.
    /// </summary>
    /// <param name="changeDirectory">The absolute path to an OpenSpec change directory.</param>
    /// <param name="sidecar">The sidecar content to write.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteSidecarAsync(string changeDirectory, ChangeSidecar sidecar, CancellationToken ct = default);
}
