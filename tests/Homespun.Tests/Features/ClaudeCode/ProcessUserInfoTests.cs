using Homespun.Features.ClaudeCode.Services;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for ProcessUserInfo utility class.
/// These tests verify the UID/GID retrieval for Docker --user flag.
/// </summary>
[TestFixture]
public class ProcessUserInfoTests
{
    [Test]
    [Platform(Include = "Linux")]
    public void GetUid_OnLinux_ReturnsValidValue()
    {
        // Act
        var uid = ProcessUserInfo.GetUid();

        // Assert - UID should be a valid non-negative value
        Assert.That(uid, Is.GreaterThanOrEqualTo(0u));
    }

    [Test]
    [Platform(Include = "Linux")]
    public void GetGid_OnLinux_ReturnsValidValue()
    {
        // Act
        var gid = ProcessUserInfo.GetGid();

        // Assert - GID should be a valid non-negative value
        Assert.That(gid, Is.GreaterThanOrEqualTo(0u));
    }

    [Test]
    [Platform(Include = "Linux")]
    public void GetDockerUserFlag_OnLinux_ReturnsUidGidFormat()
    {
        // Act
        var userFlag = ProcessUserInfo.GetDockerUserFlag();

        // Assert - should match pattern "uid:gid" where both are numbers
        Assert.That(userFlag, Is.Not.Null);
        Assert.That(userFlag, Does.Match(@"^\d+:\d+$"));
    }

    [Test]
    [Platform(Include = "Linux")]
    public void GetDockerUserFlag_OnLinux_ContainsCurrentUidAndGid()
    {
        // Arrange
        var expectedUid = ProcessUserInfo.GetUid();
        var expectedGid = ProcessUserInfo.GetGid();
        var expected = $"{expectedUid}:{expectedGid}";

        // Act
        var userFlag = ProcessUserInfo.GetDockerUserFlag();

        // Assert
        Assert.That(userFlag, Is.EqualTo(expected));
    }

    [Test]
    [Platform(Include = "Win")]
    public void GetDockerUserFlag_OnWindows_ReturnsNull()
    {
        // Act
        var userFlag = ProcessUserInfo.GetDockerUserFlag();

        // Assert - Windows doesn't use UID/GID
        Assert.That(userFlag, Is.Null);
    }

    [Test]
    [Platform(Include = "MacOsX")]
    public void GetDockerUserFlag_OnMacOS_ReturnsNull()
    {
        // Act
        var userFlag = ProcessUserInfo.GetDockerUserFlag();

        // Assert - macOS Docker Desktop handles permissions differently
        Assert.That(userFlag, Is.Null);
    }
}
