using Bunit;
using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Tests.Helpers;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Tests.Components;

[TestFixture]
public class QuickIssueCreateBarTests : BunitTestContext
{
    private MockHttpMessageHandler _mockHandler = null!;

    private static readonly IssueResponse MockIssueResponse = new()
    {
        Id = "abc123",
        Title = "Test Issue",
        Status = IssueStatus.Open,
        Type = IssueType.Feature,
        LastUpdate = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockHandler = new MockHttpMessageHandler();
        _mockHandler.RespondWith("api/issues", MockIssueResponse);

        var httpClient = _mockHandler.CreateClient();
        Services.AddSingleton(new HttpIssueApiService(httpClient));
    }

    private IRenderedComponent<QuickIssueCreateBar> RenderBar(
        Action<ComponentParameterCollectionBuilder<QuickIssueCreateBar>>? configure = null)
    {
        return Render<QuickIssueCreateBar>(p =>
        {
            p.Add(x => x.ProjectId, "proj-1");
            p.Add(x => x.LocalPath, "/tmp/test");
            configure?.Invoke(p);
        });
    }

    [Test]
    public void Renders_InputWithCorrectPlaceholder()
    {
        var cut = RenderBar();

        var input = cut.Find("input.title-input");
        Assert.That(input.GetAttribute("placeholder"), Is.EqualTo("Add an issue..."));
    }

    [Test]
    public void EnterKey_WithNonEmptyTitle_CallsOnIssueCreated()
    {
        var invoked = false;
        var cut = RenderBar(p =>
            p.Add(x => x.OnIssueCreated, () => { invoked = true; }));

        var input = cut.Find("input.title-input");
        input.Input("New issue title");
        input.KeyDown(Key.Enter);

        Assert.That(invoked, Is.True);
    }

    [Test]
    public void ShiftEnter_WithNonEmptyTitle_CallsOnIssueCreatedForEdit()
    {
        string? receivedId = null;
        var cut = RenderBar(p =>
            p.Add(x => x.OnIssueCreatedForEdit, id => { receivedId = id; }));

        var input = cut.Find("input.title-input");
        input.Input("New issue title");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        Assert.That(receivedId, Is.EqualTo("abc123"));
    }

    [Test]
    public void EnterKey_WithEmptyTitle_DoesNotSubmit()
    {
        var invoked = false;
        var cut = RenderBar(p =>
            p.Add(x => x.OnIssueCreated, () => { invoked = true; }));

        var input = cut.Find("input.title-input");
        input.KeyDown(Key.Enter);

        Assert.That(invoked, Is.False);
    }

    [Test]
    public void Renders_AllIssueTypeValues_InTypeSelect()
    {
        var cut = RenderBar();

        var typeSelect = cut.Find("select.type-select");
        var options = typeSelect.QuerySelectorAll("option");

        var expectedTypes = Enum.GetValues<IssueType>();
        Assert.That(options, Has.Count.EqualTo(expectedTypes.Length));

        for (var i = 0; i < expectedTypes.Length; i++)
        {
            Assert.That(options[i].TextContent, Is.EqualTo(expectedTypes[i].ToString()));
        }
    }

    [Test]
    public void Renders_ExpectedStatusOptions()
    {
        var cut = RenderBar();

        var statusSelect = cut.Find("select.status-select");
        var options = statusSelect.QuerySelectorAll("option");

        Assert.That(options, Has.Count.EqualTo(3));
        Assert.That(options[0].TextContent, Is.EqualTo("open"));
        Assert.That(options[1].TextContent, Is.EqualTo("progress"));
        Assert.That(options[2].TextContent, Is.EqualTo("review"));
    }
}
