namespace Scry.Analysis.Model;

/// <summary>An object's type identity, size, and instance fields.</summary>
public sealed record ObjectDump(
    ulong Address,
    string Type,
    ulong MethodTable,
    ulong Size,
    IReadOnlyList<ObjectField> Fields);
