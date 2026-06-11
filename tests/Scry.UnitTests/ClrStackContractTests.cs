using Google.Protobuf;
using Scry.Contracts.V1;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ClrStackContractTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ClrStackRequest_AllThreads_DefaultsFalse_AndZeroId()
    {
        var req = new ClrStackRequest();
        Assert.False(req.AllThreads);
        Assert.Equal(0u, req.ThreadOsId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ClrStackRequest_RoundTrips_ThroughProtobuf()
    {
        var req = new ClrStackRequest { AllThreads = true, ThreadOsId = 1234 };
        var bytes = req.ToByteArray();
        var parsed = ClrStackRequest.Parser.ParseFrom(bytes);

        Assert.True(parsed.AllThreads);
        Assert.Equal(1234u, parsed.ThreadOsId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ClrStackResponse_RoundTrips_NestedFrames()
    {
        var frame = new StackFrame
        {
            Kind = "ManagedMethod",
            InstructionPointer = 0x7ff0UL,
            StackPointer = 0x1000UL,
            Method = "Foo.Bar",
            Type = "Foo",
            Module = "app.dll"
        };

        var threadStack = new ThreadStack
        {
            OsThreadId = 7,
            ManagedThreadId = 3,
            IsAlive = true,
            Frames = { frame }
        };

        var resp = new ClrStackResponse { Threads = { threadStack } };
        var bytes = resp.ToByteArray();
        var parsed = ClrStackResponse.Parser.ParseFrom(bytes);

        Assert.Single(parsed.Threads);
        var t = parsed.Threads[0];
        Assert.Equal(7u, t.OsThreadId);
        Assert.Equal(3, t.ManagedThreadId);
        Assert.True(t.IsAlive);

        Assert.Single(t.Frames);
        var f = t.Frames[0];
        Assert.Equal("ManagedMethod", f.Kind);
        Assert.Equal(0x7ff0UL, f.InstructionPointer);
        Assert.Equal(0x1000UL, f.StackPointer);
        Assert.Equal("Foo.Bar", f.Method);
        Assert.Equal("Foo", f.Type);
        Assert.Equal("app.dll", f.Module);
    }
}
