using Markdig;
using System.Text.RegularExpressions;
using System.Web;

namespace Homespun.Client.Services;

public interface IMarkdownRenderingService
{
    string RenderToHtml(string? markdown);
    bool ContainsMarkdown(string? text);
}

public partial class MarkdownRenderingService : IMarkdownRenderingService
{
    private readonly MarkdownPipeline _pipeline;

    [GeneratedRegex(
        @"\s+@?on[a-z]+(?::[a-z]+)?\s*=\s*(?:""[^""]*""|'[^']*'|\S+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex DangerousAttributePattern();

    public MarkdownRenderingService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAlertBlocks()
            .UseAbbreviations()
            .UseAutoIdentifiers()
            .UseCitations()
            .UseCustomContainers()
            .UseDefinitionLists()
            .UseEmphasisExtras()
            .UseFigures()
            .UseFooters()
            .UseFootnotes()
            .UseGridTables()
            .UseMathematics()
            .UseMediaLinks()
            .UsePipeTables()
            .UseListExtras()
            .UseTaskLists()
            .UseDiagrams()
            .UseAutoLinks()
            .UseEmojiAndSmiley()
            .UseSoftlineBreakAsHardlineBreak()
            .DisableHtml()
            .Build();
    }

    public string RenderToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        if (!ContainsMarkdown(markdown))
            return $"<p>{HttpUtility.HtmlEncode(markdown)}</p>";

        var html = Markdown.ToHtml(markdown, _pipeline);
        return DangerousAttributePattern().Replace(html, string.Empty);
    }

    public bool ContainsMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (Regex.IsMatch(text, @"^#{1,6}\s", RegexOptions.Multiline)) return true;
        if (Regex.IsMatch(text, @"^[\*\-\+]\s", RegexOptions.Multiline)) return true;
        if (Regex.IsMatch(text, @"^\d+\.\s", RegexOptions.Multiline)) return true;
        if (text.Contains("```") || text.Contains("~~~")) return true;
        if (Regex.IsMatch(text, @"\[.+?\]\(.+?\)")) return true;
        if (Regex.IsMatch(text, @"\*\*.+?\*\*|\*.+?\*|__.+?__|_.+?_")) return true;
        if (Regex.IsMatch(text, @"^>\s", RegexOptions.Multiline)) return true;
        if (Regex.IsMatch(text, @"^(\*\*\*|---|___)$", RegexOptions.Multiline)) return true;

        return false;
    }
}
