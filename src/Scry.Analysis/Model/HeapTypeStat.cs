namespace Scry.Analysis.Model;

/// <summary>Per-type heap aggregate.</summary>
public sealed record HeapTypeStat(string Type, ulong MethodTable, long Count, ulong TotalSize);
