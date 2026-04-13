using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode.Services;

[TestFixture]
public class DefaultPromptsInitializationServiceTests
{
    private Mock<IAgentPromptService> _promptService = null!;
    private Mock<ILogger<DefaultPromptsInitializationService>> _logger = null!;
    private DefaultPromptsInitializationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _promptService = new Mock<IAgentPromptService>();
        _logger = new Mock<ILogger<DefaultPromptsInitializationService>>();
        _service = new DefaultPromptsInitializationService(_promptService.Object, _logger.Object);
    }

    [Test]
    public async Task StartAsync_CallsEnsureDefaultPromptsAsync()
    {
        await _service.StartAsync(CancellationToken.None);

        _promptService.Verify(s => s.EnsureDefaultPromptsAsync(), Times.Once);
    }

    [Test]
    public async Task StartAsync_WhenCancelled_DoesNotCallEnsureDefaults()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await _service.StartAsync(cts.Token);

        _promptService.Verify(s => s.EnsureDefaultPromptsAsync(), Times.Never);
    }

    [Test]
    public async Task StartAsync_WhenEnsureDefaultsThrows_DoesNotPropagate()
    {
        _promptService.Setup(s => s.EnsureDefaultPromptsAsync())
            .ThrowsAsync(new InvalidOperationException("test error"));

        Assert.DoesNotThrowAsync(() => _service.StartAsync(CancellationToken.None));
    }

    [Test]
    public async Task StopAsync_CompletesSuccessfully()
    {
        await _service.StopAsync(CancellationToken.None);

        // No exception means success
        Assert.Pass();
    }
}
