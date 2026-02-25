using Bunit;
using Homespun.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Homespun.Tests.Components;

[TestFixture]
public class SearchBarTests : BunitTestContext
{
    [Test]
    public void SearchBar_Visible_WhenIsVisibleTrue()
    {
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "");
        });

        Assert.That(cut.FindAll(".search-bar").Count, Is.EqualTo(1));
    }

    [Test]
    public void SearchBar_NotRendered_WhenIsVisibleFalse()
    {
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, false);
            p.Add(x => x.SearchTerm, "");
        });

        Assert.That(cut.FindAll(".search-bar").Count, Is.EqualTo(0));
    }

    [Test]
    public void SearchBar_DisplaysSearchTerm()
    {
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "test search");
        });

        var input = cut.Find("input");
        Assert.That(input.GetAttribute("value"), Is.EqualTo("test search"));
    }

    [Test]
    public async Task Input_FiresOnSearchTermChanged()
    {
        string? receivedTerm = null;
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "");
            p.Add(x => x.OnSearchTermChanged, EventCallback.Factory.Create<string>(this, (term) => receivedTerm = term));
        });

        var input = cut.Find("input");
        await input.InputAsync(new ChangeEventArgs { Value = "hello" });

        Assert.That(receivedTerm, Is.EqualTo("hello"));
    }

    [Test]
    public async Task Enter_FiresOnEnter()
    {
        var enterFired = false;
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "test");
            p.Add(x => x.OnEnter, EventCallback.Factory.Create(this, () => enterFired = true));
        });

        var input = cut.Find("input");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.That(enterFired, Is.True);
    }

    [Test]
    public async Task Escape_FiresOnEscape()
    {
        var escapeFired = false;
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "test");
            p.Add(x => x.OnEscape, EventCallback.Factory.Create(this, () => escapeFired = true));
        });

        var input = cut.Find("input");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.That(escapeFired, Is.True);
    }

    [Test]
    public void SearchBar_HasSlashIcon()
    {
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "");
        });

        var icon = cut.Find(".search-icon");
        Assert.That(icon.TextContent.Trim(), Is.EqualTo("/"));
    }

    [Test]
    public void SearchBar_HasPlaceholder()
    {
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "");
        });

        var input = cut.Find("input");
        Assert.That(input.GetAttribute("placeholder"), Is.EqualTo("Search issues..."));
    }

    [Test]
    public async Task OtherKeys_DoNotTriggerCallbacks()
    {
        var enterFired = false;
        var escapeFired = false;
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "test");
            p.Add(x => x.OnEnter, EventCallback.Factory.Create(this, () => enterFired = true));
            p.Add(x => x.OnEscape, EventCallback.Factory.Create(this, () => escapeFired = true));
        });

        var input = cut.Find("input");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        Assert.That(enterFired, Is.False);
        Assert.That(escapeFired, Is.False);
    }

    [Test]
    public void SearchBar_ShowsMatchCount_WhenMatchesExist()
    {
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "test");
            p.Add(x => x.MatchCount, 5);
        });

        var count = cut.Find(".match-count");
        Assert.That(count.TextContent, Does.Contain("5"));
    }

    [Test]
    public void SearchBar_HidesMatchCount_WhenNoSearchTerm()
    {
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "");
            p.Add(x => x.MatchCount, 5);
        });

        Assert.That(cut.FindAll(".match-count").Count, Is.EqualTo(0));
    }

    [Test]
    public void SearchBar_ShowsNoMatchesMessage_WhenSearchTermButNoMatches()
    {
        var cut = Render<SearchBar>(p =>
        {
            p.Add(x => x.IsVisible, true);
            p.Add(x => x.SearchTerm, "xyz");
            p.Add(x => x.MatchCount, 0);
        });

        var count = cut.Find(".match-count.no-matches");
        Assert.That(count.TextContent, Does.Contain("No matches"));
    }
}
