using Markdig;
using System.Text.RegularExpressions;
using System.Web;

namespace Homespun.Features.Shared.Services;

/// <summary>
/// Service for rendering markdown text to HTML using Markdig.
/// </summary>
public partial class MarkdownRenderingService : IMarkdownRenderingService
{
    private readonly MarkdownPipeline _pipeline;

    /// <summary>
    /// Regex to match dangerous HTML event handler attributes in rendered output.
    /// Matches attributes like onclick="...", onerror="...", @onclick="...",
    /// and colon-variants like onclick:preventDefault="..." which are invalid HTML
    /// attribute names and crash Blazor Server circuits with InvalidCharacterError.
    /// </summary>
    [GeneratedRegex(
        @"\s+@?on[a-z]+(?::[a-z]+)?\s*=\s*(?:""[^""]*""|'[^']*'|\S+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex DangerousAttributePattern();

    public MarkdownRenderingService()
    {
        // Configure Markdig pipeline with safe, commonly-used extensions.
        // IMPORTANT: We intentionally do NOT use UseAdvancedExtensions() because it includes
        // GenericAttributesExtension, which allows arbitrary HTML attribute injection via
        // {key=value} syntax in markdown (e.g., ## Title{onclick=alert(1)}). This creates:
        // 1. XSS vulnerabilities through event handler injection
        // 2. Blazor Server circuit crashes when @onclick or onclick:preventDefault appear
        //    as literal DOM attributes (InvalidCharacterError: @onclick is not a valid
        //    attribute name)
        // Instead, we include each safe extension individually.
        _pipeline = new MarkdownPipelineBuilder()
            .UseAlertBlocks()                    // > [!NOTE] alert blocks
            .UseAbbreviations()                  // Abbreviation definitions
            .UseAutoIdentifiers()                // Auto-generate heading IDs
            .UseCitations()                      // "citation" syntax
            .UseCustomContainers()               // :::container blocks
            .UseDefinitionLists()                // Definition lists
            .UseEmphasisExtras()                 // ~~strikethrough~~, superscript, etc.
            .UseFigures()                        // Figure/figcaption
            .UseFooters()                        // Footer blocks
            .UseFootnotes()                      // [^1] footnotes
            .UseGridTables()                     // Grid-style tables
            .UseMathematics()                    // $math$ blocks
            .UseMediaLinks()                     // Media link embedding
            .UsePipeTables()                     // | pipe | tables |
            .UseListExtras()                     // Additional list features
            .UseTaskLists()                      // - [x] task lists
            .UseDiagrams()                       // Diagram blocks
            .UseAutoLinks()                      // Auto-convert URLs to links
            .UseEmojiAndSmiley()                 // :emoji: support
            .UseSoftlineBreakAsHardlineBreak()   // Single line breaks become <br>
            .DisableHtml()                       // SECURITY: Disable raw HTML in markdown
            .Build();
        // GenericAttributesExtension intentionally excluded — see comment above
    }

    /// <inheritdoc/>
    public string RenderToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        // If it doesn't look like markdown, return as plain text wrapped in <p>
        if (!ContainsMarkdown(markdown))
        {
            return $"<p>{HttpUtility.HtmlEncode(markdown)}</p>";
        }

        // Convert markdown to HTML and sanitize the output to remove any
        // dangerous event handler attributes that could cause XSS or crash
        // the Blazor Server circuit
        var html = Markdown.ToHtml(markdown, _pipeline);
        return SanitizeHtml(html);
    }

    /// <summary>
    /// Removes dangerous HTML event handler attributes from the rendered output.
    /// This is a defense-in-depth measure that strips onclick, onerror, onload, etc.
    /// attributes to prevent XSS and Blazor circuit crashes even if the Markdig pipeline
    /// is modified in the future.
    /// </summary>
    internal static string SanitizeHtml(string html)
    {
        return DangerousAttributePattern().Replace(html, string.Empty);
    }

    /// <inheritdoc/>
    public bool ContainsMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Heuristic checks for common markdown patterns
        // Headers
        if (Regex.IsMatch(text, @"^#{1,6}\s", RegexOptions.Multiline))
            return true;

        // Lists (unordered or ordered)
        if (Regex.IsMatch(text, @"^[\*\-\+]\s", RegexOptions.Multiline))
            return true;
        if (Regex.IsMatch(text, @"^\d+\.\s", RegexOptions.Multiline))
            return true;

        // Code blocks (fenced or indented)
        if (text.Contains("```") || text.Contains("~~~"))
            return true;

        // Links
        if (Regex.IsMatch(text, @"\[.+?\]\(.+?\)"))
            return true;

        // Emphasis
        if (Regex.IsMatch(text, @"\*\*.+?\*\*|\*.+?\*|__.+?__|_.+?_"))
            return true;

        // Blockquotes
        if (Regex.IsMatch(text, @"^>\s", RegexOptions.Multiline))
            return true;

        // Horizontal rules
        if (Regex.IsMatch(text, @"^(\*\*\*|---|___)$", RegexOptions.Multiline))
            return true;

        // No markdown patterns detected
        return false;
    }
}
