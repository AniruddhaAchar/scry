using Google.Protobuf;
using Scry.Contracts.V1;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ThreadsContractTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ClrThreadsResponse_RoundTrips()
    {
        var thread = new ThreadInfo
        {
            OsThreadId = 4242,
            ManagedThreadId = 1,
            IsAlive = true,
            IsBackground = true,
            IsFinalizer = false,
            IsGc = false,
            GcMode = "Preemptive",
            LockCount = 2,
            State = { "TS_Background", "TS_CompletionPortThread" },
            CurrentException = new ExceptionRef
            {
                Address = 0xCAFEBABEUL,
                Type = "System.InvalidOperationException",
                Message = "boom",
            },
        };

        var resp = new ClrThreadsResponse { Threads = { thread } };

        var bytes = resp.ToByteArray();
        var parsed = ClrThreadsResponse.Parser.ParseFrom(bytes);

        Assert.Single(parsed.Threads);
        var t = parsed.Threads[0];
        Assert.Equal(4242U, t.OsThreadId);
        Assert.Equal(1, t.ManagedThreadId);
        Assert.True(t.IsAlive);
        Assert.True(t.IsBackground);
        Assert.Equal("Preemptive", t.GcMode);
        Assert.Equal(2U, t.LockCount);
        Assert.Equal(new[] { "TS_Background", "TS_CompletionPortThread" }, t.State);
        Assert.NotNull(t.CurrentException);
        Assert.Equal(0xCAFEBABEUL, t.CurrentException.Address);
        Assert.Equal("System.InvalidOperationException", t.CurrentException.Type);
        Assert.Equal("boom", t.CurrentException.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ClrThreadsResponse_NoCurrentException_RoundTrips()
    {
        var resp = new ClrThreadsResponse
        {
            Threads = { new ThreadInfo { OsThreadId = 1, GcMode = "Cooperative" } },
        };

        var parsed = ClrThreadsResponse.Parser.ParseFrom(resp.ToByteArray());

        Assert.Null(parsed.Threads[0].CurrentException);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GcRootResponse_RoundTrips()
    {
        var path = new GcRootPath
        {
            RootKind = "Stack",
            RootAddress = 0xDECADEUL,
            StackFrame = "Program.Main()",
            Chain =
            {
                new GcRootNode { Address = 0xDECADEUL, Type = "System.Object[]" },
                new GcRootNode { Address = 0xBEEFUL, Type = "Leaky.Cache" },
            },
        };

        var resp = new GcRootResponse
        {
            Found = true,
            Target = 0xBEEFUL,
            Rooted = true,
            Truncated = true,
            Roots = { path },
        };

        var parsed = GcRootResponse.Parser.ParseFrom(resp.ToByteArray());

        Assert.True(parsed.Found);
        Assert.Equal(0xBEEFUL, parsed.Target);
        Assert.True(parsed.Rooted);
        Assert.True(parsed.Truncated);
        Assert.Single(parsed.Roots);

        var p = parsed.Roots[0];
        Assert.Equal("Stack", p.RootKind);
        Assert.Equal(0xDECADEUL, p.RootAddress);
        Assert.Equal("Program.Main()", p.StackFrame);
        Assert.Equal(2, p.Chain.Count);
        Assert.Equal(0xDECADEUL, p.Chain[0].Address);
        Assert.Equal("System.Object[]", p.Chain[0].Type);
        Assert.Equal("Leaky.Cache", p.Chain[1].Type);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GcRootResponse_NotFound_RoundTrips()
    {
        var resp = new GcRootResponse { Found = false };

        var parsed = GcRootResponse.Parser.ParseFrom(resp.ToByteArray());

        Assert.False(parsed.Found);
        Assert.Empty(parsed.Roots);
    }
}
