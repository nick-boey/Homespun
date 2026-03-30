using Homespun.Features.Observability.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class DataDirectoryHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WritableDirectory_ReturnsHealthy()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var check = new DataDirectoryHealthCheck(tempDir);

            // Act
            var result = await check.CheckHealthAsync(
                new HealthCheckContext
                {
                    Registration = new HealthCheckRegistration("test", check, null, null)
                });

            // Assert
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task CheckHealthAsync_NonExistentDirectory_ReturnsUnhealthy()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var check = new DataDirectoryHealthCheck(nonExistentDir);

        // Act
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", check, null, null)
            });

        // Assert
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
        Assert.That(result.Description, Does.Contain("does not exist"));
    }
}
