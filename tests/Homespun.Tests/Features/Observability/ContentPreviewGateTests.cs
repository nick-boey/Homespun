using Homespun.Features.Observability;

namespace Homespun.Tests.Features.Observability;

/// <summary>
/// Behaviour-parity tests against the retired <c>SessionEventLog.TruncatePreview</c>.
/// </summary>
[TestFixture]
public class ContentPreviewGateTests
{
    [Test]
    public void Truncate_ZeroChars_ReturnsNull()
    {
        Assert.That(ContentPreviewGate.Truncate("hello", 0), Is.Null);
    }

    [Test]
    public void Truncate_NullText_ReturnsNull()
    {
        Assert.That(ContentPreviewGate.Truncate(null, 10), Is.Null);
    }

    [Test]
    public void Truncate_ExactLength_ReturnsOriginal()
    {
        Assert.That(ContentPreviewGate.Truncate("abcde", 5), Is.EqualTo("abcde"));
    }

    [Test]
    public void Truncate_Longer_TruncatesWithEllipsis()
    {
        Assert.That(ContentPreviewGate.Truncate("abcdefgh", 3), Is.EqualTo("abc\u2026"));
    }

    [Test]
    public void Truncate_Shorter_ReturnsOriginal()
    {
        Assert.That(ContentPreviewGate.Truncate("ab", 5), Is.EqualTo("ab"));
    }

    [Test]
    public void Truncate_UnicodeExactLength_ReturnsOriginal()
    {
        Assert.That(ContentPreviewGate.Truncate("héllo", 5), Is.EqualTo("héllo"));
    }

    [Test]
    public void Truncate_MinusOneSentinel_ReturnsOriginalUnchanged()
    {
        // -1 is the "no truncation" sentinel wired by HOMESPUN_DEBUG_FULL_MESSAGES.
        var longText = new string('a', 5000);
        Assert.That(ContentPreviewGate.Truncate(longText, -1), Is.EqualTo(longText));
    }

    [Test]
    public void Truncate_MinusOneSentinel_NullText_ReturnsNull()
    {
        Assert.That(ContentPreviewGate.Truncate(null, -1), Is.Null);
    }

    [Test]
    public void Truncate_NegativeBelowMinusOne_ReturnsNull()
    {
        Assert.That(ContentPreviewGate.Truncate("hello", -2), Is.Null);
    }
}
