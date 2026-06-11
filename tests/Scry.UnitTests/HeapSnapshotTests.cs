using Scry.Analysis;
using Scry.Analysis.Model;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class HeapSnapshotTests
{
    private static HeapSnapshot CreateTestSnapshot()
    {
        // Hand-made snapshot:
        // 3 types: System.String, Foo, System.Exception
        var typeNames = new[] { "System.String", "Foo", "System.Exception" };
        var typeMethodTables = new ulong[] { 0x1000UL, 0x2000UL, 0x3000UL };

        // 5 objects: typeIndex [0, 0, 1, 2, 2], addresses [0xa, 0xb, 0xc, 0xd, 0xe]
        var addresses = new ulong[] { 0xa, 0xb, 0xc, 0xd, 0xe };
        var typeIndex = new[] { 0, 0, 1, 2, 2 };
        var sizes = new ulong[] { 10, 20, 30, 40, 50 };

        // Exceptions at positions 3, 4 (addresses 0xd, 0xe)
        var exceptionIndices = new[] { 3, 4 };

        return new HeapSnapshot(typeNames, typeMethodTables, addresses, typeIndex, sizes, exceptionIndices);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Stat_NoFilter_Returns3EntriesSortedByTotalSizeDesc()
    {
        var snapshot = CreateTestSnapshot();
        var stats = snapshot.Stat(null);

        Assert.Equal(3, stats.Count);

        // System.Exception: 40 + 50 = 90 (largest, first)
        Assert.Equal("System.Exception", stats[0].Type);
        Assert.Equal(2, stats[0].Count);
        Assert.Equal(90UL, stats[0].TotalSize);

        // The next two entries both have TotalSize = 30 (System.String: 10+20=30, Foo: 30)
        // They may come in any order, so we check both exist with correct values
        var smallEntries = new[] { stats[1], stats[2] };
        var typeNames = smallEntries.Select(e => e.Type).OrderBy(t => t).ToList();
        Assert.Equal(new[] { "Foo", "System.String" }, typeNames);

        // Both should have TotalSize 30
        Assert.All(smallEntries, e => Assert.Equal(30UL, e.TotalSize));

        // System.String has count 2, Foo has count 1
        var fooEntry = smallEntries.Single(e => e.Type == "Foo");
        var stringEntry = smallEntries.Single(e => e.Type == "System.String");
        Assert.Equal(1, fooEntry.Count);
        Assert.Equal(2, stringEntry.Count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Stat_FilterFoo_Returns1Entry()
    {
        var snapshot = CreateTestSnapshot();
        var stats = snapshot.Stat("Foo");

        Assert.Single(stats);
        Assert.Equal("Foo", stats[0].Type);
        Assert.Equal(1, stats[0].Count);
        Assert.Equal(30UL, stats[0].TotalSize);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Objects_FilterSystemString_ReturnsBothAddresses()
    {
        var snapshot = CreateTestSnapshot();
        var page = snapshot.Objects("System.String", 0, 10);

        Assert.Equal(2, page.TotalMatches);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(0xaUL, page.Items[0].Address);
        Assert.Equal(0xbUL, page.Items[1].Address);
        Assert.Equal("System.String", page.Items[0].Type);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Objects_FilterSystemStringOffset1_ReturnsSingleItem()
    {
        var snapshot = CreateTestSnapshot();
        var page = snapshot.Objects("System.String", 1, 10);

        Assert.Equal(2, page.TotalMatches);
        Assert.Single(page.Items);
        Assert.Equal(0xbUL, page.Items[0].Address);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExceptionAddresses_Offset0Limit10_ReturnsBothExceptions()
    {
        var snapshot = CreateTestSnapshot();
        var page = snapshot.ExceptionAddresses(0, 10);

        Assert.Equal(2, page.TotalMatches);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(0xdUL, page.Items[0]);
        Assert.Equal(0xeUL, page.Items[1]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ObjectCount_ReturnsCorrectCount()
    {
        var snapshot = CreateTestSnapshot();
        Assert.Equal(5, snapshot.ObjectCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExceptionCount_ReturnsCorrectCount()
    {
        var snapshot = CreateTestSnapshot();
        Assert.Equal(2, snapshot.ExceptionCount);
    }
}
