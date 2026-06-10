using Scry.Contracts;
using Xunit;

namespace Scry.UnitTests;

// Every unit test carries [Trait("Category", "Unit")] so the pre-commit hook can
// run exactly the fast, dump-free tests with `dotnet test --filter Category=Unit`.
[Trait("Category", "Unit")]
public sealed class ScryEndpointTests
{
    [Fact]
    public void DeriveId_IsDeterministic_ForSamePath()
    {
        var a = ScryEndpoint.DeriveId(@"C:\dumps\app.dmp");
        var b = ScryEndpoint.DeriveId(@"C:\dumps\app.dmp");

        Assert.Equal(a, b);
    }

    [Fact]
    public void DeriveId_HasExpectedShape()
    {
        var id = ScryEndpoint.DeriveId("app.dmp");

        Assert.StartsWith("scry-", id);
        Assert.Equal("scry-".Length + 16, id.Length);
        Assert.Matches("^scry-[0-9a-f]{16}$", id);
    }

    [Fact]
    public void DeriveId_NormalizesRelativeAndAbsoluteSpellings()
    {
        var viaRelative = ScryEndpoint.DeriveId("sub/../app.dmp");
        var viaAbsolute = ScryEndpoint.DeriveId(Path.GetFullPath("app.dmp"));

        Assert.Equal(viaAbsolute, viaRelative);
    }

    [Fact]
    public void DeriveId_DiffersForDifferentPaths()
    {
        var one = ScryEndpoint.DeriveId("a.dmp");
        var two = ScryEndpoint.DeriveId("b.dmp");

        Assert.NotEqual(one, two);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DeriveId_RejectsBlankPath(string path)
    {
        Assert.Throws<ArgumentException>(() => ScryEndpoint.DeriveId(path));
    }
}
