using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>
/// Finds GC root paths that keep <paramref name="address"/> alive — the "why is this object
/// not collected / what's leaking" query (SOS !GCRoot). Returns <c>null</c> for an invalid
/// address; a valid-but-unrooted object yields <see cref="GcRootResult.Rooted"/> = false.
/// </summary>
/// <remarks>
/// ClrMD's <see cref="GCRoot"/> builds a reverse object graph over the whole heap, so this is
/// the most expensive command we expose. Enumeration is lazy and stops once
/// <paramref name="maxPaths"/> paths are collected (default 1 — one path is usually enough to
/// explain rooting). 0x0FACE15 hint: raise --max-paths only when you need every retainer.
/// </remarks>
public sealed class GcRootCommand(ulong address, int maxPaths) : IAnalysisCommand<GcRootResult?>
{
    public GcRootResult? Execute(DumpSession session, CancellationToken ct)
    {
        var heap = session.Runtime.Heap;

        var target = heap.GetObject(address);
        if (!target.IsValid || target.Type is null)
        {
            return null;
        }

        var cap = maxPaths <= 0 ? 1 : maxPaths;
        var gcRoot = new GCRoot(heap, new[] { address });

        var paths = new List<GcRootPath>();
        var truncated = false;

        foreach (var (root, link) in gcRoot.EnumerateRootPaths(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (paths.Count >= cap)
            {
                truncated = true;
                break;
            }

            var chain = new List<GcRootNode>();
            for (var node = link; node is not null; node = node.Next)
            {
                ct.ThrowIfCancellationRequested();
                var type = heap.GetObject(node.Object).Type?.Name;
                chain.Add(new GcRootNode(node.Object, type));
            }

            var stackFrame = (root as ClrStackRoot)?.StackFrame?.ToString();
            paths.Add(new GcRootPath(root.RootKind.ToString(), root.Address, stackFrame, chain));
        }

        return new GcRootResult(address, paths.Count > 0, truncated, paths);
    }
}
