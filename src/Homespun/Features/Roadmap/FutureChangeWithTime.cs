namespace Homespun.Features.Roadmap;

/// <summary>
/// Result of a future change with its calculated time and depth.
/// </summary>
public record FutureChangeWithTime(FutureChange Change, int Time, int Depth);