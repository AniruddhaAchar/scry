using Scry.Analysis.Model;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class PageTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void From_Offset0Limit10Over100Items_ReturnsFirst10Truncated()
    {
        var source = Enumerable.Range(0, 100);
        var page = Page.From(source, 0, 10);

        Assert.Equal(10, page.Items.Count);
        Assert.Equal(0, page.Items[0]);
        Assert.Equal(100, page.TotalMatches);
        Assert.True(page.Truncated);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void From_Offset95Limit10Over100Items_ReturnsFinalItems5NotTruncated()
    {
        var source = Enumerable.Range(0, 100);
        var page = Page.From(source, 95, 10);

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(95, page.Items[0]);
        Assert.Equal(99, page.Items[4]);
        Assert.Equal(100, page.TotalMatches);
        Assert.False(page.Truncated);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void From_Limit0_UsesDefaultLimit1000()
    {
        var source = Enumerable.Range(0, 100);
        var page = Page.From(source, 0, 0);

        Assert.Equal(100, page.Items.Count);
        Assert.Equal(100, page.TotalMatches);
        Assert.False(page.Truncated);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void From_OffsetPastEnd_ReturnsEmptyItems()
    {
        var source = Enumerable.Range(0, 100);
        var page = Page.From(source, 200, 10);

        Assert.Empty(page.Items);
        Assert.Equal(100, page.TotalMatches);
        Assert.False(page.Truncated);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void From_SmallSourceLargeLimit_ReturnsAllItemsNotTruncated()
    {
        var source = Enumerable.Range(0, 5);
        var page = Page.From(source, 0, 10);

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(5, page.TotalMatches);
        Assert.False(page.Truncated);
    }
}
