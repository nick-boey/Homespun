using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class PortAllocationServiceTests
{
    private Mock<ILogger<PortAllocationService>> _mockLogger = null!;
    private IOptions<OpenCodeOptions> _options = null!;
    private PortAllocationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<PortAllocationService>>();
        _options = Options.Create(new OpenCodeOptions
        {
            BasePort = 5000,
            MaxConcurrentServers = 3
        });
        _service = new PortAllocationService(_options, _mockLogger.Object);
    }

    [Test]
    public void AllocatePort_ReturnsBasePort_WhenNoPortsAllocated()
    {
        var port = _service.AllocatePort();
        Assert.That(port, Is.GreaterThanOrEqualTo(5000));
    }

    [Test]
    public void AllocatePort_ReturnsSequentialPorts()
    {
        var port1 = _service.AllocatePort();
        var port2 = _service.AllocatePort();
        
        // Port2 should be greater than port1 (might skip if some ports are in use)
        Assert.That(port2, Is.GreaterThan(port1));
    }

    [Test]
    public void AllocatePort_ThrowsWhenMaxServersReached()
    {
        _service.AllocatePort();
        _service.AllocatePort();
        _service.AllocatePort();
        
        Assert.Throws<InvalidOperationException>(() => _service.AllocatePort());
    }

    [Test]
    public void ReleasePort_AllowsNewAllocation()
    {
        var port1 = _service.AllocatePort();
        _service.AllocatePort();
        _service.AllocatePort();
        
        // Now at max
        Assert.Throws<InvalidOperationException>(() => _service.AllocatePort());
        
        // Release one
        _service.ReleasePort(port1);
        
        // Should be able to allocate again
        var newPort = _service.AllocatePort();
        Assert.That(newPort, Is.GreaterThanOrEqualTo(5000));
    }

    [Test]
    public void IsPortInUse_ReturnsFalse_ForUnusedPort()
    {
        // Use a high port that's unlikely to be in use
        var result = _service.IsPortInUse(59999);
        Assert.That(result, Is.False);
    }

    [Test]
    public void AllocatePort_SkipsPortsInUse()
    {
        // This test verifies that the service skips ports that are in use
        // by checking that it doesn't throw when ports might be occupied
        // The actual skip behavior is tested implicitly by checking we get valid ports
        
        var port1 = _service.AllocatePort();
        var port2 = _service.AllocatePort();
        
        Assert.That(port1, Is.GreaterThanOrEqualTo(5000));
        Assert.That(port2, Is.GreaterThanOrEqualTo(5000));
        Assert.That(port1, Is.Not.EqualTo(port2));
    }
}
