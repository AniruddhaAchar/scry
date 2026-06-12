using Scry.Analysis;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ValueTextTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Quote_WrapsInQuotes()
    {
        var result = ValueText.Quote("ab");
        Assert.Equal("\"ab\"", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Quote_EscapesSpecials()
    {
        var resultBackslash = ValueText.Quote("a\\b");
        Assert.Contains("\\\\", resultBackslash);

        var resultQuote = ValueText.Quote("a\"b");
        Assert.Contains("\\\"", resultQuote);

        var resultNewline = ValueText.Quote("x\ny");
        Assert.Contains("\\n", resultNewline);

        var resultCarriageReturn = ValueText.Quote("a\rb");
        Assert.Contains("\\r", resultCarriageReturn);

        var resultTab = ValueText.Quote("a\tb");
        Assert.Contains("\\t", resultTab);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Quote_Empty()
    {
        var result = ValueText.Quote("");
        Assert.Equal("\"\"", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Quote_TruncatesBeyondMaxLength()
    {
        var longString = new string('x', 300);
        var result = ValueText.Quote(longString, 256);
        Assert.Contains("…(+44 more)", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Quote_NoTruncationMarkerForShortString()
    {
        var result = ValueText.Quote("short", 256);
        Assert.DoesNotContain("more)", result);
    }
}
