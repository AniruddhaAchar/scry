using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>
/// Lists managed sync blocks (the records behind <c>lock</c>/<c>Monitor</c>), with the owning
/// thread, recursion depth, and waiter count — the view SOS prints as <c>!SyncBlk</c>. This is
/// the primary signal for monitor-based deadlocks.
/// </summary>
public sealed class SyncBlkCommand : IAnalysisCommand<IReadOnlyList<SyncBlockInfo>>
{
    public IReadOnlyList<SyncBlockInfo> Execute(DumpSession session, CancellationToken ct)
    {
        var runtime = session.Runtime;
        var heap = runtime.Heap;

        // Map the runtime thread-object address → thread so we can name the monitor owner.
        var threadByAddress = new Dictionary<ulong, (uint OsId, int ManagedId)>();
        foreach (var thread in runtime.Threads)
        {
            threadByAddress[thread.Address] = (thread.OSThreadId, thread.ManagedThreadId);
        }

        var blocks = new List<SyncBlockInfo>();
        foreach (var sb in heap.EnumerateSyncBlocks())
        {
            ct.ThrowIfCancellationRequested();

            // Only surface blocks acting as a live monitor. Sync blocks allocated for other
            // reasons (hashcode, COM interop) carry uninitialized monitor fields — e.g. a bogus
            // waiter count — that would mislead an agent. This mirrors SOS !SyncBlk's default.
            if (!sb.IsMonitorHeld && sb.HoldingThreadAddress == 0)
            {
                continue;
            }

            var objectType = sb.Object != 0 ? heap.GetObject(sb.Object).Type?.Name : null;

            uint? ownerOsId = null;
            int? ownerManagedId = null;
            if (sb.HoldingThreadAddress != 0 && threadByAddress.TryGetValue(sb.HoldingThreadAddress, out var owner))
            {
                ownerOsId = owner.OsId;
                ownerManagedId = owner.ManagedId;
            }

            blocks.Add(new SyncBlockInfo(
                sb.Index,
                sb.Object,
                objectType,
                sb.IsMonitorHeld,
                sb.HoldingThreadAddress,
                ownerOsId,
                ownerManagedId,
                sb.RecursionCount,
                sb.WaitingThreadCount));
        }

        return blocks;
    }
}
