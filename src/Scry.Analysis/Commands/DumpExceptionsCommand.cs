using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>Paged live exceptions. The snapshot pages the addresses; detail is read on demand for the page only.</summary>
public sealed class DumpExceptionsCommand(int offset, int limit) : IAnalysisCommand<Page<ExceptionInfo>>
{
    private const int MaxInnerDepth = 10;

    public Page<ExceptionInfo> Execute(DumpSession session, CancellationToken ct)
    {
        var heap = session.Runtime.Heap;
        var addressPage = session.GetHeap(ct).ExceptionAddresses(offset, limit);

        var infos = new List<ExceptionInfo>(addressPage.Items.Count);
        foreach (var addr in addressPage.Items)
        {
            ct.ThrowIfCancellationRequested();
            var ex = heap.GetObject(addr).AsException();
            if (ex is null)
            {
                continue;
            }

            infos.Add(ToInfo(ex));
        }

        return new Page<ExceptionInfo>(addressPage.TotalMatches, addressPage.Truncated, infos);
    }

    internal static ExceptionInfo ToInfo(ClrException ex)
    {
        var inner = new List<ExceptionLink>();
        var cur = ex.Inner;
        var depth = 0;
        while (cur is not null && depth < MaxInnerDepth)
        {
            inner.Add(new ExceptionLink(cur.Type?.Name ?? "<unknown>", cur.Message));
            cur = cur.Inner;
            depth++;
        }

        return new ExceptionInfo(ex.Address, ex.Type?.Name ?? "<unknown>", ex.Message, ex.HResult, inner);
    }
}
