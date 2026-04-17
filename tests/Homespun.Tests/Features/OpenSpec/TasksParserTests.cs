using Homespun.Features.OpenSpec.Services;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class TasksParserTests
{
    [Test]
    public void Parse_Empty_ReturnsEmpty()
    {
        var result = TasksParser.Parse("");

        Assert.That(result.TasksTotal, Is.EqualTo(0));
        Assert.That(result.TasksDone, Is.EqualTo(0));
        Assert.That(result.Phases, Is.Empty);
        Assert.That(result.NextIncomplete, Is.Null);
    }

    [Test]
    public void Parse_GroupsTasksByPhaseHeading()
    {
        var md = """
            ## 1. Backend

            - [x] 1.1 Done task
            - [ ] 1.2 Pending task

            ## 2. Frontend

            - [ ] 2.1 Another pending
            """;

        var result = TasksParser.Parse(md);

        Assert.That(result.TasksTotal, Is.EqualTo(3));
        Assert.That(result.TasksDone, Is.EqualTo(1));
        Assert.That(result.Phases, Has.Count.EqualTo(2));
        Assert.That(result.Phases[0].Name, Is.EqualTo("1. Backend"));
        Assert.That(result.Phases[0].Done, Is.EqualTo(1));
        Assert.That(result.Phases[0].Total, Is.EqualTo(2));
        Assert.That(result.Phases[1].Name, Is.EqualTo("2. Frontend"));
        Assert.That(result.Phases[1].Total, Is.EqualTo(1));
    }

    [Test]
    public void Parse_CapturesFirstIncompleteAsNext()
    {
        var md = """
            ## Phase

            - [x] Done one
            - [ ] First pending
            - [ ] Second pending
            """;

        var result = TasksParser.Parse(md);

        Assert.That(result.NextIncomplete, Is.EqualTo("First pending"));
    }

    [Test]
    public void Parse_AllDone_NextIncompleteIsNull()
    {
        var md = """
            ## Phase

            - [x] Done one
            - [x] Done two
            """;

        var result = TasksParser.Parse(md);

        Assert.That(result.NextIncomplete, Is.Null);
        Assert.That(result.TasksDone, Is.EqualTo(2));
    }

    [Test]
    public void Parse_PhaseTasks_IncludeCheckboxState()
    {
        var md = """
            ## Phase

            - [x] Task one
            - [ ] Task two
            """;

        var result = TasksParser.Parse(md);

        Assert.That(result.Phases[0].Tasks, Has.Count.EqualTo(2));
        Assert.That(result.Phases[0].Tasks[0].Description, Is.EqualTo("Task one"));
        Assert.That(result.Phases[0].Tasks[0].Done, Is.True);
        Assert.That(result.Phases[0].Tasks[1].Done, Is.False);
    }

    [Test]
    public void Parse_TasksWithoutHeading_BucketedAsUnnamed()
    {
        var md = """
            - [x] Orphan done
            - [ ] Orphan pending
            """;

        var result = TasksParser.Parse(md);

        Assert.That(result.Phases, Has.Count.EqualTo(1));
        Assert.That(result.Phases[0].Name, Is.EqualTo("(unnamed)"));
        Assert.That(result.Phases[0].Total, Is.EqualTo(2));
    }

    [Test]
    public void Parse_IgnoresNonTaskLines()
    {
        var md = """
            ## Phase

            Some prose.

            - [x] Real task

            More prose. Not a - [ ] task because no checkbox at line start? Actually our regex is anchored.
            """;

        var result = TasksParser.Parse(md);

        Assert.That(result.TasksTotal, Is.EqualTo(1));
    }
}
