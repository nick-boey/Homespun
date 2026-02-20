using Bunit;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class InlineIssueEditorTests : BunitTestContext
{
    private Mock<IKeyboardNavigationService> _mockNavService = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockNavService = new Mock<IKeyboardNavigationService>();
        Services.AddSingleton(_mockNavService.Object);

        // Setup bUnit JS interop to handle the focusWithCursor call
        Context!.JSInterop.SetupVoid("homespunInterop.focusWithCursor", _ => true);
    }

    [Test]
    public void Renders_InputWithInitialTitle()
    {
        var cut = Render<InlineIssueEditor>(p =>
            p.Add(x => x.Title, "Test Title"));

        var input = cut.Find("input.inline-issue-input");
        Assert.That(input.GetAttribute("value"), Is.EqualTo("Test Title"));
    }

    [Test]
    public void HandleInput_UpdatesTitleAndInvokesCallback()
    {
        string? receivedTitle = null;
        var cut = Render<InlineIssueEditor>(p =>
        {
            p.Add(x => x.Title, "Initial");
            p.Add(x => x.TitleChanged, EventCallback.Factory.Create<string>(this, value => receivedTitle = value));
        });

        var input = cut.Find("input.inline-issue-input");
        input.Input("Initial appended");

        Assert.That(receivedTitle, Is.EqualTo("Initial appended"));
        _mockNavService.Verify(s => s.UpdateEditTitle("Initial appended"), Times.Once);
    }

    [Test]
    public void HandleKeyDown_Escape_CallsCancelEdit()
    {
        var cut = Render<InlineIssueEditor>(p =>
            p.Add(x => x.Title, "Test"));

        var input = cut.Find("input.inline-issue-input");
        input.KeyDown(Key.Escape);

        _mockNavService.Verify(s => s.CancelEdit(), Times.Once);
    }

    [Test]
    public void HandleKeyDown_Enter_CallsAcceptEditAsync()
    {
        _mockNavService.Setup(s => s.AcceptEditAsync()).Returns(Task.CompletedTask);

        var cut = Render<InlineIssueEditor>(p =>
            p.Add(x => x.Title, "Test"));

        var input = cut.Find("input.inline-issue-input");
        input.KeyDown(Key.Enter);

        _mockNavService.Verify(s => s.AcceptEditAsync(), Times.Once);
    }

    [Test]
    public void HandleKeyDown_Tab_CallsIndentAsChild()
    {
        var cut = Render<InlineIssueEditor>(p =>
            p.Add(x => x.Title, "Test"));

        var input = cut.Find("input.inline-issue-input");
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = false });

        _mockNavService.Verify(s => s.IndentAsChild(), Times.Once);
    }

    [Test]
    public void HandleKeyDown_ShiftTab_CallsUnindentAsSibling()
    {
        var cut = Render<InlineIssueEditor>(p =>
            p.Add(x => x.Title, "Test"));

        var input = cut.Find("input.inline-issue-input");
        input.KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        _mockNavService.Verify(s => s.UnindentAsSibling(), Times.Once);
    }

    [Test]
    public void HandleKeyDown_RegularKey_DoesNotCallUpdateEditTitle()
    {
        // After the fix, regular keydown events should NOT sync title to service.
        // Title sync happens through the oninput handler instead.
        var cut = Render<InlineIssueEditor>(p =>
            p.Add(x => x.Title, "Test"));

        var input = cut.Find("input.inline-issue-input");
        input.KeyDown(new KeyboardEventArgs { Key = "a" });

        _mockNavService.Verify(s => s.UpdateEditTitle(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void FocusOnFirstRender_CallsFocusWithCursor()
    {
        var cut = Render<InlineIssueEditor>(p =>
        {
            p.Add(x => x.Title, "Hello");
            p.Add(x => x.CursorPosition, EditCursorPosition.End);
        });

        // Verify the JS interop was called with focusWithCursor (not focusAndSetValue)
        var invocations = Context!.JSInterop.Invocations;
        var focusCall = invocations.FirstOrDefault(i => i.Identifier == "homespunInterop.focusWithCursor");
        Assert.That(focusCall, Is.Not.Null, "Should call homespunInterop.focusWithCursor on first render");
    }

    [Test]
    public void FocusOnFirstRender_PassesValueLength_NotValue()
    {
        var cut = Render<InlineIssueEditor>(p =>
        {
            p.Add(x => x.Title, "Hello");
            p.Add(x => x.CursorPosition, EditCursorPosition.End);
        });

        var invocations = Context!.JSInterop.Invocations;
        var focusCall = invocations.First(i => i.Identifier == "homespunInterop.focusWithCursor");

        // Should pass: element, cursorPosition string, value length (not the value itself)
        var args = focusCall.Arguments;
        Assert.That(args[1], Is.EqualTo("end"), "Second arg should be cursor position string");
        Assert.That(args[2], Is.EqualTo(5), "Third arg should be value length (5 for 'Hello')");
    }
}
