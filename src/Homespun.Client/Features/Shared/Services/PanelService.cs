namespace Homespun.Client.Services;

/// <summary>
/// Service implementation for managing the detail panel state across pages.
/// Enables the title bar panel toggle button to control the detail sidebar.
/// </summary>
public class PanelService : IPanelService
{
    private bool _isPanelAvailable;
    private bool _isPanelOpen;

    /// <inheritdoc />
    public bool IsPanelAvailable => _isPanelAvailable;

    /// <inheritdoc />
    public bool IsPanelOpen => _isPanelOpen;

    /// <inheritdoc />
    public event Action? OnStateChanged;

    /// <inheritdoc />
    public void SetPanelAvailable(bool available)
    {
        if (_isPanelAvailable != available)
        {
            _isPanelAvailable = available;
            // When panel becomes unavailable, also close it
            if (!available && _isPanelOpen)
            {
                _isPanelOpen = false;
            }
            OnStateChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public void SetPanelOpen(bool open)
    {
        // Only allow opening if panel is available
        if (open && !_isPanelAvailable)
        {
            return;
        }

        if (_isPanelOpen != open)
        {
            _isPanelOpen = open;
            OnStateChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public void TogglePanel()
    {
        if (_isPanelAvailable)
        {
            _isPanelOpen = !_isPanelOpen;
            OnStateChanged?.Invoke();
        }
    }
}
