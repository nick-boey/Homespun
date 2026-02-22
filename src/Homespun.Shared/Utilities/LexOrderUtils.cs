namespace Homespun.Shared.Utilities;

/// <summary>
/// Utility for computing lexicographic midpoints between sort order strings.
/// Used to insert new sibling issues between existing ones in series-mode parents.
/// </summary>
public static class LexOrderUtils
{
    private const char MinChar = '!'; // ASCII 33 - below all printable sort order chars
    private const char MaxChar = 'z';
    private const char MidChar = 'V';

    /// <summary>
    /// Computes a string that sorts lexicographically between <paramref name="before"/> and <paramref name="after"/>.
    /// </summary>
    /// <param name="before">The lower bound (exclusive), or null if inserting before all.</param>
    /// <param name="after">The upper bound (exclusive), or null if inserting after all.</param>
    /// <returns>A string that is lexicographically between before and after.</returns>
    public static string ComputeMidpoint(string? before, string? after)
    {
        if (before is null && after is null)
        {
            return MidChar.ToString();
        }

        if (before is null)
        {
            // Insert before 'after': prepend a character that sorts before after's first char
            // If after starts with MinChar, we need to go deeper
            if (after!.Length > 0 && after[0] > MinChar)
            {
                var midCharVal = (char)((MinChar + after[0]) / 2);
                if (midCharVal > MinChar && midCharVal < after[0])
                {
                    return midCharVal.ToString();
                }
            }
            // Fallback: prepend MinChar and recurse
            return MinChar + ComputeMidpoint(null, after!.Length > 0 ? after[1..] : null);
        }

        if (after is null)
        {
            // Insert after 'before': append MidChar
            return before + MidChar;
        }

        // Both bounds present: find midpoint between them
        return ComputeMidpointBetween(before, after);
    }

    private static string ComputeMidpointBetween(string a, string b)
    {
        // Pad shorter string with MinChar conceptually
        var maxLen = Math.Max(a.Length, b.Length);
        // Try character-by-character midpoint
        for (var i = 0; i < maxLen; i++)
        {
            var ca = i < a.Length ? a[i] : MinChar;
            var cb = i < b.Length ? b[i] : MaxChar;

            if (ca < cb)
            {
                var mid = (char)((ca + cb) / 2);
                if (mid > ca)
                {
                    // Found a valid midpoint at this position
                    var prefix = a[..i];
                    return prefix + mid;
                }
                // ca and cb are adjacent - need to go deeper
                // Take ca and find midpoint in the remaining space
                return a[..(i + 1)] + ComputeMidpoint(
                    i + 1 < a.Length ? a[(i + 1)..] : null,
                    null);
            }

            if (ca == cb)
            {
                continue; // Same character, move to next position
            }

            // ca > cb shouldn't happen if a < b, but handle gracefully
            break;
        }

        // Strings are equal up to maxLen - append MidChar to the shorter one
        return a + MidChar;
    }
}
