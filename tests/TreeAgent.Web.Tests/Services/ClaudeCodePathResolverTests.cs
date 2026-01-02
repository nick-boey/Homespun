using TreeAgent.Web.Services;

namespace TreeAgent.Web.Tests.Services;

public class ClaudeCodePathResolverTests
{
    [Fact]
    public void Resolve_WithEnvironmentVariable_ReturnsEnvPath()
    {
        // Arrange
        var envPath = @"C:\custom\claude.exe";
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: envPath,
            fileExistsCheck: _ => false);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal(envPath, result);
    }

    [Fact]
    public void Resolve_WithoutEnvVar_ChecksDefaultLocations()
    {
        // Arrange
        var existingPath = @"C:\Users\test\AppData\Local\Programs\claude-code\claude.exe";
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == existingPath,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal(existingPath, result);
    }

    [Fact]
    public void Resolve_Windows_ChecksNativeInstallerFirst()
    {
        // Arrange
        var nativePath = @"C:\Users\test\AppData\Local\Programs\claude-code\claude.exe";
        var npmPath = @"C:\Users\test\AppData\Roaming\npm\claude.cmd";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == nativePath || path == npmPath,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal(nativePath, result);
    }

    [Fact]
    public void Resolve_Windows_FallsBackToNpmPath()
    {
        // Arrange
        var npmPath = @"C:\Users\test\AppData\Roaming\npm\claude.cmd";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == npmPath,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal(npmPath, result);
    }

    [Fact]
    public void Resolve_Linux_ChecksNativeInstallerFirst()
    {
        // Arrange
        var nativePath = "/usr/local/bin/claude";
        var npmPath = "/home/test/.npm-global/bin/claude";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == nativePath || path == npmPath,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => null,
            getHomeDirectory: () => "/home/test",
            isWindows: () => false);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal(nativePath, result);
    }

    [Fact]
    public void Resolve_Linux_FallsBackToNpmGlobalPath()
    {
        // Arrange
        var npmPath = "/home/test/.npm-global/bin/claude";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == npmPath,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => null,
            getHomeDirectory: () => "/home/test",
            isWindows: () => false);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal(npmPath, result);
    }

    [Fact]
    public void Resolve_Linux_FallsBackToLocalBinPath()
    {
        // Arrange
        var localBinPath = "/home/test/.local/bin/claude";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == localBinPath,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => null,
            getHomeDirectory: () => "/home/test",
            isWindows: () => false);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal(localBinPath, result);
    }

    [Fact]
    public void Resolve_NoPathFound_ReturnsClaude()
    {
        // Arrange
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: _ => false,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal("claude", result);
    }

    [Fact]
    public void Resolve_NullLocalAppData_SkipsNativeWindowsPath()
    {
        // Arrange
        var npmPath = @"C:\Users\test\AppData\Roaming\npm\claude.cmd";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == npmPath,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.Equal(npmPath, result);
    }

    [Fact]
    public void GetDefaultPaths_Windows_ReturnsExpectedPaths()
    {
        // Arrange
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: _ => false,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var paths = resolver.GetDefaultPaths().ToList();

        // Assert
        Assert.Contains(@"C:\Users\test\AppData\Local\Programs\claude-code\claude.exe", paths);
        Assert.Contains(@"C:\Users\test\AppData\Roaming\npm\claude.cmd", paths);
    }

    [Fact]
    public void GetDefaultPaths_Linux_ReturnsExpectedPaths()
    {
        // Arrange
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: _ => false,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => null,
            getHomeDirectory: () => "/home/test",
            isWindows: () => false);

        // Act
        var paths = resolver.GetDefaultPaths().ToList();

        // Assert
        Assert.Contains("/usr/local/bin/claude", paths);
        Assert.Contains("/home/test/.npm-global/bin/claude", paths);
        Assert.Contains("/home/test/.local/bin/claude", paths);
    }
}
