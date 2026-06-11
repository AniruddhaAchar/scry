using Scry.Analysis;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class HeapMatchTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Matches_TypeNameContainsFilter_ReturnsTrue()
    {
        Assert.True(HeapMatch.Matches("Foo", "Foo"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Matches_FilterInGenericType_ReturnsTrue()
    {
        Assert.True(HeapMatch.Matches("System.Collections.Generic.List<Foo>", "Foo"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Matches_FilterInGenericInterface_ReturnsTrue()
    {
        Assert.True(HeapMatch.Matches("ILogger<Foo>", "Foo"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Matches_CaseSensitive_FilterDoesNotMatchDifferentCase()
    {
        Assert.False(HeapMatch.Matches("Foo", "foo"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Matches_EmptyFilter_ReturnsTrue()
    {
        Assert.True(HeapMatch.Matches("Anything", ""));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Matches_NullFilter_ReturnsTrue()
    {
        Assert.True(HeapMatch.Matches("Anything", null));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Matches_NonMatchingSubstring_ReturnsFalse()
    {
        Assert.False(HeapMatch.Matches("Foo", "Bar"));
    }
}
