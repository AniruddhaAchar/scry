namespace Scry.Analysis;

/// <summary>
/// A unit of dump analysis. Implementations run ON the single analysis thread
/// (see <see cref="AnalysisWorker"/>) and may freely touch ClrMD via the supplied
/// <see cref="DumpSession"/> (its <c>Runtime</c> and cached <c>Heap</c> snapshot);
/// they must not be invoked directly from gRPC handlers. See
/// docs/adr/0003-single-threaded-analysis-worker.md.
/// </summary>
public interface IAnalysisCommand<out TResult>
{
    TResult Execute(DumpSession session, CancellationToken ct);
}
