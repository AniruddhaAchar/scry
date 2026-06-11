using Scry.Analysis;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class AnalysisWorkerTests
{
    private sealed class NoopCommand : IAnalysisCommand<int>
    {
        public int Execute(DumpSession session, CancellationToken ct) => 0;
    }

    [Fact]
    public async Task LoadAsync_NonexistentPath_ReturnsFailure()
    {
        using var w = new AnalysisWorker(@"C:\does\not\exist\nope.dmp");
        var r = await w.LoadAsync();
        Assert.False(r.Success);
        Assert.NotNull(r.Detail);
        Assert.NotEmpty(r.Detail);
    }

    [Fact]
    public async Task RunAsync_BeforeLoad_Throws()
    {
        using var w = new AnalysisWorker(@"C:\whatever.dmp");
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.RunAsync(new NoopCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_CancelledToken_Cancels()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var w = new AnalysisWorker(@"C:\whatever.dmp");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => w.RunAsync(new NoopCommand(), cts.Token));
    }

    [Fact]
    public void Dispose_WithoutLoad_DoesNotThrow()
    {
        var w = new AnalysisWorker(@"C:\whatever.dmp");
        w.Dispose();
        // No exception thrown — test passes.
    }
}
