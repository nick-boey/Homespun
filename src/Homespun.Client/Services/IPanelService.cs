namespace Homespun.Client.Services;

/// <summary>
/// Service for managing the detail panel state across pages.
/// Enables the title bar panel toggle button to control the detail sidebar.
/// </summary>
public interface IPanelService
{
    /// <summary>
    /// Whether a panel is available on the current page.
    /// When false, the panel toggle button should be disabled.
    /// </summary>
    bool IsPanelAvailable { get; }

    /// <summary>
    /// Whether the panel is currently open.
    /// </summary>
    bool IsPanelOpen { get; }

    /// <summary>
    /// Register that a panel is available on the current page.
    /// Call this from pages that have a detail sidebar.
    /// </summary>
    /// <param name="available">Whether the panel is available.</param>
    void SetPanelAvailable(bool available);

    /// <summary>
    /// Set whether the panel is open or closed.
    /// </summary>
    /// <param name="open">Whether the panel should be open.</param>
    void SetPanelOpen(bool open);

    /// <summary>
    /// Toggle the panel open/closed state.
    /// </summary>
    void TogglePanel();

    /// <summary>
    /// Event fired when panel state changes (availability or open state).
    /// Subscribe to this to react to panel state changes.
    /// </summary>
    event Action? OnStateChanged;
}
