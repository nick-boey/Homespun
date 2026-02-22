using Homespun.Shared.Utilities;

namespace Homespun.Tests.Utilities;

[TestFixture]
public class LexOrderUtilsTests
{
    [Test]
    public void ComputeMidpoint_BetweenTwoValues_ReturnsLexicographicMiddle()
    {
        var result = LexOrderUtils.ComputeMidpoint("0", "1");

        Assert.That(string.Compare(result, "0", StringComparison.Ordinal), Is.GreaterThan(0),
            $"Result '{result}' should be > '0'");
        Assert.That(string.Compare(result, "1", StringComparison.Ordinal), Is.LessThan(0),
            $"Result '{result}' should be < '1'");
    }

    [Test]
    public void ComputeMidpoint_AfterLast_ReturnsValueAfter()
    {
        var result = LexOrderUtils.ComputeMidpoint("1", null);

        Assert.That(string.Compare(result, "1", StringComparison.Ordinal), Is.GreaterThan(0),
            $"Result '{result}' should be > '1'");
    }

    [Test]
    public void ComputeMidpoint_BeforeFirst_ReturnsValueBefore()
    {
        var result = LexOrderUtils.ComputeMidpoint(null, "0");

        Assert.That(string.Compare(result, "0", StringComparison.Ordinal), Is.LessThan(0),
            $"Result '{result}' should be < '0'");
    }

    [Test]
    public void ComputeMidpoint_BothNull_ReturnsDefault()
    {
        var result = LexOrderUtils.ComputeMidpoint(null, null);

        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ComputeMidpoint_BetweenAdjacentValues_ReturnsValidMidpoint()
    {
        // "0" and "1" are adjacent single-char values in ASCII
        var result = LexOrderUtils.ComputeMidpoint("0", "1");

        Assert.That(string.Compare(result, "0", StringComparison.Ordinal), Is.GreaterThan(0));
        Assert.That(string.Compare(result, "1", StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public void ComputeMidpoint_BetweenWidelySpacedValues_ReturnsMidpoint()
    {
        var result = LexOrderUtils.ComputeMidpoint("A", "z");

        Assert.That(string.Compare(result, "A", StringComparison.Ordinal), Is.GreaterThan(0));
        Assert.That(string.Compare(result, "z", StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public void ComputeMidpoint_MultipleMidpoints_AreAllDistinct()
    {
        // Simulate inserting multiple siblings between "0" and "1"
        var mid1 = LexOrderUtils.ComputeMidpoint("0", "1");
        var mid2 = LexOrderUtils.ComputeMidpoint("0", mid1);
        var mid3 = LexOrderUtils.ComputeMidpoint(mid1, "1");

        Assert.That(mid1, Is.Not.EqualTo(mid2));
        Assert.That(mid1, Is.Not.EqualTo(mid3));
        Assert.That(mid2, Is.Not.EqualTo(mid3));

        // Verify ordering: "0" < mid2 < mid1 < mid3 < "1"
        Assert.That(string.Compare("0", mid2, StringComparison.Ordinal), Is.LessThan(0));
        Assert.That(string.Compare(mid2, mid1, StringComparison.Ordinal), Is.LessThan(0));
        Assert.That(string.Compare(mid1, mid3, StringComparison.Ordinal), Is.LessThan(0));
        Assert.That(string.Compare(mid3, "1", StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public void ComputeMidpoint_AfterLast_MultipleInserts_AreOrdered()
    {
        var a = LexOrderUtils.ComputeMidpoint("1", null);
        var b = LexOrderUtils.ComputeMidpoint(a, null);

        Assert.That(string.Compare("1", a, StringComparison.Ordinal), Is.LessThan(0));
        Assert.That(string.Compare(a, b, StringComparison.Ordinal), Is.LessThan(0));
    }
}
