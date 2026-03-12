using Homespun.Features.PullRequests.Data;
using Homespun.Features.Settings.Controllers;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Homespun.Tests.Features.Settings;

[TestFixture]
public class SettingsControllerTests
{
    private Mock<IDataStore> _dataStoreMock = null!;
    private SettingsController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStoreMock = new Mock<IDataStore>();
        _controller = new SettingsController(_dataStoreMock.Object);
    }

    #region GetUserSettings Tests

    [Test]
    public void GetUserSettings_ReturnsUserEmail()
    {
        // Arrange
        _dataStoreMock.Setup(d => d.UserEmail).Returns("test@example.com");

        // Act
        var result = _controller.GetUserSettings();

        // Assert
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var response = okResult!.Value as UserSettingsResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.UserEmail, Is.EqualTo("test@example.com"));
    }

    [Test]
    public void GetUserSettings_WhenNoEmailSet_ReturnsNull()
    {
        // Arrange
        _dataStoreMock.Setup(d => d.UserEmail).Returns((string?)null);

        // Act
        var result = _controller.GetUserSettings();

        // Assert
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var response = okResult!.Value as UserSettingsResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.UserEmail, Is.Null);
    }

    #endregion

    #region UpdateUserEmail Tests

    [Test]
    public async Task UpdateUserEmail_ValidEmail_UpdatesAndReturnsResponse()
    {
        // Arrange
        var request = new UpdateUserEmailRequest { Email = "new@example.com" };
        _dataStoreMock.Setup(d => d.SetUserEmailAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        _dataStoreMock.Setup(d => d.UserEmail).Returns("new@example.com");

        // Act
        var result = await _controller.UpdateUserEmail(request);

        // Assert
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var response = okResult!.Value as UserSettingsResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.UserEmail, Is.EqualTo("new@example.com"));

        _dataStoreMock.Verify(d => d.SetUserEmailAsync("new@example.com"), Times.Once);
    }

    [Test]
    public async Task UpdateUserEmail_EmptyEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new UpdateUserEmailRequest { Email = "" };

        // Act
        var result = await _controller.UpdateUserEmail(request);

        // Assert
        var badRequest = result.Result as BadRequestObjectResult;
        Assert.That(badRequest, Is.Not.Null);
        Assert.That(badRequest!.Value, Is.EqualTo("Email is required"));
    }

    [Test]
    public async Task UpdateUserEmail_WhitespaceEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new UpdateUserEmailRequest { Email = "   " };

        // Act
        var result = await _controller.UpdateUserEmail(request);

        // Assert
        var badRequest = result.Result as BadRequestObjectResult;
        Assert.That(badRequest, Is.Not.Null);
        Assert.That(badRequest!.Value, Is.EqualTo("Email is required"));
    }

    [Test]
    public async Task UpdateUserEmail_InvalidEmailFormat_ReturnsBadRequest()
    {
        // Arrange
        var request = new UpdateUserEmailRequest { Email = "notanemail" };

        // Act
        var result = await _controller.UpdateUserEmail(request);

        // Assert
        var badRequest = result.Result as BadRequestObjectResult;
        Assert.That(badRequest, Is.Not.Null);
        Assert.That(badRequest!.Value, Is.EqualTo("Invalid email format"));
    }

    [Test]
    public async Task UpdateUserEmail_TrimsWhitespace()
    {
        // Arrange
        var request = new UpdateUserEmailRequest { Email = "  test@example.com  " };
        _dataStoreMock.Setup(d => d.SetUserEmailAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        _dataStoreMock.Setup(d => d.UserEmail).Returns("test@example.com");

        // Act
        await _controller.UpdateUserEmail(request);

        // Assert
        _dataStoreMock.Verify(d => d.SetUserEmailAsync("test@example.com"), Times.Once);
    }

    #endregion
}
