using Bunit;
using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Requests;
using Homespun.Tests.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Tests.Components;

[TestFixture]
public class InlineIssueCreateInputTests : BunitTestContext
{
    private MockHttpMessageHandler _mockHandler = null!;

    private static readonly IssueResponse MockCreatedIssue = new()
    {
        Id = "new-id",
        Title = "Created Issue",
        Status = IssueStatus.Open,
        Type = IssueType.Task,
        LastUpdate = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockHandler = new MockHttpMessageHandler();
        _mockHandler.RespondWith("api/issues", MockCreatedIssue);

        var httpClient = _mockHandler.CreateClient();
        Services.AddSingleton(new HttpIssueApiService(httpClient));

        // Setup bUnit JS interop to handle the focusWithCursor call
        Context!.JSInterop.SetupVoid("homespunInterop.focusWithCursor", _ => true);
    }

    private Action<ComponentParameterCollectionBuilder<InlineIssueCreateInput>> DefaultParams(
        Action<ComponentParameterCollectionBuilder<InlineIssueCreateInput>>? configure = null)
    {
        return p =>
        {
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.InsertIndex, 0);
            p.Add(x => x.InsertAbove, false);
            p.Add(x => x.AdjacentIssueId, "adjacent-123");
            p.Add(x => x.CanMoveUp, true);
            p.Add(x => x.CanMoveDown, true);
            configure?.Invoke(p);
        };
    }

    [Test]
    public void FocusOnFirstRender_CallsFocusWithCursor()
    {
        var cut = Render<InlineIssueCreateInput>(DefaultParams());

        var invocations = Context!.JSInterop.Invocations;
        var focusCall = invocations.FirstOrDefault(i => i.Identifier == "homespunInterop.focusWithCursor");
        Assert.That(focusCall, Is.Not.Null, "Should call homespunInterop.focusWithCursor on first render");
    }

    [Test]
    public void FocusOnFirstRender_PassesStartCursorPosition()
    {
        var cut = Render<InlineIssueCreateInput>(DefaultParams());

        var invocations = Context!.JSInterop.Invocations;
        var focusCall = invocations.First(i => i.Identifier == "homespunInterop.focusWithCursor");

        var args = focusCall.Arguments;
        Assert.That(args[1], Is.EqualTo("start"), "Second arg should be 'start' cursor position");
        Assert.That(args[2], Is.EqualTo(0), "Third arg should be 0 (empty input)");
    }

    [Test]
    public void HandleKeyDown_Tab_ShowsParentIndicator()
    {
        var cut = Render<InlineIssueCreateInput>(DefaultParams());

        var input = cut.Find("[data-testid='inline-issue-input']");
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = false });

        var indicator = cut.Find(".lane-indicator.parent");
        Assert.That(indicator.TextContent, Does.Contain("Parent of above"));
    }

    [Test]
    public void HandleKeyDown_ShiftTab_ShowsChildIndicator()
    {
        var cut = Render<InlineIssueCreateInput>(DefaultParams());

        var input = cut.Find("[data-testid='inline-issue-input']");
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        var indicator = cut.Find(".lane-indicator.child");
        Assert.That(indicator.TextContent, Does.Contain("Child of above"));
    }

    [Test]
    public void HandleKeyDown_Tab_InsertAbove_ShowsParentOfBelowIndicator()
    {
        var cut = Render<InlineIssueCreateInput>(p =>
        {
            p.Add(x => x.ProjectId, "test-project");
            p.Add(x => x.InsertIndex, 0);
            p.Add(x => x.InsertAbove, true);
            p.Add(x => x.AdjacentIssueId, "adjacent-123");
            p.Add(x => x.CanMoveUp, true);
            p.Add(x => x.CanMoveDown, true);
        });

        var input = cut.Find("[data-testid='inline-issue-input']");
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = false });

        var indicator = cut.Find(".lane-indicator.parent");
        Assert.That(indicator.TextContent, Does.Contain("Parent of below"));
    }

    [Test]
    public void HandleKeyDown_Tab_OnlyRegistersOnce()
    {
        var cut = Render<InlineIssueCreateInput>(DefaultParams());

        var input = cut.Find("[data-testid='inline-issue-input']");

        // First Tab → parent
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = false });
        var parentIndicator = cut.Find(".lane-indicator.parent");
        Assert.That(parentIndicator, Is.Not.Null, "First Tab should show parent indicator");

        // Second Tab → should be ignored (still parent)
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = false });
        parentIndicator = cut.Find(".lane-indicator.parent");
        Assert.That(parentIndicator, Is.Not.Null, "Second Tab should still show parent indicator");

        // Shift+Tab after Tab → should also be ignored
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });
        parentIndicator = cut.Find(".lane-indicator.parent");
        Assert.That(parentIndicator, Is.Not.Null, "Shift+Tab after Tab should be ignored");
    }

    [Test]
    public void HandleKeyDown_Enter_SubmitsWithTitle()
    {
        var cut = Render<InlineIssueCreateInput>(DefaultParams(p =>
            p.Add(x => x.OnCreated, EventCallback.Factory.Create<string>(this, _ => { }))));

        var input = cut.Find("[data-testid='inline-issue-input']");
        input.Input("Test Issue");
        input.KeyDown(Key.Enter);

        // Wait for async submit to complete
        cut.WaitForState(() => _mockHandler.CapturedRequests.Any(r => r.Method == HttpMethod.Post));

        var postRequest = _mockHandler.CapturedRequests.First(r => r.Method == HttpMethod.Post);
        var body = postRequest.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Title, Is.EqualTo("Test Issue"));
        Assert.That(body.ProjectId, Is.EqualTo("test-project"));
    }

    [Test]
    public void HandleKeyDown_Escape_CancelsCreation()
    {
        bool cancelInvoked = false;
        var cut = Render<InlineIssueCreateInput>(DefaultParams(p =>
            p.Add(x => x.OnCancel, EventCallback.Factory.Create(this, () => cancelInvoked = true))));

        var input = cut.Find("[data-testid='inline-issue-input']");
        input.KeyDown(Key.Escape);

        Assert.That(cancelInvoked, Is.True, "Escape should invoke OnCancel");
    }

    [Test]
    public void Submit_WithTabPressed_SetsChildIssueId()
    {
        var cut = Render<InlineIssueCreateInput>(DefaultParams(p =>
            p.Add(x => x.OnCreated, EventCallback.Factory.Create<string>(this, _ => { }))));

        var input = cut.Find("[data-testid='inline-issue-input']");

        // Press Tab to become parent
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = false });

        // Type and submit
        input.Input("New Parent Issue");
        input.KeyDown(Key.Enter);

        cut.WaitForState(() => _mockHandler.CapturedRequests.Any(r => r.Method == HttpMethod.Post));

        var postRequest = _mockHandler.CapturedRequests.First(r => r.Method == HttpMethod.Post);
        var body = postRequest.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ChildIssueId, Is.EqualTo("adjacent-123"),
            "Tab (parent) should set ChildIssueId to adjacent issue");
        Assert.That(body.ParentIssueId, Is.Null,
            "Tab (parent) should NOT set ParentIssueId");
    }

    [Test]
    public void Submit_WithShiftTabPressed_SetsParentIssueId()
    {
        var cut = Render<InlineIssueCreateInput>(DefaultParams(p =>
            p.Add(x => x.OnCreated, EventCallback.Factory.Create<string>(this, _ => { }))));

        var input = cut.Find("[data-testid='inline-issue-input']");

        // Press Shift+Tab to become child
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        // Type and submit
        input.Input("New Child Issue");
        input.KeyDown(Key.Enter);

        cut.WaitForState(() => _mockHandler.CapturedRequests.Any(r => r.Method == HttpMethod.Post));

        var postRequest = _mockHandler.CapturedRequests.First(r => r.Method == HttpMethod.Post);
        var body = postRequest.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ParentIssueId, Is.EqualTo("adjacent-123"),
            "Shift+Tab (child) should set ParentIssueId to adjacent issue");
        Assert.That(body.ChildIssueId, Is.Null,
            "Shift+Tab (child) should NOT set ChildIssueId");
    }
}
