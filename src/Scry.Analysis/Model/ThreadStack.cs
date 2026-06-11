namespace Scry.Analysis.Model;

/// <summary>A single thread and its walked frames.</summary>
public sealed record ThreadStack(
    uint OsThreadId,
    int ManagedThreadId,
    bool IsAlive,
    IReadOnlyList<StackFrame> Frames);
