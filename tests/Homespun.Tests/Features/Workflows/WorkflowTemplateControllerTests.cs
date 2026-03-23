using Homespun.Features.Projects;
using Homespun.Features.Workflows.Controllers;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Workflows;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Workflows;

/// <summary>
/// Unit tests for WorkflowTemplateController.
/// </summary>
[TestFixture]
public class WorkflowTemplateControllerTests
{
    private Mock<IWorkflowTemplateService> _templateServiceMock = null!;
    private Mock<IWorkflowStorageService> _storageServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private WorkflowTemplateController _controller = null!;

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
        _templateServiceMock = new Mock<IWorkflowTemplateService>();
        _storageServiceMock = new Mock<IWorkflowStorageService>();
        _projectServiceMock = new Mock<IProjectService>();

        _controller = new WorkflowTemplateController(
            _templateServiceMock.Object,
            _storageServiceMock.Object,
            _projectServiceMock.Object,
            NullLogger<WorkflowTemplateController>.Instance);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    #region ListTemplates Tests

    [Test]
    public void ListTemplates_ReturnsOk_WithTemplates()
    {
        // Arrange
        var templates = new List<WorkflowTemplateSummary>
        {
            new() { Id = "template-1", Title = "Template 1", Description = "Desc", StepCount = 3 },
            new() { Id = "template-2", Title = "Template 2", Description = "Desc", StepCount = 5 }
        };

        _templateServiceMock
            .Setup(x => x.GetTemplates())
            .Returns(templates);

        // Act
        var result = _controller.ListTemplates();

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var returnedTemplates = (IReadOnlyList<WorkflowTemplateSummary>)okResult.Value!;
        Assert.That(returnedTemplates, Has.Count.EqualTo(2));
    }

    #endregion

    #region CreateFromTemplate Tests

    [Test]
    public async Task CreateFromTemplate_ReturnsCreated_WithWorkflow()
    {
        // Arrange
        var workflow = new WorkflowDefinition
        {
            Id = "wf-new",
            ProjectId = TestProject.Id,
            Title = "Test Template"
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _templateServiceMock
            .Setup(x => x.CreateWorkflowFromTemplate("template-1", TestProject.Id))
            .Returns(workflow);
        _storageServiceMock
            .Setup(x => x.CreateWorkflowAsync(
                TestProject.LocalPath,
                It.IsAny<CreateWorkflowParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        // Act
        var result = await _controller.CreateFromTemplate("template-1", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = (CreatedAtActionResult)result.Result!;
        var returnedWorkflow = (WorkflowDefinition)createdResult.Value!;
        Assert.That(returnedWorkflow.Id, Is.EqualTo("wf-new"));
    }

    [Test]
    public async Task CreateFromTemplate_ProjectNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _controller.CreateFromTemplate("template-1", "nonexistent");

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task CreateFromTemplate_TemplateNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _templateServiceMock
            .Setup(x => x.CreateWorkflowFromTemplate("nonexistent", TestProject.Id))
            .Returns((WorkflowDefinition?)null);

        // Act
        var result = await _controller.CreateFromTemplate("nonexistent", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion
}
