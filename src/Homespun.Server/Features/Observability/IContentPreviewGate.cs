namespace Homespun.Features.Observability;

/// <summary>
/// Gates the <c>homespun.content.preview</c> span attribute using
/// <see cref="SessionEventContentOptions.ContentPreviewChars"/>. Call sites
/// should invoke <see cref="Gate"/> before passing the string to
/// <c>Activity.SetTag</c>; a null return means "do not set the attribute".
/// </summary>
public interface IContentPreviewGate
{
    /// <summary>
    /// Returns <paramref name="text"/> truncated to the configured preview
    /// length followed by an ellipsis when it exceeds the budget. Returns
    /// <c>null</c> when <paramref name="text"/> is null or the configured
    /// length is zero.
    /// </summary>
    string? Gate(string? text);
}
