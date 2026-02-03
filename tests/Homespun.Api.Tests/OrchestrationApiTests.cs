using System.Net;
using System.Net.Http.Json;
using Homespun.Features.AgentOrchestration.Controllers;
using Homespun.Features.AgentOrchestration.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests for the Orchestration API endpoints.
/// </summary>
[TestFixture]
public class OrchestrationApiTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Mock<IBranchIdGeneratorService> _mockBranchIdGenerator = null!;

    [SetUp]
    public void SetUp()
    {
        _mockBranchIdGenerator = new Mock<IBranchIdGeneratorService>();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace the real service with our mock
                    services.RemoveAll<IBranchIdGeneratorService>();
                    services.AddSingleton(_mockBranchIdGenerator.Object);
                });
                builder.UseEnvironment("Testing");
            });

        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    #region GenerateBranchId Tests

    [Test]
    public async Task GenerateBranchId_ValidTitle_ReturnsSuccess()
    {
        // Arrange
        _mockBranchIdGenerator.Setup(x => x.GenerateAsync("Add user authentication", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchIdResult(
                Success: true,
                BranchId: "add-user-auth",
                Error: null,
                WasAiGenerated: true));

        var request = new GenerateBranchIdRequest("Add user authentication");

        // Act
        var response = await _client.PostAsJsonAsync("/api/orchestration/generate-branch-id", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<GenerateBranchIdResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True);
            Assert.That(result.BranchId, Is.EqualTo("add-user-auth"));
            Assert.That(result.WasAiGenerated, Is.True);
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public async Task GenerateBranchId_EmptyTitle_ReturnsBadRequest()
    {
        // Arrange
        var request = new GenerateBranchIdRequest("");

        // Act
        var response = await _client.PostAsJsonAsync("/api/orchestration/generate-branch-id", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var result = await response.Content.ReadFromJsonAsync<GenerateBranchIdResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("Title is required"));
        });
    }

    [Test]
    public async Task GenerateBranchId_NullTitle_ReturnsBadRequest()
    {
        // Arrange
        var request = new GenerateBranchIdRequest(null!);

        // Act
        var response = await _client.PostAsJsonAsync("/api/orchestration/generate-branch-id", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GenerateBranchId_AIFallback_ReturnsSuccessWithFallbackFlag()
    {
        // Arrange
        _mockBranchIdGenerator.Setup(x => x.GenerateAsync("Fix login bug", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchIdResult(
                Success: true,
                BranchId: "fix-login-bug",
                Error: null,
                WasAiGenerated: false)); // Fell back to sanitization

        var request = new GenerateBranchIdRequest("Fix login bug");

        // Act
        var response = await _client.PostAsJsonAsync("/api/orchestration/generate-branch-id", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<GenerateBranchIdResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True);
            Assert.That(result.BranchId, Is.EqualTo("fix-login-bug"));
            Assert.That(result.WasAiGenerated, Is.False);
        });
    }

    [Test]
    public async Task GenerateBranchId_ServiceError_ReturnsSuccess_WithFallback()
    {
        // Arrange - even when AI fails, the service falls back to sanitization
        _mockBranchIdGenerator.Setup(x => x.GenerateAsync("Test title", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchIdResult(
                Success: true,
                BranchId: "test-title",
                Error: null,
                WasAiGenerated: false));

        var request = new GenerateBranchIdRequest("Test title");

        // Act
        var response = await _client.PostAsJsonAsync("/api/orchestration/generate-branch-id", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<GenerateBranchIdResponse>();
        Assert.That(result!.Success, Is.True);
    }

    #endregion
}
