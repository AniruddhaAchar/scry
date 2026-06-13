using Google.Protobuf;
using Scry.Contracts.V1;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ConcurrencyContractTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void SyncBlkResponse_RoundTrips()
    {
        var block = new SyncBlock
        {
            Index = 3,
            ObjectAddress = 0xC0FFEEUL,
            ObjectType = "System.Object",
            MonitorHeld = true,
            OwningThreadAddress = 0xBEEFUL,
            OwningOsThreadId = 7788,
            OwningManagedThreadId = 4,
            RecursionCount = 2,
            WaitingThreadCount = 1,
        };

        var resp = new SyncBlkResponse { Blocks = { block } };
        var parsed = SyncBlkResponse.Parser.ParseFrom(resp.ToByteArray());

        Assert.Single(parsed.Blocks);
        var b = parsed.Blocks[0];
        Assert.Equal(3, b.Index);
        Assert.Equal(0xC0FFEEUL, b.ObjectAddress);
        Assert.Equal("System.Object", b.ObjectType);
        Assert.True(b.MonitorHeld);
        Assert.Equal(0xBEEFUL, b.OwningThreadAddress);
        Assert.Equal(7788U, b.OwningOsThreadId);
        Assert.Equal(4, b.OwningManagedThreadId);
        Assert.Equal(2, b.RecursionCount);
        Assert.Equal(1, b.WaitingThreadCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DumpAsyncResponse_RoundTrips()
    {
        var suspended = new AsyncStateMachine
        {
            Address = 0xFEEDFACEUL,
            Type = "App.Worker+<RunAsync>d__3",
            State = 1,
            HasState = true,
            Status = "suspended at await 1",
            ContinuationAddress = 0xCAFEUL,
            ContinuationType = "System.Threading.Tasks.Task",
        };
        var unknown = new AsyncStateMachine
        {
            Address = 0xDEADUL,
            Type = "App.Other+<Go>d__0",
            HasState = false,
            Status = "unknown",
        };

        var resp = new DumpAsyncResponse
        {
            TotalMatches = 2,
            Truncated = false,
            Machines = { suspended, unknown },
        };
        var parsed = DumpAsyncResponse.Parser.ParseFrom(resp.ToByteArray());

        Assert.Equal(2, parsed.TotalMatches);
        Assert.False(parsed.Truncated);
        Assert.Equal(2, parsed.Machines.Count);

        var m0 = parsed.Machines[0];
        Assert.Equal(0xFEEDFACEUL, m0.Address);
        Assert.Equal("App.Worker+<RunAsync>d__3", m0.Type);
        Assert.True(m0.HasState);
        Assert.Equal(1, m0.State);
        Assert.Equal("suspended at await 1", m0.Status);
        Assert.Equal(0xCAFEUL, m0.ContinuationAddress);
        Assert.Equal("System.Threading.Tasks.Task", m0.ContinuationType);

        var m1 = parsed.Machines[1];
        Assert.False(m1.HasState);
        Assert.Equal("unknown", m1.Status);
        Assert.Equal(0UL, m1.ContinuationAddress);
    }
}
