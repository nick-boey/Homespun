using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

/// <summary>
/// Tier 3 guardrail — when <see cref="IssueGraphOpenSpecEnricher.EnrichAsync"/> is
/// called with a pre-built <see cref="BranchResolutionContext"/>, it MUST NOT call
/// <see cref="IGitCloneService.ListClonesAsync"/>. The hot path builds one context
/// per request in <c>GraphService.BuildEnhancedTaskGraphAsync</c> so per-node calls
/// would defeat the hoisting.
/// </summary>
[TestFixture]
public class IssueGraphOpenSpecEnricherNoFanOutTests
{
    [Test]
    public async Task EnrichAsync_With_Context_Does_Not_Call_ListClonesAsync()
    {
        var projectId = "proj-1";
        var branchResolver = new Mock<IIssueBranchResolverService>(MockBehavior.Strict);
        branchResolver
            .Setup(b => b.ResolveIssueBranchAsync(
                projectId,
                It.IsAny<string>(),
                It.IsAny<BranchResolutionContext>()))
            .ReturnsAsync((string?)null);

        var stateResolver = new Mock<IBranchStateResolverService>();
        var cloneService = new Mock<IGitCloneService>(MockBehavior.Strict);
        var dataStore = new Mock<IDataStore>();
        dataStore.Setup(d => d.GetProject(projectId)).Returns((Project?)null);

        var scanner = new Mock<IChangeScannerService>();

        var enricher = new IssueGraphOpenSpecEnricher(
            branchResolver.Object,
            stateResolver.Object,
            dataStore.Object,
            cloneService.Object,
            scanner.Object,
            NullLogger<IssueGraphOpenSpecEnricher>.Instance);

        var response = new TaskGraphResponse
        {
            Nodes =
            [
                new TaskGraphNodeResponse { Issue = new IssueResponse { Id = "issue-1", Title = "a" } },
                new TaskGraphNodeResponse { Issue = new IssueResponse { Id = "issue-2", Title = "b" } },
                new TaskGraphNodeResponse { Issue = new IssueResponse { Id = "issue-3", Title = "c" } },
            ]
        };

        var context = new BranchResolutionContext(
            Array.Empty<CloneInfo>(),
            new Dictionary<string, string>(StringComparer.Ordinal));

        await enricher.EnrichAsync(projectId, response, context);

        cloneService.Verify(
            c => c.ListClonesAsync(It.IsAny<string>()),
            Times.Never,
            "Enricher fanned out to ListClonesAsync despite receiving a pre-built context");
    }
}
