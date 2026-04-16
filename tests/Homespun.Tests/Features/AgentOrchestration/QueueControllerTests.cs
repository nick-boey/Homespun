using FleeceIssue = global::Fleece.Core.Models.Issue;
using FleeceIssueStatus = global::Fleece.Core.Models.IssueStatus;
using FleeceIssueType = global::Fleece.Core.Models.IssueType;
using Homespun.Features.AgentOrchestration.Controllers;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class QueueControllerTests
{
    private Mock<IQueueCoordinator> _queueCoordinatorMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private QueueController _controller = null!;

    private static readonly Project TestProject = new()
    {
        Id = "project-123",
        Name = "Test Project",
        LocalPath = "/path/to/project",
        DefaultBranch = "main"
    };

    [SetUp]
    public void SetUp()
    {
        _queueCoordinatorMock = new Mock<IQueueCoordinator>();
        _projectServiceMock = new Mock<IProjectService>();

        _controller = new QueueController(
            _queueCoordinatorMock.Object,
            _projectServiceMock.Object,
            NullLogger<QueueController>.Instance);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    #region Start

    [Test]
    public async Task Start_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync("nonexistent")).ReturnsAsync((Project?)null);

        var result = await _controller.Start("nonexistent", new StartQueueRequest { IssueId = "issue1" }, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        Assert.That(((NotFoundObjectResult)result.Result!).Value, Is.EqualTo("Project not found"));
    }

    [Test]
    public async Task Start_ReturnsBadRequest_WhenIssueIdIsEmpty()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync(TestProject.Id)).ReturnsAsync(TestProject);

        var result = await _controller.Start(TestProject.Id, new StartQueueRequest { IssueId = "" }, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(((BadRequestObjectResult)result.Result!).Value, Is.EqualTo("IssueId is required"));
    }

    [Test]
    public async Task Start_ReturnsNotFound_WhenIssueNotFound()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync(TestProject.Id)).ReturnsAsync(TestProject);
        _queueCoordinatorMock
            .Setup(c => c.StartExecution(
                TestProject.Id, "missing-issue", TestProject.LocalPath, TestProject.DefaultBranch,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Issue missing-issue not found."));

        var request = new StartQueueRequest { IssueId = "missing-issue" };

        var result = await _controller.Start(TestProject.Id, request, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Start_ReturnsOk_WithStatusResponse()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync(TestProject.Id)).ReturnsAsync(TestProject);
        _queueCoordinatorMock
            .Setup(c => c.GetStatus(TestProject.Id))
            .Returns(new QueueCoordinatorState
            {
                ProjectId = TestProject.Id,
                Status = QueueCoordinatorStatus.Running,
                ActiveQueues = new List<ITaskQueue>(),
                MaxConcurrency = 5,
                RunningQueueCount = 0,
                RootIssueId = "issue1"
            });

        var request = new StartQueueRequest { IssueId = "issue1" };

        var result = await _controller.Start(TestProject.Id, request, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var response = ((OkObjectResult)result.Result!).Value as QueueStatusResponse;
        Assert.That(response, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(response!.ProjectId, Is.EqualTo(TestProject.Id));
            Assert.That(response.Status, Is.EqualTo("Running"));
            Assert.That(response.RootIssueId, Is.EqualTo("issue1"));
        });
    }

    #endregion

    #region GetStatus

    [Test]
    public async Task GetStatus_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync("nonexistent")).ReturnsAsync((Project?)null);

        var result = await _controller.GetStatus("nonexistent");

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetStatus_ReturnsNotFound_WhenNoActiveExecution()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync(TestProject.Id)).ReturnsAsync(TestProject);
        _queueCoordinatorMock.Setup(c => c.GetStatus(TestProject.Id)).Returns((QueueCoordinatorState?)null);

        var result = await _controller.GetStatus(TestProject.Id);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        Assert.That(((NotFoundObjectResult)result.Result!).Value, Is.EqualTo("No active execution for this project"));
    }

    [Test]
    public async Task GetStatus_ReturnsCorrectQueueBreakdown()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync(TestProject.Id)).ReturnsAsync(TestProject);

        var mockQueue = new Mock<ITaskQueue>();
        mockQueue.Setup(q => q.Id).Returns("queue-1");
        mockQueue.Setup(q => q.State).Returns(TaskQueueState.Running);
        mockQueue.Setup(q => q.CurrentRequest).Returns(new AgentStartRequest
        {
            IssueId = "issue-a",
            ProjectId = TestProject.Id,
            ProjectLocalPath = TestProject.LocalPath,
            ProjectDefaultBranch = TestProject.DefaultBranch,
            Issue = new FleeceIssue
            {
                Id = "issue-a",
                Title = "Test",
                Status = FleeceIssueStatus.Open,
                Type = FleeceIssueType.Task,
                LastUpdate = DateTimeOffset.UtcNow
            },
            BranchName = "task/issue-a"
        });
        mockQueue.Setup(q => q.PendingRequests).Returns(new List<AgentStartRequest>());
        mockQueue.Setup(q => q.History).Returns(new List<TaskQueueHistoryEntry>
        {
            new()
            {
                IssueId = "issue-b",
                Request = new AgentStartRequest
                {
                    IssueId = "issue-b",
                    ProjectId = TestProject.Id,
                    ProjectLocalPath = TestProject.LocalPath,
                    ProjectDefaultBranch = TestProject.DefaultBranch,
                    Issue = new FleeceIssue
                    {
                        Id = "issue-b",
                        Title = "Done",
                        Status = FleeceIssueStatus.Open,
                        Type = FleeceIssueType.Task,
                        LastUpdate = DateTimeOffset.UtcNow
                    },
                    BranchName = "task/issue-b"
                },
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                CompletedAt = DateTimeOffset.UtcNow,
                Success = true
            }
        });

        _queueCoordinatorMock.Setup(c => c.GetStatus(TestProject.Id))
            .Returns(new QueueCoordinatorState
            {
                ProjectId = TestProject.Id,
                Status = QueueCoordinatorStatus.Running,
                ActiveQueues = new List<ITaskQueue> { mockQueue.Object },
                MaxConcurrency = 5,
                RunningQueueCount = 1,
                RootIssueId = "root-1"
            });

        var result = await _controller.GetStatus(TestProject.Id);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var response = ((OkObjectResult)result.Result!).Value as QueueStatusResponse;
        Assert.That(response, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(response!.Queues, Has.Count.EqualTo(1));
            Assert.That(response.Queues[0].Id, Is.EqualTo("queue-1"));
            Assert.That(response.Queues[0].State, Is.EqualTo("Running"));
            Assert.That(response.Queues[0].CurrentIssueId, Is.EqualTo("issue-a"));
            Assert.That(response.Queues[0].History, Has.Count.EqualTo(1));
            Assert.That(response.Queues[0].History[0].IssueId, Is.EqualTo("issue-b"));
            Assert.That(response.Queues[0].History[0].Success, Is.True);
            Assert.That(response.Progress.TotalIssues, Is.EqualTo(2)); // 1 history + 1 current
            Assert.That(response.Progress.Completed, Is.EqualTo(1));
            Assert.That(response.Progress.Remaining, Is.EqualTo(1));
        });
    }

    #endregion

    #region Cancel

    [Test]
    public async Task Cancel_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync("nonexistent")).ReturnsAsync((Project?)null);

        var result = await _controller.Cancel("nonexistent");

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Cancel_ReturnsNotFound_WhenNoActiveExecution()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync(TestProject.Id)).ReturnsAsync(TestProject);
        _queueCoordinatorMock.Setup(c => c.GetStatus(TestProject.Id)).Returns((QueueCoordinatorState?)null);

        var result = await _controller.Cancel(TestProject.Id);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Cancel_CallsCancelAllAndReturnsStatus()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync(TestProject.Id)).ReturnsAsync(TestProject);

        // First call returns Running (pre-cancel check), second returns Cancelled (post-cancel)
        var callCount = 0;
        _queueCoordinatorMock.Setup(c => c.GetStatus(TestProject.Id))
            .Returns(() =>
            {
                callCount++;
                return new QueueCoordinatorState
                {
                    ProjectId = TestProject.Id,
                    Status = callCount <= 1 ? QueueCoordinatorStatus.Running : QueueCoordinatorStatus.Cancelled,
                    ActiveQueues = new List<ITaskQueue>(),
                    MaxConcurrency = 5,
                    RunningQueueCount = 0,
                    RootIssueId = "issue1"
                };
            });

        var result = await _controller.Cancel(TestProject.Id);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _queueCoordinatorMock.Verify(c => c.CancelAll(TestProject.Id), Times.Once);
        var response = ((OkObjectResult)result.Result!).Value as QueueStatusResponse;
        Assert.That(response!.Status, Is.EqualTo("Cancelled"));
    }

    #endregion
}
