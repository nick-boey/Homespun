using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SessionCachePurgeHostedServiceTests
{
    private string _baseDir = null!;

    [SetUp]
    public void SetUp()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "homespun-purge-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, recursive: true);
        }
    }

    [Test]
    public async Task StartAsync_DeletesLegacyJsonlFiles()
    {
        var projectDir = Path.Combine(_baseDir, "project-1");
        Directory.CreateDirectory(projectDir);
        var legacy = Path.Combine(projectDir, "session-abc.jsonl");
        await File.WriteAllTextAsync(legacy, "{\"legacy\":true}\n");

        var service = CreateService();

        await service.StartAsync(CancellationToken.None);

        Assert.That(File.Exists(legacy), Is.False, "legacy *.jsonl should be deleted");
    }

    [Test]
    public async Task StartAsync_PreservesA2AEventLogs()
    {
        var projectDir = Path.Combine(_baseDir, "project-1");
        Directory.CreateDirectory(projectDir);
        var eventsLog = Path.Combine(projectDir, "session-abc.events.jsonl");
        await File.WriteAllTextAsync(eventsLog, "{\"seq\":1}\n");

        var service = CreateService();

        await service.StartAsync(CancellationToken.None);

        Assert.That(File.Exists(eventsLog), Is.True, "*.events.jsonl files must be preserved — they are the new A2A event log");
    }

    [Test]
    public async Task StartAsync_DeletesLegacyMetaAndIndex()
    {
        var projectDir = Path.Combine(_baseDir, "project-1");
        Directory.CreateDirectory(projectDir);
        var meta = Path.Combine(projectDir, "session-abc.meta.json");
        var index = Path.Combine(_baseDir, "index.json");
        await File.WriteAllTextAsync(meta, "{}");
        await File.WriteAllTextAsync(index, "{}");

        var service = CreateService();

        await service.StartAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(meta), Is.False);
            Assert.That(File.Exists(index), Is.False);
        });
    }

    [Test]
    public async Task StartAsync_WhenSkipEnvVarSet_LeavesFilesUntouched()
    {
        var projectDir = Path.Combine(_baseDir, "project-1");
        Directory.CreateDirectory(projectDir);
        var legacy = Path.Combine(projectDir, "session-abc.jsonl");
        await File.WriteAllTextAsync(legacy, "{\"legacy\":true}\n");

        var service = CreateService(skip: true);

        await service.StartAsync(CancellationToken.None);

        Assert.That(File.Exists(legacy), Is.True, "legacy files must be preserved when HOMESPUN_SKIP_CACHE_PURGE=true");
    }

    [Test]
    public async Task StartAsync_WhenBaseDirMissing_DoesNotThrow()
    {
        var missingDir = Path.Combine(_baseDir, "does-not-exist");
        var service = CreateService(overrideBaseDir: missingDir);

        Assert.DoesNotThrowAsync(async () => await service.StartAsync(CancellationToken.None));
    }

    private SessionCachePurgeHostedService CreateService(bool skip = false, string? overrideBaseDir = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HOMESPUN_SKIP_CACHE_PURGE"] = skip ? "true" : null,
            })
            .Build();
        return new SessionCachePurgeHostedService(
            overrideBaseDir ?? _baseDir,
            config,
            NullLogger<SessionCachePurgeHostedService>.Instance);
    }
}
