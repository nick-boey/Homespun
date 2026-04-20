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
    public void Truncate_NegativeChars_ReturnsNull()
    {
        Assert.That(ContentPreviewGate.Truncate("hello", -1), Is.Null);
    }
}
