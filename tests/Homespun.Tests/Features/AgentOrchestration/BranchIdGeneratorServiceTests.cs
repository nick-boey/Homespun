using Homespun.Features.AgentOrchestration.Services;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class BranchIdGeneratorServiceTests
{
    private Mock<IMiniPromptService> _mockMiniPromptService = null!;
    private BranchIdGeneratorService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockMiniPromptService = new Mock<IMiniPromptService>();
        _service = new BranchIdGeneratorService(_mockMiniPromptService.Object);
    }

    #region Input Validation Tests

    [Test]
    public void GenerateAsync_EmptyTitle_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.GenerateAsync(""));
    }

    [Test]
    public void GenerateAsync_NullTitle_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.GenerateAsync(null!));
    }

    [Test]
    public void GenerateAsync_WhitespaceTitle_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.GenerateAsync("   "));
    }

    #endregion

    #region Successful Generation Tests

    [Test]
    public async Task GenerateAsync_ValidTitle_ReturnsBranchId()
    {
        // Arrange
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "add-user-auth",
                Error: null,
                CostUsd: 0.0001m,
                DurationMs: 100));

        // Act
        var result = await _service.GenerateAsync("Add user authentication");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.BranchId, Is.EqualTo("add-user-auth"));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.WasAiGenerated, Is.True);
        });
    }

    [Test]
    public async Task GenerateAsync_LongTitle_ReturnsCondensedBranchId()
    {
        // Arrange
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "fix-mobile-login",
                Error: null,
                CostUsd: 0.0001m,
                DurationMs: 100));

        // Act
        var result = await _service.GenerateAsync("Fix login button not working on mobile devices when user tries to sign in");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.BranchId, Is.EqualTo("fix-mobile-login"));
            Assert.That(result.WasAiGenerated, Is.True);
        });
    }

    #endregion

    #region Output Validation Tests

    [Test]
    public async Task GenerateAsync_AIReturnsValidFormat_UsesAIResponse()
    {
        // Arrange - valid format: 2-4 lowercase hyphen-separated words
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "add-dark-mode",
                Error: null,
                CostUsd: null,
                DurationMs: null));

        // Act
        var result = await _service.GenerateAsync("Implement dark mode toggle");

        // Assert
        Assert.That(result.BranchId, Is.EqualTo("add-dark-mode"));
        Assert.That(result.WasAiGenerated, Is.True);
    }

    [Test]
    public async Task GenerateAsync_AIReturnsWithWhitespace_TrimsResponse()
    {
        // Arrange - AI might return with leading/trailing whitespace or newlines
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "  add-feature  \n",
                Error: null,
                CostUsd: null,
                DurationMs: null));

        // Act
        var result = await _service.GenerateAsync("Add a new feature");

        // Assert
        Assert.That(result.BranchId, Is.EqualTo("add-feature"));
    }

    [Test]
    public async Task GenerateAsync_AIReturnsUppercase_ConvertsToLowercase()
    {
        // Arrange
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "Add-User-Auth",
                Error: null,
                CostUsd: null,
                DurationMs: null));

        // Act
        var result = await _service.GenerateAsync("Add user authentication");

        // Assert
        Assert.That(result.BranchId, Is.EqualTo("add-user-auth"));
    }

    [Test]
    public async Task GenerateAsync_AIReturnsInvalidChars_SanitizesResponse()
    {
        // Arrange - AI returns with special characters
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "add_feature@test!",
                Error: null,
                CostUsd: null,
                DurationMs: null));

        // Act
        var result = await _service.GenerateAsync("Add a feature");

        // Assert - should sanitize invalid characters
        Assert.That(result.BranchId, Does.Not.Contain("@"));
        Assert.That(result.BranchId, Does.Not.Contain("!"));
        Assert.That(result.BranchId, Does.Not.Contain("_"));
    }

    [Test]
    public async Task GenerateAsync_AIReturnsTooManyWords_FallsBackToSanitization()
    {
        // Arrange - AI returns more than 4 words
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "this-is-way-too-many-words-for-a-branch",
                Error: null,
                CostUsd: null,
                DurationMs: null));

        // Act
        var result = await _service.GenerateAsync("Some title");

        // Assert - should fall back to sanitization
        Assert.That(result.WasAiGenerated, Is.False);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task GenerateAsync_AIReturnsSingleWord_FallsBackToSanitization()
    {
        // Arrange - AI returns only 1 word (need at least 2)
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "feature",
                Error: null,
                CostUsd: null,
                DurationMs: null));

        // Act
        var result = await _service.GenerateAsync("Add new feature implementation");

        // Assert - should fall back to sanitization
        Assert.That(result.WasAiGenerated, Is.False);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task GenerateAsync_AIReturnsEmpty_FallsBackToSanitization()
    {
        // Arrange
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: true,
                Response: "",
                Error: null,
                CostUsd: null,
                DurationMs: null));

        // Act
        var result = await _service.GenerateAsync("Add user authentication");

        // Assert - should fall back to sanitization
        Assert.That(result.WasAiGenerated, Is.False);
        Assert.That(result.Success, Is.True);
        Assert.That(result.BranchId, Is.Not.Empty);
    }

    #endregion

    #region Fallback Tests

    [Test]
    public async Task GenerateAsync_AIFails_FallsBackToSanitization()
    {
        // Arrange
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(
                Success: false,
                Response: null,
                Error: "Service unavailable",
                CostUsd: null,
                DurationMs: null));

        // Act
        var result = await _service.GenerateAsync("Add user authentication");

        // Assert - should fall back to sanitization of title
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.BranchId, Is.EqualTo("add-user-authentication"));
            Assert.That(result.WasAiGenerated, Is.False);
            Assert.That(result.Error, Is.Null); // No error since fallback succeeded
        });
    }

    [Test]
    public async Task GenerateAsync_AIThrowsException_FallsBackToSanitization()
    {
        // Arrange
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        // Act
        var result = await _service.GenerateAsync("Fix authentication bug");

        // Assert - should fall back to sanitization
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.BranchId, Is.EqualTo("fix-authentication-bug"));
            Assert.That(result.WasAiGenerated, Is.False);
        });
    }

    [Test]
    public void GenerateAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert - cancellation should propagate
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _service.GenerateAsync("Test title", cts.Token));
    }

    #endregion

    #region Sanitization Consistency Tests

    [Test]
    public async Task GenerateAsync_FallbackSanitization_MatchesBranchNameGenerator()
    {
        // Arrange - Make AI fail to trigger fallback
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(false, null, "Error", null, null));

        // Act
        var result = await _service.GenerateAsync("Fix Login Button");

        // Assert - should match existing BranchNameGenerator behavior
        Assert.That(result.BranchId, Is.EqualTo("fix-login-button"));
    }

    [Test]
    public async Task GenerateAsync_FallbackSanitization_HandlesSpecialCharacters()
    {
        // Arrange - Make AI fail to trigger fallback
        _mockMiniPromptService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(false, null, "Error", null, null));

        // Act
        var result = await _service.GenerateAsync("Fix bug! @user #123");

        // Assert
        Assert.That(result.BranchId, Is.EqualTo("fix-bug-user-123"));
    }

    #endregion
}
