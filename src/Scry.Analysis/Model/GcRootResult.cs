namespace Scry.Analysis.Model;

/// <summary>The set of GC root paths keeping a target object alive (SOS !GCRoot analog).</summary>
public sealed record GcRootResult(
    ulong Target,
    bool Rooted,
    bool Truncated,
    IReadOnlyList<GcRootPath> Roots);

/// <summary>One path from a GC root down to the target object.</summary>
public sealed record GcRootPath(
    string RootKind,
    ulong RootAddress,
    string? StackFrame,
    IReadOnlyList<GcRootNode> Chain);

/// <summary>One object on a root path (the chain runs root → ... → target).</summary>
public sealed record GcRootNode(ulong Address, string? Type);
