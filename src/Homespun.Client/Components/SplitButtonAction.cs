using Microsoft.AspNetCore.Components;

namespace Homespun.Client.Components;

/// <summary>
/// Represents an action in a split button dropdown.
/// </summary>
public record SplitButtonAction(
    string Key,
    string Text,
    RenderFragment? Icon,
    EventCallback OnClick);
