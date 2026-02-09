namespace Homespun.Client.Components;

/// <summary>
/// Defines the type/severity of a form message.
/// </summary>
public enum FormMessageType
{
    /// <summary>
    /// Error message - indicates a failure or validation error.
    /// </summary>
    Error,

    /// <summary>
    /// Success message - indicates a successful operation.
    /// </summary>
    Success,

    /// <summary>
    /// Warning message - indicates something that needs attention but isn't an error.
    /// </summary>
    Warning,

    /// <summary>
    /// Info message - provides neutral information.
    /// </summary>
    Info
}
