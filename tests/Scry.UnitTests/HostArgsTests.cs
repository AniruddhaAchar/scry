using Scry.Host;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class HostArgsTests
{
    [Fact]
    public void TryParse_FullArgs_ParsesAll()
    {
        var args = new[] { "--dump", "C:\\a.dmp", "--idle-timeout", "5", "--verbose" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Null(error);
        Assert.Equal("C:\\a.dmp", parsed.DumpPath);
        Assert.Equal(TimeSpan.FromMinutes(5), parsed.IdleTimeout);
        Assert.True(parsed.Verbose);
    }

    [Fact]
    public void TryParse_Defaults()
    {
        var args = new[] { "--dump", "x" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(TimeSpan.FromMinutes(10), parsed.IdleTimeout);
        Assert.False(parsed.Verbose);
    }

    [Fact]
    public void TryParse_ShortVerboseFlag()
    {
        var args = new[] { "--dump", "x", "-v" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.True(parsed.Verbose);
    }

    [Fact]
    public void TryParse_IdleTimeoutZero_Disables()
    {
        var args = new[] { "--dump", "x", "--idle-timeout", "0" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(TimeSpan.Zero, parsed.IdleTimeout);
    }

    [Fact]
    public void TryParse_MissingDump_Fails()
    {
        var args = new[] { "--idle-timeout", "5" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.False(success);
        Assert.Null(parsed);
        Assert.NotNull(error);
        Assert.Contains("required", error);
    }

    [Fact]
    public void TryParse_DanglingDump_Fails()
    {
        var args = new[] { "--dump" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.False(success);
        Assert.Null(parsed);
        Assert.NotNull(error);
        Assert.Contains("requires a path", error);
    }

    [Fact]
    public void TryParse_BadIdleTimeoutNonNumeric_Fails()
    {
        var args = new[] { "--dump", "x", "--idle-timeout", "abc" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.False(success);
        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_BadIdleTimeoutNegative_Fails()
    {
        var args = new[] { "--dump", "x", "--idle-timeout", "-1" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.False(success);
        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_UnknownArg_Fails()
    {
        var args = new[] { "--dump", "x", "--bogus" };
        var success = HostArgs.TryParse(args, out var parsed, out var error);
        Assert.False(success);
        Assert.Null(parsed);
        Assert.NotNull(error);
        Assert.Contains("unknown argument", error);
    }
}
