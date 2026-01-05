using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Roadmap;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class AgentWorkflowServiceTests
{
    private Mock<IOpenCodeServerManager> _mockServerManager = null!;
    private Mock<IOpenCodeClient> _mockClient = null!;
    private Mock<IOpenCodeConfigGenerator> _mockConfigGenerator = null!;
    private Mock<PullRequestDataService> _mockPullRequestService = null!;
    private Mock<IRoadmapService> _mockRoadmapService = null!;
    private Mock<ILogger<AgentWorkflowService>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockServerManager = new Mock<IOpenCodeServerManager>();
        _mockClient = new Mock<IOpenCodeClient>();
        _mockConfigGenerator = new Mock<IOpenCodeConfigGenerator>();
        _mockPullRequestService = new Mock<PullRequestDataService>(MockBehavior.Loose, null!);
        _mockRoadmapService = new Mock<IRoadmapService>();
        _mockLogger = new Mock<ILogger<AgentWorkflowService>>();
    }

    #region BuildInitialPrompt Tests

    [Test]
    public void BuildInitialPrompt_IncludesBranchName()
    {
        var change = CreateTestChange();
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        Assert.That(prompt, Does.Contain("core/feature/add-auth"));
        Assert.That(prompt, Does.Contain("Branch:").Or.Contain("branch"));
    }

    [Test]
    public void BuildInitialPrompt_IncludesTitle()
    {
        var change = CreateTestChange();
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        Assert.That(prompt, Does.Contain("Add Authentication"));
    }

    [Test]
    public void BuildInitialPrompt_IncludesDescription_WhenProvided()
    {
        var change = CreateTestChange();
        change.Description = "Implement OAuth2 authentication flow";
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        Assert.That(prompt, Does.Contain("OAuth2 authentication flow"));
    }

    [Test]
    public void BuildInitialPrompt_ExcludesDescription_WhenNull()
    {
        var change = CreateTestChange();
        change.Description = null;
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        Assert.That(prompt, Does.Not.Contain("Description:"));
    }

    [Test]
    public void BuildInitialPrompt_IncludesInstructions_WhenProvided()
    {
        var change = CreateTestChange();
        change.Instructions = "Use JWT tokens for session management";
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        Assert.That(prompt, Does.Contain("JWT tokens for session management"));
    }

    [Test]
    public void BuildInitialPrompt_ExcludesInstructions_WhenNull()
    {
        var change = CreateTestChange();
        change.Instructions = null;
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        Assert.That(prompt, Does.Not.Contain("Instructions:"));
    }

    [Test]
    public void BuildInitialPrompt_IncludesPrCreationInstructions()
    {
        var change = CreateTestChange();
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        Assert.That(prompt, Does.Contain("gh pr create"));
    }

    [Test]
    public void BuildInitialPrompt_InstructsToCommitToBranch()
    {
        var change = CreateTestChange();
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        Assert.That(prompt, Does.Contain("commit").IgnoreCase);
        Assert.That(prompt, Does.Contain("core/feature/add-auth"));
    }

    [Test]
    public void BuildInitialPrompt_IncludesWorkflowInstructions()
    {
        var change = CreateTestChange();
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        // Should instruct to create PR when done
        Assert.That(prompt, Does.Contain("create").IgnoreCase.And.Contain("pull request").IgnoreCase
            .Or.Contain("create").IgnoreCase.And.Contain("PR").IgnoreCase);
    }

    [Test]
    public void BuildInitialPrompt_SpecifiesBaseBranch_WhenParentsExist()
    {
        var change = CreateTestChange();
        change.Parents = ["core/feature/base-feature"];
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        // Should mention the parent branch as the base for the PR
        Assert.That(prompt, Does.Contain("core/feature/base-feature"));
    }

    [Test]
    public void BuildInitialPrompt_UsesMainAsBase_WhenNoParents()
    {
        var change = CreateTestChange();
        change.Parents = [];
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        // Should mention main/master as the base when no parents
        Assert.That(prompt, Does.Contain("main").Or.Contain("master"));
    }

    [Test]
    public void BuildInitialPrompt_UsesFirstParent_WhenMultipleParentsExist()
    {
        var change = CreateTestChange();
        change.Parents = ["core/feature/first-parent", "core/feature/second-parent"];
        
        var prompt = AgentWorkflowService.BuildInitialPrompt(change);
        
        // Should use the first parent as the base branch
        Assert.That(prompt, Does.Contain("core/feature/first-parent"));
    }

    #endregion

    #region Helper Methods

    private static FutureChange CreateTestChange()
    {
        return new FutureChange
        {
            Id = "core/feature/add-auth",
            ShortTitle = "add-auth",
            Group = "core",
            Type = ChangeType.Feature,
            Title = "Add Authentication",
            Description = null,
            Instructions = null,
            Parents = []
        };
    }

    #endregion
}
