using System.Diagnostics;
using Microsoft.Playwright;

namespace Homespun.E2E.Tests;

/// <summary>
/// Manages the Homespun application process for E2E tests.
/// </summary>
[SetUpFixture]
public class HomespunFixture
{
    private static Process? _appProcess;
    public static string BaseUrl { get; private set; } = "http://localhost:5000";

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Install Playwright browsers
        var exitCode = Microsoft.Playwright.Program.Main(["install", "--with-deps", "chromium"]);
        if (exitCode != 0)
        {
            throw new Exception($"Playwright browser installation failed with exit code {exitCode}");
        }

        // Check if we should start the application
        var startApp = Environment.GetEnvironmentVariable("E2E_START_APP") ?? "true";
        var customBaseUrl = Environment.GetEnvironmentVariable("E2E_BASE_URL");

        if (!string.IsNullOrEmpty(customBaseUrl))
        {
            BaseUrl = customBaseUrl;
            Console.WriteLine($"Using custom base URL: {BaseUrl}");
            return;
        }

        if (startApp.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("E2E_START_APP is false, expecting external app");
            return;
        }

        // Find the project directory
        var projectDir = FindProjectDirectory();
        if (projectDir == null)
        {
            throw new Exception("Could not find Homespun project directory");
        }

        Console.WriteLine($"Starting Homespun application from: {projectDir}");

        // Start the application
        _appProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --no-build --configuration {GetBuildConfiguration()}",
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment =
                {
                    ["ASPNETCORE_URLS"] = BaseUrl,
                    ["ASPNETCORE_ENVIRONMENT"] = "Development"
                }
            }
        };

        _appProcess.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[App] {e.Data}");
        };

        _appProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[App Error] {e.Data}");
        };

        _appProcess.Start();
        _appProcess.BeginOutputReadLine();
        _appProcess.BeginErrorReadLine();

        // Wait for the application to start
        await WaitForApplicationAsync(BaseUrl, TimeSpan.FromSeconds(30));

        Console.WriteLine("Homespun application started successfully");
    }

    [OneTimeTearDown]
    public void GlobalTeardown()
    {
        if (_appProcess != null && !_appProcess.HasExited)
        {
            Console.WriteLine("Stopping Homespun application...");
            _appProcess.Kill(entireProcessTree: true);
            _appProcess.WaitForExit(5000);
            _appProcess.Dispose();
            _appProcess = null;
        }
    }

    private static string GetBuildConfiguration()
    {
        // Check environment variable first (CI can set this)
        var envConfig = Environment.GetEnvironmentVariable("E2E_CONFIGURATION");
        if (!string.IsNullOrEmpty(envConfig))
            return envConfig;

        // Check if we're running in Release mode based on assembly path
        var assemblyPath = typeof(HomespunFixture).Assembly.Location;
        if (assemblyPath.Contains("Release", StringComparison.OrdinalIgnoreCase))
            return "Release";

        return "Debug";
    }

    private static string? FindProjectDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var searchPaths = new[]
        {
            Path.Combine(currentDir, "..", "..", "..", "..", "..", "src", "Homespun"),
            Path.Combine(currentDir, "..", "..", "..", "..", "src", "Homespun"),
            Path.Combine(currentDir, "..", "..", "..", "src", "Homespun"),
            Path.Combine(currentDir, "..", "..", "src", "Homespun"),
            Path.Combine(currentDir, "..", "src", "Homespun"),
            Path.Combine(currentDir, "src", "Homespun")
        };

        foreach (var path in searchPaths)
        {
            var fullPath = Path.GetFullPath(path);
            var projectFile = Path.Combine(fullPath, "Homespun.csproj");
            if (File.Exists(projectFile))
            {
                return fullPath;
            }
        }

        return null;
    }

    private static async Task WaitForApplicationAsync(string baseUrl, TimeSpan timeout)
    {
        using var client = new HttpClient();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await client.GetAsync($"{baseUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Application not ready yet
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Application did not start within {timeout.TotalSeconds} seconds");
    }
}
