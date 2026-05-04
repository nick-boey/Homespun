using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for <see cref="PlanArtefactStore"/> covering FI-6 — server-owned
/// lifecycle of the plan-file artefacts written during a session.
/// </summary>
[TestFixture]
public class PlanArtefactStoreTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "plan-artefact-store-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private PlanArtefactStore CreateStore() =>
        new(NullLogger<PlanArtefactStore>.Instance);

    [Test]
    public async Task RemoveForSessionAsync_deletes_registered_files_on_disk()
    {
        var store = CreateStore();
        var file1 = Path.Combine(_tempDir, "plan-1.md");
        var file2 = Path.Combine(_tempDir, "plan-2.md");
        await File.WriteAllTextAsync(file1, "# plan 1");
        await File.WriteAllTextAsync(file2, "# plan 2");

        store.Register("session-A", file1);
        store.Register("session-A", file2);

        var deleted = await store.RemoveForSessionAsync("session-A");

        Assert.That(deleted, Is.EqualTo(2));
        Assert.That(File.Exists(file1), Is.False);
        Assert.That(File.Exists(file2), Is.False);
    }

    [Test]
    public async Task RemoveForSessionAsync_only_removes_files_for_the_named_session()
    {
        var store = CreateStore();
        var fileA = Path.Combine(_tempDir, "plan-A.md");
        var fileB = Path.Combine(_tempDir, "plan-B.md");
        await File.WriteAllTextAsync(fileA, "A");
        await File.WriteAllTextAsync(fileB, "B");

        store.Register("session-A", fileA);
        store.Register("session-B", fileB);

        await store.RemoveForSessionAsync("session-A");

        Assert.That(File.Exists(fileA), Is.False);
        Assert.That(File.Exists(fileB), Is.True);
    }

    [Test]
    public async Task RemoveForSessionAsync_returns_zero_for_unknown_session()
    {
        var store = CreateStore();
        var deleted = await store.RemoveForSessionAsync("unknown");
        Assert.That(deleted, Is.EqualTo(0));
    }

    [Test]
    public async Task RemoveForSessionAsync_treats_missing_file_as_already_removed()
    {
        var store = CreateStore();
        var ghostPath = Path.Combine(_tempDir, "never-existed.md");

        store.Register("session-A", ghostPath);

        var deleted = await store.RemoveForSessionAsync("session-A");

        Assert.That(deleted, Is.EqualTo(0));
        Assert.That(store.IsRegistered(ghostPath), Is.False);
    }

    [Test]
    public void Register_skips_agent_placeholder_paths()
    {
        var store = CreateStore();
        store.Register("session-A", "agent:~/.claude/plans/plan.md");
        Assert.That(store.IsRegistered("agent:~/.claude/plans/plan.md"), Is.False);
    }

    [Test]
    public async Task Register_is_idempotent_for_the_same_path()
    {
        var store = CreateStore();
        var file = Path.Combine(_tempDir, "plan.md");
        await File.WriteAllTextAsync(file, "x");

        store.Register("session-A", file);
        store.Register("session-A", file);
        store.Register("session-A", file);

        var deleted = await store.RemoveForSessionAsync("session-A");

        Assert.That(deleted, Is.EqualTo(1));
    }
}
