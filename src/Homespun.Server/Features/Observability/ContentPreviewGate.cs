using Microsoft.Extensions.Options;

namespace Homespun.Features.Observability;

/// <summary>
/// Default <see cref="IContentPreviewGate"/>. Reads
/// <see cref="SessionEventContentOptions.ContentPreviewChars"/> via
/// <see cref="IOptionsMonitor{TOptions}"/> so live config changes take effect
/// without restart.
/// </summary>
public sealed class ContentPreviewGate : IContentPreviewGate
{
    private readonly IOptionsMonitor<SessionEventContentOptions> _options;

    public ContentPreviewGate(IOptionsMonitor<SessionEventContentOptions> options)
    {
        _options = options;
    }

    public string? Gate(string? text)
    {
        var chars = _options.CurrentValue.ContentPreviewChars;
        return Truncate(text, chars);
    }

    internal static string? Truncate(string? text, int chars)
    {
        if (chars <= 0 || text is null)
        {
            return null;
        }

        if (text.Length <= chars)
        {
            return text;
        }

        return text[..chars] + "\u2026";
    }
}
