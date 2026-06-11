using Scry.Client;
using Scry.Contracts;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ResolveTargetTests
{
    private static SessionDescriptor MakeDescriptor(string handle, string dumpPath, int pid = 1234)
        => new(handle, dumpPath, pid, DateTimeOffset.UtcNow, "0.0.1");

    [Fact]
    public void ExplicitHandle_Present_ReturnsDescriptor()
    {
        var handle = "scry-aaa";
        var descriptor = MakeDescriptor(handle, "C:\\test.dmp");
        var sessions = new[] { descriptor };

        var result = ScryCommands.ResolveTarget(handle, null, sessions);
        Assert.Equal(handle, result.Handle);
        Assert.NotNull(result.Descriptor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ExplicitHandle_Absent_ReturnsHandleNoDescriptor()
    {
        var sessions = new[] { MakeDescriptor("scry-bbb", "C:\\other.dmp") };

        var result = ScryCommands.ResolveTarget("scry-xyz", null, sessions);
        Assert.Equal("scry-xyz", result.Handle);
        Assert.Null(result.Descriptor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ExplicitHandle_EmptySessionList_ReturnsHandleNoDescriptor()
    {
        var result = ScryCommands.ResolveTarget("scry-xyz", null, Array.Empty<SessionDescriptor>());
        Assert.Equal("scry-xyz", result.Handle);
        Assert.Null(result.Descriptor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void DumpPath_DerivesHandle()
    {
        var dumpPath = "C:\\d.dmp";
        var expectedHandle = ScryEndpoint.DeriveId(dumpPath);
        var descriptor = MakeDescriptor(expectedHandle, dumpPath);
        var sessions = new[] { descriptor };

        var result = ScryCommands.ResolveTarget(null, dumpPath, sessions);
        Assert.Equal(expectedHandle, result.Handle);
        Assert.NotNull(result.Descriptor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void DumpPath_NonMatchingSession_ReturnsHandleNoDescriptor()
    {
        var dumpPath = "C:\\d.dmp";
        var expectedHandle = ScryEndpoint.DeriveId(dumpPath);
        var sessions = new[] { MakeDescriptor("scry-other", "C:\\other.dmp") };

        var result = ScryCommands.ResolveTarget(null, dumpPath, sessions);
        Assert.Equal(expectedHandle, result.Handle);
        Assert.Null(result.Descriptor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void NoTarget_Empty_Errors()
    {
        var result = ScryCommands.ResolveTarget(null, null, Array.Empty<SessionDescriptor>());
        Assert.Null(result.Handle);
        Assert.Null(result.Descriptor);
        Assert.NotNull(result.Error);
        Assert.Equal("FAILED_PRECONDITION", result.Error.Code);
    }

    [Fact]
    public void NoTarget_SingleSession_ReturnsIt()
    {
        var descriptor = MakeDescriptor("scry-single", "C:\\single.dmp");
        var sessions = new[] { descriptor };

        var result = ScryCommands.ResolveTarget(null, null, sessions);
        Assert.Equal("scry-single", result.Handle);
        Assert.NotNull(result.Descriptor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void NoTarget_MultipleSessions_Errors()
    {
        var sessions = new[]
        {
            MakeDescriptor("scry-aaa", "C:\\a.dmp", 1000),
            MakeDescriptor("scry-bbb", "C:\\b.dmp", 2000),
        };

        var result = ScryCommands.ResolveTarget(null, null, sessions);
        Assert.Null(result.Handle);
        Assert.Null(result.Descriptor);
        Assert.NotNull(result.Error);
        Assert.Equal("FAILED_PRECONDITION", result.Error.Code);
    }
}
