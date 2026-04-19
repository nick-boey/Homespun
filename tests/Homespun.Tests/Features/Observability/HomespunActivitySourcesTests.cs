using Homespun.Features.Observability;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class HomespunActivitySourcesTests
{
    [Test]
    public void AllSourceNames_ContainsExpectedSources()
    {
        Assert.That(HomespunActivitySources.AllSourceNames, Has.Length.EqualTo(4));
        Assert.That(HomespunActivitySources.AllSourceNames, Does.Contain("Homespun.AgentOrchestration"));
        Assert.That(HomespunActivitySources.AllSourceNames, Does.Contain("Homespun.GitClone"));
        Assert.That(HomespunActivitySources.AllSourceNames, Does.Contain("Homespun.FleeceSync"));
        Assert.That(HomespunActivitySources.AllSourceNames, Does.Contain("Homespun.Signalr"));
    }

    [Test]
    public void ActivitySources_AreInitialized()
    {
        Assert.That(HomespunActivitySources.AgentOrchestrationSource, Is.Not.Null);
        Assert.That(HomespunActivitySources.AgentOrchestrationSource.Name, Is.EqualTo("Homespun.AgentOrchestration"));

        Assert.That(HomespunActivitySources.GitCloneSource, Is.Not.Null);
        Assert.That(HomespunActivitySources.GitCloneSource.Name, Is.EqualTo("Homespun.GitClone"));

        Assert.That(HomespunActivitySources.FleeceSyncSource, Is.Not.Null);
        Assert.That(HomespunActivitySources.FleeceSyncSource.Name, Is.EqualTo("Homespun.FleeceSync"));
    }
}
