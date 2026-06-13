namespace Scry.Analysis.Model;

/// <summary>
/// One managed sync block — the lock/monitor record behind a <c>lock(obj)</c> (SOS !SyncBlk).
/// The owning-thread ids are resolved when the holding thread is found in the runtime's thread list.
/// </summary>
public sealed record SyncBlockInfo(
    int Index,
    ulong ObjectAddress,
    string? ObjectType,
    bool MonitorHeld,
    ulong OwningThreadAddress,
    uint? OwningOsThreadId,
    int? OwningManagedThreadId,
    int RecursionCount,
    int WaitingThreadCount);
