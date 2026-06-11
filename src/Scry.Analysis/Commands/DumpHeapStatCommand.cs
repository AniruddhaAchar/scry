using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>Per-type heap statistics, optionally filtered by a case-sensitive substring.</summary>
public sealed class DumpHeapStatCommand(string? typeFilter) : IAnalysisCommand<IReadOnlyList<HeapTypeStat>>
{
    public IReadOnlyList<HeapTypeStat> Execute(DumpSession session, CancellationToken ct) =>
        session.GetHeap(ct).Stat(typeFilter);
}
