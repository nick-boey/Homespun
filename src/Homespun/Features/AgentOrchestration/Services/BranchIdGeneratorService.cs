using System.Text.RegularExpressions;
using Homespun.Features.PullRequests;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Service for generating working branch IDs from issue titles using AI.
/// Falls back to title sanitization if AI is unavailable or returns invalid output.
/// </summary>
public partial class BranchIdGeneratorService : IBranchIdGeneratorService
{
    private readonly IMiniPromptService _miniPromptService;
    private readonly ILogger<BranchIdGeneratorService> _logger;

    private const string BranchIdPrompt = """
        Generate a concise working branch ID for the following issue title.

        Requirements:
        - EXACTLY 2 to 4 simple, descriptive words
        - All lowercase letters only
        - Words separated by single hyphens
        - No special characters, numbers, or underscores
        - Capture the essence of what the issue is about
        - Use common programming terminology when applicable

        Examples:
        - "Add user authentication" -> "add-user-auth"
        - "Fix login button not working on mobile" -> "fix-mobile-login"
        - "Implement dark mode toggle" -> "add-dark-mode"
        - "Update database schema for new fields" -> "update-db-schema"
        - "Refactor API error handling" -> "refactor-api-errors"

        Issue title: "{0}"

        Respond with ONLY the branch ID, nothing else. No explanation, no quotes, just the branch ID.
        """;

    public BranchIdGeneratorService(
        IMiniPromptService miniPromptService,
        ILogger<BranchIdGeneratorService>? logger = null)
    {
        _miniPromptService = miniPromptService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BranchIdGeneratorService>.Instance;
    }

    /// <inheritdoc />
    public async Task<BranchIdResult> GenerateAsync(string title, CancellationToken cancellationToken = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title cannot be null or empty", nameof(title));
        }

        try
        {
            // Attempt AI generation
            var prompt = string.Format(BranchIdPrompt, title.Trim());
            var result = await _miniPromptService.ExecuteAsync(prompt, "haiku", cancellationToken);

            if (result.Success && !string.IsNullOrWhiteSpace(result.Response))
            {
                var branchId = SanitizeAndValidateAIResponse(result.Response);
                if (branchId != null)
                {
                    _logger.LogDebug("AI generated branch ID '{BranchId}' for title '{Title}'", branchId, title);
                    return new BranchIdResult(
                        Success: true,
                        BranchId: branchId,
                        Error: null,
                        WasAiGenerated: true);
                }

                _logger.LogDebug("AI response '{Response}' did not pass validation, falling back to sanitization", result.Response);
            }
            else
            {
                _logger.LogDebug("AI generation failed: {Error}, falling back to sanitization", result.Error);
            }

            // Fall back to sanitization
            return FallbackToSanitization(title);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Branch ID generation cancelled for title '{Title}'", title);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI branch ID generation failed for title '{Title}', falling back to sanitization", title);
            return FallbackToSanitization(title);
        }
    }

    /// <summary>
    /// Sanitizes and validates the AI response to ensure it matches the expected format.
    /// </summary>
    /// <param name="response">The raw AI response</param>
    /// <returns>A valid branch ID, or null if the response is invalid</returns>
    private string? SanitizeAndValidateAIResponse(string response)
    {
        // Clean up the response
        var cleaned = response.Trim().ToLowerInvariant();

        // Remove any quotes that might have been added
        cleaned = cleaned.Trim('"', '\'', '`');

        // Remove any newlines
        cleaned = cleaned.Split('\n')[0].Trim();

        // Remove any characters that aren't lowercase letters or hyphens
        cleaned = NonAlphaHyphenRegex().Replace(cleaned, "");

        // Normalize consecutive hyphens
        cleaned = ConsecutiveHyphensRegex().Replace(cleaned, "-");

        // Trim hyphens from start and end
        cleaned = cleaned.Trim('-');

        if (string.IsNullOrEmpty(cleaned))
        {
            return null;
        }

        // Validate word count (2-4 words)
        var wordCount = cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < 2 || wordCount > 4)
        {
            _logger.LogDebug("AI response has {WordCount} words, expected 2-4", wordCount);
            return null;
        }

        return cleaned;
    }

    /// <summary>
    /// Falls back to using the existing BranchNameGenerator.SanitizeForBranch method.
    /// </summary>
    private BranchIdResult FallbackToSanitization(string title)
    {
        var sanitized = BranchNameGenerator.SanitizeForBranch(title);

        _logger.LogDebug("Falling back to sanitization for title '{Title}': '{BranchId}'", title, sanitized);

        return new BranchIdResult(
            Success: true,
            BranchId: sanitized,
            Error: null,
            WasAiGenerated: false);
    }

    [GeneratedRegex("[^a-z-]")]
    private static partial Regex NonAlphaHyphenRegex();

    [GeneratedRegex("-+")]
    private static partial Regex ConsecutiveHyphensRegex();
}
