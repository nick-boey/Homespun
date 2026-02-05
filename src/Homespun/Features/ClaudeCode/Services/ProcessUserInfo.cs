using System.Runtime.InteropServices;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Utility class to get the current process's UID/GID on Linux.
/// Used to pass the --user flag to Docker containers so they run with the same
/// permissions as the main container, avoiding file permission mismatches.
/// </summary>
public static class ProcessUserInfo
{
    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUidNative();

    [DllImport("libc", EntryPoint = "getgid")]
    private static extern uint GetGidNative();

    /// <summary>
    /// Gets the effective user ID of the calling process.
    /// </summary>
    /// <returns>The UID of the current process.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown on non-Linux platforms.</exception>
    public static uint GetUid()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("GetUid is only supported on Linux");
        return GetUidNative();
    }

    /// <summary>
    /// Gets the effective group ID of the calling process.
    /// </summary>
    /// <returns>The GID of the current process.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown on non-Linux platforms.</exception>
    public static uint GetGid()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("GetGid is only supported on Linux");
        return GetGidNative();
    }

    /// <summary>
    /// Gets the Docker --user flag value (uid:gid) for the current process.
    /// This is used to run Docker containers with the same user permissions as the current process.
    /// </summary>
    /// <returns>
    /// A string in the format "uid:gid" on Linux, or null on other platforms.
    /// Returns null on non-Linux platforms because Docker Desktop (macOS/Windows)
    /// handles user permissions differently and doesn't require the --user flag.
    /// </returns>
    public static string? GetDockerUserFlag()
    {
        if (!OperatingSystem.IsLinux())
            return null;
        return $"{GetUidNative()}:{GetGidNative()}";
    }
}
