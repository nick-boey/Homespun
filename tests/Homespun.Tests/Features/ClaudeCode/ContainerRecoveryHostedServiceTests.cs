using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for ContainerRecoveryHostedService.
/// Tests the startup recovery of containers after server restart.
/// </summary>
[TestFixture]
public class ContainerRecoveryHostedServiceTests
{
    private Mock<IContainerDiscoveryService> _discoveryServiceMock = null!;
    private Mock<ILogger<ContainerRecoveryHostedService>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _discoveryServiceMock = new Mock<IContainerDiscoveryService>();
        _loggerMock = new Mock<ILogger<ContainerRecoveryHostedService>>();
    }

    #region StartAsync Tests

    [Test]
    public async Task StartAsync_NoContainersDiscovered_CompletesSuccessfully()
    {
        // Arrange
        _discoveryServiceMock
            .Setup(d => d.DiscoverHomespunContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DiscoveredContainer>());

        var registerCalled = false;
        var service = new ContainerRecoveryHostedService(
            _discoveryServiceMock.Object,
            container => registerCalled = true,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        // Give the background task time to complete (includes 1s startup delay)
        await Task.Delay(1500);
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(registerCalled, Is.False);
        _discoveryServiceMock.Verify(
            d => d.DiscoverHomespunContainersAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task StartAsync_WithDiscoveredContainers_RegistersEach()
    {
        // Arrange
        var containers = new List<DiscoveredContainer>
        {
            new("c1", "name1", "http://1.1.1.1:8080", "p1", "i1", "/data/p1", DateTime.UtcNow),
            new("c2", "name2", "http://2.2.2.2:8080", "p2", "i2", "/data/p2", DateTime.UtcNow),
        };

        _discoveryServiceMock
            .Setup(d => d.DiscoverHomespunContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);

        var registeredContainers = new List<DiscoveredContainer>();
        var service = new ContainerRecoveryHostedService(
            _discoveryServiceMock.Object,
            container => registeredContainers.Add(container),
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        // Give the background task time to complete (includes 1s startup delay)
        await Task.Delay(1500);
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(registeredContainers, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(registeredContainers[0].ContainerId, Is.EqualTo("c1"));
            Assert.That(registeredContainers[1].ContainerId, Is.EqualTo("c2"));
        });
    }

    [Test]
    public async Task StartAsync_DiscoveryThrows_LogsErrorAndCompletes()
    {
        // Arrange
        _discoveryServiceMock
            .Setup(d => d.DiscoverHomespunContainersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Docker unavailable"));

        var registerCalled = false;
        var service = new ContainerRecoveryHostedService(
            _discoveryServiceMock.Object,
            container => registerCalled = true,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act - should not throw
        await service.StartAsync(cts.Token);
        await Task.Delay(1500);
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(registerCalled, Is.False);
    }

    [Test]
    public async Task StartAsync_RegisterThrows_ContinuesWithNextContainer()
    {
        // Arrange
        var containers = new List<DiscoveredContainer>
        {
            new("c1", "name1", "http://1.1.1.1:8080", "p1", "i1", "/data/p1", DateTime.UtcNow),
            new("c2", "name2", "http://2.2.2.2:8080", "p2", "i2", "/data/p2", DateTime.UtcNow),
        };

        _discoveryServiceMock
            .Setup(d => d.DiscoverHomespunContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);

        var registeredContainers = new List<DiscoveredContainer>();
        var firstCall = true;
        var service = new ContainerRecoveryHostedService(
            _discoveryServiceMock.Object,
            container =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    throw new Exception("Register failed");
                }
                registeredContainers.Add(container);
            },
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(1500);
        await service.StopAsync(CancellationToken.None);

        // Assert - second container should still be registered
        Assert.That(registeredContainers, Has.Count.EqualTo(1));
        Assert.That(registeredContainers[0].ContainerId, Is.EqualTo("c2"));
    }

    #endregion

    #region Cancellation Tests

    [Test]
    public async Task StartAsync_Cancelled_StopsGracefully()
    {
        // Arrange
        var tcs = new TaskCompletionSource<IReadOnlyList<DiscoveredContainer>>();
        _discoveryServiceMock
            .Setup(d => d.DiscoverHomespunContainersAsync(It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var registerCalled = false;
        var service = new ContainerRecoveryHostedService(
            _discoveryServiceMock.Object,
            container => registerCalled = true,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        // Cancel before discovery completes
        cts.Cancel();
        tcs.SetCanceled();

        // Wait for the service to handle cancellation
        await Task.Delay(50);

        // Assert - should not have registered anything
        Assert.That(registerCalled, Is.False);
    }

    #endregion
}
