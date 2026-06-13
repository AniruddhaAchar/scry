namespace Scry.Analysis.Model;

/// <summary>One managed thread's surface state (SOS !Threads analog).</summary>
public sealed record ThreadInfo(
    uint OsThreadId,
    int ManagedThreadId,
    bool IsAlive,
    bool IsBackground,
    bool IsFinalizer,
    bool IsGc,
    string GcMode,
    uint LockCount,
    IReadOnlyList<string> State,
    ExceptionRef? CurrentException);

/// <summary>A shallow reference to an exception object (no stack trace / inner chain).</summary>
public sealed record ExceptionRef(ulong Address, string Type, string? Message);
