using Homespun.Features.Git;
using Homespun.Features.Git.Controllers;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.Projects;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.Git.Controllers;

[TestFixture]
public class ClonesControllerTests
{
    private ClonesController _controller = null!;
    private Mock<IGitCloneService> _cloneServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _cloneServiceMock = new Mock<IGitCloneService>();
        _projectServiceMock = new Mock<IProjectService>();
        _controller = new ClonesController(
            _cloneServiceMock.Object,
            _projectServiceMock.Object);
    }

    #region GetSessionBranchInfo Tests

    [Test]
    public async Task GetSessionBranchInfo_ValidWorkingDirectory_ReturnsOkWithBranchInfo()
    {
        // Arrange
        var workingDirectory = "/test/clone/workdir";
        var expectedInfo = new SessionBranchInfo
        {
            BranchName = "feature/test",
            CommitSha = "abc1234",
            CommitMessage = "Test commit",
            CommitDate = DateTime.UtcNow,
            AheadCount = 2,
            BehindCount = 1,
            HasUncommittedChanges = true
        };

        _cloneServiceMock.Setup(s => s.GetSessionBranchInfoAsync(workingDirectory))
            .ReturnsAsync(expectedInfo);

        // Act
        var result = await _controller.GetSessionBranchInfo(workingDirectory);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var branchInfo = okResult.Value as SessionBranchInfo;
        Assert.That(branchInfo, Is.Not.Null);
        Assert.That(branchInfo!.BranchName, Is.EqualTo("feature/test"));
        Assert.That(branchInfo.CommitSha, Is.EqualTo("abc1234"));
        Assert.That(branchInfo.AheadCount, Is.EqualTo(2));
        Assert.That(branchInfo.HasUncommittedChanges, Is.True);
    }

    [Test]
    public async Task GetSessionBranchInfo_ServiceReturnsNull_ReturnsNotFound()
    {
        // Arrange
        var workingDirectory = "/nonexistent/directory";

        _cloneServiceMock.Setup(s => s.GetSessionBranchInfoAsync(workingDirectory))
            .ReturnsAsync((SessionBranchInfo?)null);

        // Act
        var result = await _controller.GetSessionBranchInfo(workingDirectory);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task GetSessionBranchInfo_DetachedHead_ReturnsOkWithNullBranchName()
    {
        // Arrange
        var workingDirectory = "/test/clone/workdir";
        var expectedInfo = new SessionBranchInfo
        {
            BranchName = null, // Detached HEAD
            CommitSha = "def5678",
            CommitMessage = "Detached commit",
            CommitDate = DateTime.UtcNow,
            AheadCount = 0,
            BehindCount = 0,
            HasUncommittedChanges = false
        };

        _cloneServiceMock.Setup(s => s.GetSessionBranchInfoAsync(workingDirectory))
            .ReturnsAsync(expectedInfo);

        // Act
        var result = await _controller.GetSessionBranchInfo(workingDirectory);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var branchInfo = okResult.Value as SessionBranchInfo;
        Assert.That(branchInfo, Is.Not.Null);
        Assert.That(branchInfo!.BranchName, Is.Null);
        Assert.That(branchInfo.CommitSha, Is.EqualTo("def5678"));
    }

    #endregion
}
