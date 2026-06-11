using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>Paged listing of heap objects whose type name contains <paramref name="typeFilter"/>.</summary>
public sealed class DumpHeapObjectsCommand(string typeFilter, int offset, int limit)
    : IAnalysisCommand<Page<HeapObject>>
{
    public Page<HeapObject> Execute(DumpSession session, CancellationToken ct) =>
        session.GetHeap(ct).Objects(typeFilter, offset, limit);
}
