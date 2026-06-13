namespace Scry.Analysis.Model;

/// <summary>
/// One async state machine found on the heap (a boxed <c>async</c> method in flight — SOS !DumpAsync).
/// <see cref="State"/> is the compiler's <c>&lt;&gt;1__state</c> field: -1 = running/not-started,
/// -2 = completed, &gt;= 0 = suspended at that await point. <see cref="Status"/> renders that for agents.
/// </summary>
public sealed record AsyncStateMachine(
    ulong Address,
    string Type,
    int? State,
    string Status,
    ulong ContinuationAddress,
    string? ContinuationType);
