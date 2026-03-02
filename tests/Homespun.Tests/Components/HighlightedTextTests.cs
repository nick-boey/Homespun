using Bunit;
using Homespun.Client.Components;
using Homespun.Client.Features.Shared.Components;

namespace Homespun.Tests.Components;

[TestFixture]
public class HighlightedTextTests : BunitTestContext
{
    [Test]
    public void Renders_PlainText_WhenNoMatch()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "This is some text");
            p.Add(x => x.SearchTerm, "xyz");
        });

        Assert.That(cut.Markup.Contains("<mark"), Is.False);
        Assert.That(cut.Markup.Contains("This is some text"), Is.True);
    }

    [Test]
    public void Renders_PlainText_WhenSearchTermEmpty()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "This is some text");
            p.Add(x => x.SearchTerm, "");
        });

        Assert.That(cut.Markup.Contains("<mark"), Is.False);
        Assert.That(cut.Markup.Contains("This is some text"), Is.True);
    }

    [Test]
    public void Renders_PlainText_WhenSearchTermNull()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "This is some text");
            p.Add(x => x.SearchTerm, null!);
        });

        Assert.That(cut.Markup.Contains("<mark"), Is.False);
        Assert.That(cut.Markup.Contains("This is some text"), Is.True);
    }

    [Test]
    public void Renders_HighlightMark_WhenMatch()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "Authentication bug");
            p.Add(x => x.SearchTerm, "auth");
        });

        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(1));
        Assert.That(marks[0].TextContent, Is.EqualTo("Auth"));
        Assert.That(cut.Markup.Contains("entication bug"), Is.True);
    }

    [Test]
    public void Highlights_CaseInsensitive()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "Authentication bug");
            p.Add(x => x.SearchTerm, "AUTH");
        });

        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(1));
        Assert.That(marks[0].TextContent, Is.EqualTo("Auth"));
    }

    [Test]
    public void Highlights_MultipleOccurrences()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "Test the test cases for testing");
            p.Add(x => x.SearchTerm, "test");
        });

        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(3));
    }

    [Test]
    public void Highlights_EntireText_WhenSearchMatchesAll()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "auth");
            p.Add(x => x.SearchTerm, "auth");
        });

        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(1));
        Assert.That(marks[0].TextContent, Is.EqualTo("auth"));
    }

    [Test]
    public void Highlights_AtStart()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "Auth at the start");
            p.Add(x => x.SearchTerm, "Auth");
        });

        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(1));
        Assert.That(marks[0].TextContent, Is.EqualTo("Auth"));
    }

    [Test]
    public void Highlights_AtEnd()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "Ends with auth");
            p.Add(x => x.SearchTerm, "auth");
        });

        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(1));
        Assert.That(marks[0].TextContent, Is.EqualTo("auth"));
    }

    [Test]
    public void HasCorrectCssClass_OnHighlightMark()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "Authentication bug");
            p.Add(x => x.SearchTerm, "auth");
        });

        var marks = cut.FindAll("mark.search-highlight");
        Assert.That(marks.Count, Is.EqualTo(1));
    }

    [Test]
    public void Renders_EmptyText_WhenTextEmpty()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "");
            p.Add(x => x.SearchTerm, "auth");
        });

        Assert.That(cut.Markup.Contains("<mark"), Is.False);
        Assert.That(cut.Markup.Trim(), Is.Empty);
    }

    [Test]
    public void Renders_EmptyText_WhenTextNull()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, null!);
            p.Add(x => x.SearchTerm, "auth");
        });

        Assert.That(cut.Markup.Contains("<mark"), Is.False);
    }

    [Test]
    public void PreservesOriginalCasing_InHighlightedText()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "AUTHENTICATION bug");
            p.Add(x => x.SearchTerm, "auth");
        });

        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(1));
        Assert.That(marks[0].TextContent, Is.EqualTo("AUTH"));
    }

    [Test]
    public void Handles_SpecialRegexCharacters()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "Test [special] chars");
            p.Add(x => x.SearchTerm, "[special]");
        });

        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(1));
        Assert.That(marks[0].TextContent, Is.EqualTo("[special]"));
    }

    [Test]
    public void Handles_OverlappingPotentialMatches()
    {
        var cut = Render<HighlightedText>(p =>
        {
            p.Add(x => x.Text, "aaaa");
            p.Add(x => x.SearchTerm, "aa");
        });

        // Non-overlapping matches: positions 0-1 and 2-3
        var marks = cut.FindAll("mark");
        Assert.That(marks.Count, Is.EqualTo(2));
    }
}
