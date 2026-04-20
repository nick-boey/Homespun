using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.GitHub;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tier 2 regression — no Information-or-higher log lines are emitted from the
/// graph service namespace during a single <see cref="GraphService.BuildEnhancedTaskGraphAsync"/>
/// call. All hot-path diagnostics must fall under <see cref="LogLevel.Debug"/>.
/// </summary>
[TestFixture]
public class GraphServiceHotPathLoggingTests
{
    [Test]
    public async Task BuildEnhancedTaskGraphAsync_Does_Not_Log_Information_Or_Higher()
    {
        var testPath = Path.Combine(Path.GetTempPath(), $"graph-loghygiene-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testPath);
        Directory.CreateDirectory(Path.Combine(testPath, ".fleece"));

        try
        {
            var dataStore = new MockDataStore();
            var project = new Project
            {
                Name = "test",
                LocalPath = testPath,
                GitHubOwner = "t",
                GitHubRepo = "t",
                DefaultBranch = "main"
            };
            await dataStore.AddProjectAsync(project);

            var fleece = new Mock<IProjectFleeceService>();
            fleece.Setup(f => f.GetTaskGraphWithAdditionalIssuesAsync(
                    testPath, It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TaskGraph { Nodes = [], TotalLanes = 1 });

            var sessionStore = new Mock<IClaudeSessionStore>();
            var sessions = Enumerable.Range(0, 5)
                .Select(i => new ClaudeSession
                {
                    Id = $"s-{i}",
                    ProjectId = project.Id,
                    EntityId = $"issue-{i}",
                    LastActivityAt = DateTime.UtcNow,
                    WorkingDirectory = testPath,
                    Model = "claude-sonnet-4-6",
                    Mode = SessionMode.Build
                })
                .ToList();
            sessionStore.Setup(s => s.GetByProjectId(project.Id)).Returns(sessions);

            var cache = new Mock<IGraphCacheService>();
            cache.Setup(c => c.GetCachedPRData(project.Id)).Returns((CachedPRData?)null);

            var projectService = new Mock<IProjectService>();
            projectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

            var workflow = new Mock<PullRequestWorkflowService>(
                MockBehavior.Loose, dataStore, null!, null!, null!, null!);

            var captured = new List<CapturedLog>();
            var logger = new CapturingLogger<GraphService>(captured, LogLevel.Debug);

            var service = new GraphService(
                projectService.Object,
                new Mock<IGitHubService>().Object,
                fleece.Object,
                sessionStore.Object,
                dataStore,
                workflow.Object,
                cache.Object,
                new Mock<IPRStatusResolver>().Object,
                new Mock<IIssueGraphOpenSpecEnricher>().Object,
                new Mock<Homespun.Features.Git.IGitCloneService>().Object,
                logger);

            var response = await service.BuildEnhancedTaskGraphAsync(project.Id, maxPastPRs: 5);
            Assert.That(response, Is.Not.Null);

            var elevated = captured.Where(l => l.Level >= LogLevel.Information).ToList();
            Assert.That(elevated, Is.Empty,
                "GraphService emitted Information-or-higher log lines on the hot path: "
                + string.Join("; ", elevated.Select(l => $"[{l.Level}] {l.Message}")));
        }
        finally
        {
            if (Directory.Exists(testPath))
            {
                try { Directory.Delete(testPath, recursive: true); } catch { }
            }
        }
    }

    private sealed record CapturedLog(LogLevel Level, string Message);

    private sealed class CapturingLogger<T>(List<CapturedLog> captured, LogLevel minEnabled) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= minEnabled;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            captured.Add(new CapturedLog(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
