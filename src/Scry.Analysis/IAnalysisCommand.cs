using Microsoft.Diagnostics.Runtime;

namespace Scry.Analysis;

/// <summary>
/// A unit of dump analysis. Implementations run ON the single analysis thread
/// (see <see cref="AnalysisWorker"/>) and may freely touch ClrMD; they must not
/// be invoked directly from gRPC handlers. ClrMD/DAC is not thread-safe — see
/// docs/adr/0003-single-threaded-analysis-worker.md.
/// </summary>
public interface IAnalysisCommand<out TResult>
{
    TResult Execute(ClrRuntime runtime, CancellationToken ct);
}
