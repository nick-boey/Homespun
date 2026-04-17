using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SkillDiscoveryServiceTests
{
    private string _projectPath = null!;
    private string _skillsRoot = null!;
    private SkillDiscoveryService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), $"skills-{Guid.NewGuid()}");
        _skillsRoot = Path.Combine(_projectPath, ".claude", "skills");
        Directory.CreateDirectory(_skillsRoot);

        ILogger<SkillDiscoveryService> logger = NullLogger<SkillDiscoveryService>.Instance;
        _service = new SkillDiscoveryService(logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectPath))
        {
            Directory.Delete(_projectPath, recursive: true);
        }
    }

    private void WriteSkill(string skillName, string frontmatter, string body = "Skill body content.")
    {
        var dir = Path.Combine(_skillsRoot, skillName);
        Directory.CreateDirectory(dir);
        var content = $"---\n{frontmatter}\n---\n{body}";
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    [Test]
    public async Task DiscoverSkillsAsync_ReturnsEmpty_WhenSkillsDirectoryMissing()
    {
        Directory.Delete(_skillsRoot, recursive: true);

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.OpenSpec, Is.Empty);
            Assert.That(result.Homespun, Is.Empty);
            Assert.That(result.General, Is.Empty);
        });
    }

    [Test]
    public async Task DiscoverSkillsAsync_CategorisesOpenSpecSkillsByHardCodedName()
    {
        WriteSkill(
            "openspec-apply-change",
            "name: openspec-apply-change\ndescription: Apply a change",
            "Body for openspec-apply-change");

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.That(result.OpenSpec, Has.Count.EqualTo(1));
        var skill = result.OpenSpec[0];
        Assert.Multiple(() =>
        {
            Assert.That(skill.Name, Is.EqualTo("openspec-apply-change"));
            Assert.That(skill.Description, Is.EqualTo("Apply a change"));
            Assert.That(skill.Category, Is.EqualTo(SkillCategory.OpenSpec));
            Assert.That(skill.SkillBody, Is.EqualTo("Body for openspec-apply-change"));
        });
    }

    [Test]
    public async Task DiscoverSkillsAsync_FindsAllEightOpenSpecSkills()
    {
        var names = new[]
        {
            "openspec-explore",
            "openspec-new-change",
            "openspec-propose",
            "openspec-continue-change",
            "openspec-apply-change",
            "openspec-verify-change",
            "openspec-sync-specs",
            "openspec-archive-change",
        };
        foreach (var n in names)
        {
            WriteSkill(n, $"name: {n}\ndescription: {n} desc");
        }

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.That(result.OpenSpec.Select(s => s.Name), Is.EquivalentTo(names));
        Assert.That(result.Homespun, Is.Empty);
        Assert.That(result.General, Is.Empty);
    }

    [Test]
    public async Task DiscoverSkillsAsync_CategorisesHomespunSkillByFrontmatterFlag()
    {
        WriteSkill(
            "fix-bug",
            """
            name: fix-bug
            description: Fix a bug
            homespun: true
            homespun-mode: build
            homespun-args:
              - name: issue-id
                kind: issue
            """,
            "Fix the bug described.");

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.That(result.Homespun, Has.Count.EqualTo(1));
        var skill = result.Homespun[0];
        Assert.Multiple(() =>
        {
            Assert.That(skill.Category, Is.EqualTo(SkillCategory.Homespun));
            Assert.That(skill.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(skill.Args, Has.Count.EqualTo(1));
            Assert.That(skill.Args[0].Name, Is.EqualTo("issue-id"));
            Assert.That(skill.Args[0].Kind, Is.EqualTo(SkillArgKind.Issue));
            Assert.That(skill.SkillBody, Is.EqualTo("Fix the bug described."));
        });
    }

    [Test]
    public async Task DiscoverSkillsAsync_ParsesHomespunArgKinds()
    {
        WriteSkill(
            "multi-arg",
            """
            name: multi-arg
            description: Multi-arg skill
            homespun: true
            homespun-mode: plan
            homespun-args:
              - name: issue-id
                kind: issue
              - name: change-name
                kind: change
              - name: phases
                kind: phase-list
              - name: note
                kind: free-text
            """);

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        var skill = result.Homespun.Single();
        Assert.Multiple(() =>
        {
            Assert.That(skill.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(skill.Args.Select(a => a.Kind), Is.EqualTo(new[]
            {
                SkillArgKind.Issue,
                SkillArgKind.Change,
                SkillArgKind.PhaseList,
                SkillArgKind.FreeText,
            }));
        });
    }

    [Test]
    public async Task DiscoverSkillsAsync_UnknownArgKindDefaultsToFreeText()
    {
        WriteSkill(
            "unknown-kind",
            """
            name: unknown-kind
            description: desc
            homespun: true
            homespun-args:
              - name: thing
                kind: gibberish
            """);

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.That(result.Homespun.Single().Args[0].Kind, Is.EqualTo(SkillArgKind.FreeText));
    }

    [Test]
    public async Task DiscoverSkillsAsync_NonOpenSpecNonHomespunSkillIsGeneral()
    {
        WriteSkill(
            "some-helper",
            "name: some-helper\ndescription: A helper skill");

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.That(result.General, Has.Count.EqualTo(1));
        Assert.That(result.General[0].Category, Is.EqualTo(SkillCategory.General));
    }

    [Test]
    public async Task DiscoverSkillsAsync_SkipsSkillDirectoryWithoutSkillMd()
    {
        Directory.CreateDirectory(Path.Combine(_skillsRoot, "no-skill-md"));

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.OpenSpec, Is.Empty);
            Assert.That(result.Homespun, Is.Empty);
            Assert.That(result.General, Is.Empty);
        });
    }

    [Test]
    public async Task DiscoverSkillsAsync_SkipsSkillWithNoFrontmatter()
    {
        var dir = Path.Combine(_skillsRoot, "no-frontmatter");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), "Just body text, no frontmatter.");

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.OpenSpec, Is.Empty);
            Assert.That(result.Homespun, Is.Empty);
            Assert.That(result.General, Is.Empty);
        });
    }

    [Test]
    public async Task DiscoverSkillsAsync_SkipsSkillWithMalformedFrontmatter()
    {
        var dir = Path.Combine(_skillsRoot, "bad-yaml");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "SKILL.md"),
            "---\nname: bad\n  : : : broken\n\t- indent mix\n---\nbody");

        // Should not throw — malformed entries are skipped.
        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.OpenSpec, Is.Empty);
            Assert.That(result.Homespun, Is.Empty);
            Assert.That(result.General, Is.Empty);
        });
    }

    [Test]
    public async Task DiscoverSkillsAsync_SortsEachCategoryAlphabetically()
    {
        WriteSkill("z-other", "name: z-other\ndescription: z");
        WriteSkill("a-other", "name: a-other\ndescription: a");
        WriteSkill(
            "openspec-propose",
            "name: openspec-propose\ndescription: propose");
        WriteSkill(
            "openspec-apply-change",
            "name: openspec-apply-change\ndescription: apply");

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.General.Select(s => s.Name), Is.EqualTo(new[] { "a-other", "z-other" }));
            Assert.That(
                result.OpenSpec.Select(s => s.Name),
                Is.EqualTo(new[] { "openspec-apply-change", "openspec-propose" }));
        });
    }

    [Test]
    public async Task DiscoverSkillsAsync_HomespunModeAcceptsPlanAndBuild()
    {
        WriteSkill(
            "plan-skill",
            "name: plan-skill\ndescription: d\nhomespun: true\nhomespun-mode: plan");
        WriteSkill(
            "build-skill",
            "name: build-skill\ndescription: d\nhomespun: true\nhomespun-mode: build");
        WriteSkill(
            "no-mode-skill",
            "name: no-mode-skill\ndescription: d\nhomespun: true");

        var result = await _service.DiscoverSkillsAsync(_projectPath);

        var byName = result.Homespun.ToDictionary(s => s.Name);
        Assert.Multiple(() =>
        {
            Assert.That(byName["plan-skill"].Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(byName["build-skill"].Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(byName["no-mode-skill"].Mode, Is.Null);
        });
    }

    [Test]
    public async Task GetSkillAsync_ReturnsNull_WhenSkillMissing()
    {
        var skill = await _service.GetSkillAsync(_projectPath, "does-not-exist");

        Assert.That(skill, Is.Null);
    }

    [Test]
    public async Task GetSkillAsync_ReturnsSkillWithBody()
    {
        WriteSkill(
            "my-skill",
            "name: my-skill\ndescription: desc\nhomespun: true",
            "The body.");

        var skill = await _service.GetSkillAsync(_projectPath, "my-skill");

        Assert.That(skill, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(skill!.Name, Is.EqualTo("my-skill"));
            Assert.That(skill.SkillBody, Is.EqualTo("The body."));
            Assert.That(skill.Category, Is.EqualTo(SkillCategory.Homespun));
        });
    }

    [Test]
    public async Task GetSkillAsync_ReturnsNull_WhenNameIsEmpty()
    {
        var skill = await _service.GetSkillAsync(_projectPath, "");

        Assert.That(skill, Is.Null);
    }
}
