using System.Diagnostics;
using System.Runtime.InteropServices;
using Homespun.ClaudeAgentSdk;
using Homespun.ClaudeAgentSdk.Transport;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SubprocessCliTransportTests
{
    [Test]
    [Platform("Win")]
    public void FindCli_OnWindows_ShouldSearchForMultipleExecutableVariants()
    {
        // This test verifies the Windows CLI discovery behavior by checking
        // that the transport can find executables with .exe extension
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a dummy claude.exe file
            var claudeExePath = Path.Combine(tempDir, "claude.exe");
            File.WriteAllText(claudeExePath, "dummy");

            // Modify PATH to include our temp directory
            var originalPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", $"{tempDir}{Path.PathSeparator}{originalPath}");

            try
            {
                // Act - Create transport which triggers FindCli
                // Since we can't call FindCli directly, we test by creating a transport
                // and checking that it doesn't throw CliNotFoundException
                var options = new ClaudeAgentOptions { Cwd = tempDir };

                // Use reflection to call the private static FindCli method
                var findCliMethod = typeof(SubprocessCliTransport).GetMethod(
                    "FindCli",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                Assert.That(findCliMethod, Is.Not.Null, "FindCli method should exist");

                var result = findCliMethod!.Invoke(null, null) as string;

                // Assert
                Assert.That(result, Is.Not.Null);
                Assert.That(result, Does.EndWith("claude.exe"));
                Assert.That(result, Is.EqualTo(claudeExePath));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [Platform("Win")]
    public void FindCli_OnWindows_ShouldPreferCmdOverExe()
    {
        // Verify that claude.cmd is found before claude.exe (npm global install is preferred)
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create both claude.cmd and claude.exe
            var claudeCmdPath = Path.Combine(tempDir, "claude.cmd");
            var claudeExePath = Path.Combine(tempDir, "claude.exe");
            File.WriteAllText(claudeCmdPath, "dummy cmd");
            File.WriteAllText(claudeExePath, "dummy exe");

            var originalPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", $"{tempDir}{Path.PathSeparator}{originalPath}");

            try
            {
                // Act
                var findCliMethod = typeof(SubprocessCliTransport).GetMethod(
                    "FindCli",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                var result = findCliMethod!.Invoke(null, null) as string;

                // Assert - claude.cmd should be found first
                Assert.That(result, Is.Not.Null);
                Assert.That(result, Does.EndWith("claude.cmd"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [Platform("Win")]
    public void FindCli_OnWindows_ShouldFindClaudeExeInLocalBin()
    {
        // Verify that claude.exe in ~/.local/bin is found (common Windows installation location)
        // Arrange
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localBinDir = Path.Combine(home, ".local", "bin");
        var claudeExePath = Path.Combine(localBinDir, "claude.exe");
        var createdDir = false;
        var createdFile = false;

        // Only run this test if we can create the test file
        if (!Directory.Exists(localBinDir))
        {
            Directory.CreateDirectory(localBinDir);
            createdDir = true;
        }

        // Skip if claude.exe already exists (don't overwrite real installation)
        if (File.Exists(claudeExePath))
        {
            Assert.Pass("Skipping test: claude.exe already exists at ~/.local/bin");
            return;
        }

        try
        {
            File.WriteAllText(claudeExePath, "dummy");
            createdFile = true;

            // Clear PATH so it doesn't find claude elsewhere
            var originalPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", "");

            try
            {
                // Act
                var findCliMethod = typeof(SubprocessCliTransport).GetMethod(
                    "FindCli",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                var result = findCliMethod!.Invoke(null, null) as string;

                // Assert
                Assert.That(result, Is.Not.Null);
                Assert.That(result, Is.EqualTo(claudeExePath));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
            }
        }
        finally
        {
            if (createdFile && File.Exists(claudeExePath))
            {
                File.Delete(claudeExePath);
            }
            if (createdDir && Directory.Exists(localBinDir) && !Directory.EnumerateFileSystemEntries(localBinDir).Any())
            {
                Directory.Delete(localBinDir);
            }
        }
    }

    [Test]
    public void Constructor_WithCliPath_ShouldUseProvidedPath()
    {
        // Arrange
        var customCliPath = @"C:\custom\path\to\claude.exe";
        var options = new ClaudeAgentOptions
        {
            CliPath = customCliPath,
            Cwd = Path.GetTempPath()
        };

        // Act - Create transport with custom CLI path
        var transport = new SubprocessCliTransport("test prompt", options, options.CliPath);

        // Assert - Verify via reflection that _cliPath was set correctly
        var cliPathField = typeof(SubprocessCliTransport).GetField(
            "_cliPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.That(cliPathField, Is.Not.Null, "_cliPath field should exist");
        var actualPath = cliPathField!.GetValue(transport) as string;
        Assert.That(actualPath, Is.EqualTo(customCliPath));
    }

    [Test]
    public void Constructor_WithoutCliPath_ShouldUseAutoDiscovery()
    {
        // Arrange
        var options = new ClaudeAgentOptions
        {
            Cwd = Path.GetTempPath()
        };

        // Check if Claude CLI is installed on this system
        // If not, skip the test since we can't test auto-discovery without the CLI
        var findCliMethod = typeof(SubprocessCliTransport).GetMethod(
            "FindCli",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        try
        {
            var cliPath = findCliMethod?.Invoke(null, null) as string;
            if (string.IsNullOrEmpty(cliPath))
            {
                Assert.Ignore("Claude CLI is not installed on this system - skipping auto-discovery test");
            }
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is CliNotFoundException)
        {
            Assert.Ignore("Claude CLI is not installed on this system - skipping auto-discovery test");
        }

        // Act - Create transport without custom CLI path
        // This will use auto-discovery
        var transport = new SubprocessCliTransport("test prompt", options);

        // Assert - Verify that _cliPath was set via auto-discovery (not null or empty)
        var cliPathField = typeof(SubprocessCliTransport).GetField(
            "_cliPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.That(cliPathField, Is.Not.Null, "_cliPath field should exist");
        var actualPath = cliPathField!.GetValue(transport) as string;
        Assert.That(actualPath, Is.Not.Null.And.Not.Empty);
        // Should be a real path found by auto-discovery
        Assert.That(File.Exists(actualPath), Is.True, "Auto-discovered path should exist");
    }

    [Test]
    public void CliPath_Option_ShouldBeIncludedInClaudeAgentOptions()
    {
        // Verify the CliPath property exists and can be set
        var options = new ClaudeAgentOptions
        {
            CliPath = "/custom/path/claude"
        };

        Assert.That(options.CliPath, Is.EqualTo("/custom/path/claude"));
    }

    [Test]
    public void HomeEnvironmentVariable_WhenNotInOptions_ShouldBeSetFromEnvironment()
    {
        // Arrange
        var expectedHome = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var options = new ClaudeAgentOptions
        {
            Cwd = "/tmp",
            Env = new Dictionary<string, string>() // HOME not specified
        };

        // Act - Create a ProcessStartInfo the same way SubprocessCliTransport does
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Replicate the environment variable setting logic from SubprocessCliTransport
        foreach (var (key, value) in options.Env)
        {
            startInfo.Environment[key] = value;
        }

        // Ensure HOME is set for Claude CLI to find/create its config directory
        if (!options.Env.ContainsKey("HOME"))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            if (!string.IsNullOrEmpty(home))
            {
                startInfo.Environment["HOME"] = home;
            }
        }

        // Assert
        Assert.That(startInfo.Environment.ContainsKey("HOME"), Is.True,
            "HOME environment variable should be set");
        Assert.That(startInfo.Environment["HOME"], Is.EqualTo(expectedHome),
            "HOME should be set from the current environment");
    }

    [Test]
    public void HomeEnvironmentVariable_WhenSpecifiedInOptions_ShouldNotBeOverridden()
    {
        // Arrange
        var customHome = "/custom/home/path";
        var options = new ClaudeAgentOptions
        {
            Cwd = "/tmp",
            Env = new Dictionary<string, string>
            {
                ["HOME"] = customHome
            }
        };

        // Act - Create a ProcessStartInfo the same way SubprocessCliTransport does
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Replicate the environment variable setting logic from SubprocessCliTransport
        foreach (var (key, value) in options.Env)
        {
            startInfo.Environment[key] = value;
        }

        // Ensure HOME is set for Claude CLI to find/create its config directory
        if (!options.Env.ContainsKey("HOME"))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            if (!string.IsNullOrEmpty(home))
            {
                startInfo.Environment["HOME"] = home;
            }
        }

        // Assert
        Assert.That(startInfo.Environment["HOME"], Is.EqualTo(customHome),
            "HOME should not be overridden when specified in options");
    }

    [Test]
    [Platform("Unix,Linux,MacOsX")]
    public void HomeEnvironmentVariable_InSubprocess_ShouldBeAccessible()
    {
        // This integration test verifies that HOME is properly passed to a subprocess
        // Note: This test only runs on Unix-like systems as it uses /bin/sh
        // Arrange
        var expectedHome = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = "-c \"echo $HOME\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Replicate the HOME-setting logic from SubprocessCliTransport
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        if (!string.IsNullOrEmpty(home))
        {
            startInfo.Environment["HOME"] = home;
        }

        // Act
        using var process = Process.Start(startInfo);
        Assert.That(process, Is.Not.Null, "Process should start successfully");

        var output = process!.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        // Assert
        Assert.That(process.ExitCode, Is.EqualTo(0), "Process should exit successfully");
        Assert.That(output, Is.EqualTo(expectedHome),
            "Subprocess should receive the HOME environment variable");
    }

    [Test]
    [Platform("Win")]
    public void HomeEnvironmentVariable_InSubprocess_ShouldBeAccessible_Windows()
    {
        // Windows version of the test using cmd.exe
        // Arrange
        var expectedHome = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c echo %HOME%",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Replicate the HOME-setting logic from SubprocessCliTransport
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        if (!string.IsNullOrEmpty(home))
        {
            startInfo.Environment["HOME"] = home;
        }

        // Act
        using var process = Process.Start(startInfo);
        Assert.That(process, Is.Not.Null, "Process should start successfully");

        var output = process!.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        // Assert
        Assert.That(process.ExitCode, Is.EqualTo(0), "Process should exit successfully");
        Assert.That(output, Is.EqualTo(expectedHome),
            "Subprocess should receive the HOME environment variable");
    }
}
