using System.Collections.Concurrent;

namespace Scry.Analysis;

/// <summary>Outcome of loading a dump.</summary>
public sealed record LoadResult(bool Success, string RuntimeVersion, string Detail);

/// <summary>
/// The single dedicated thread through which ALL ClrMD access is serialized
/// (ClrMD/DAC is not thread-safe — docs/adr/0003). gRPC handlers enqueue work via
/// <see cref="LoadAsync"/> / <see cref="RunAsync{T}"/> and await the result; they
/// never touch ClrMD themselves. The dump is loaded on this same thread for DAC affinity.
/// </summary>
public sealed class AnalysisWorker : IDisposable
{
    private readonly string _dumpPath;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private DumpSession? _session;

    public AnalysisWorker(string dumpPath)
    {
        _dumpPath = dumpPath;
        _thread = new Thread(Pump) { IsBackground = true, Name = "scry-analysis" };
        _thread.Start();
    }

    private void Pump()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }

    /// <summary>Loads the dump on the analysis thread. Never throws — failures come back in the result.</summary>
    public Task<LoadResult> LoadAsync()
    {
        var tcs = new TaskCompletionSource<LoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                var session = new DumpSession();
                session.Load(_dumpPath);
                _session = session;
                tcs.SetResult(new LoadResult(true, session.RuntimeVersion, "runtime loaded"));
            }
            catch (Exception ex)
            {
                tcs.SetResult(new LoadResult(false, string.Empty, ex.Message));
            }
        });
        return tcs.Task;
    }

    /// <summary>Runs an analysis command on the analysis thread once the dump is loaded.</summary>
    public Task<T> RunAsync<T>(IAnalysisCommand<T> command, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.SetCanceled(ct);
                return;
            }

            try
            {
                var session = _session ?? throw new InvalidOperationException("runtime not loaded");
                tcs.SetResult(command.Execute(session, ct));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
        _session?.Dispose();
        _queue.Dispose();
    }
}
