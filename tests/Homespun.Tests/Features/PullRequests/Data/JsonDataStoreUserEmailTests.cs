using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Tests.Features.PullRequests.Data;

[TestFixture]
public class JsonDataStoreUserEmailTests
{
    private string _tempFilePath = null!;
    private JsonDataStore _dataStore = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"homespun-test-{Guid.NewGuid()}.json");
        _dataStore = new JsonDataStore(_tempFilePath, NullLogger<JsonDataStore>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    [Test]
    public void UserEmail_WhenNotSet_ReturnsNull()
    {
        // Assert
        Assert.That(_dataStore.UserEmail, Is.Null);
    }

    [Test]
    public async Task SetUserEmailAsync_SetsEmail()
    {
        // Act
        await _dataStore.SetUserEmailAsync("test@example.com");

        // Assert
        Assert.That(_dataStore.UserEmail, Is.EqualTo("test@example.com"));
    }

    [Test]
    public async Task SetUserEmailAsync_UpdatesExistingEmail()
    {
        // Arrange
        await _dataStore.SetUserEmailAsync("old@example.com");

        // Act
        await _dataStore.SetUserEmailAsync("new@example.com");

        // Assert
        Assert.That(_dataStore.UserEmail, Is.EqualTo("new@example.com"));
    }

    [Test]
    public async Task SetUserEmailAsync_PersistsToFile()
    {
        // Arrange
        await _dataStore.SetUserEmailAsync("test@example.com");

        // Act - Create a new instance to verify persistence
        var newDataStore = new JsonDataStore(_tempFilePath, NullLogger<JsonDataStore>.Instance);

        // Assert
        Assert.That(newDataStore.UserEmail, Is.EqualTo("test@example.com"));
    }
}
