namespace Scry.Analysis.Model;

/// <summary>A single heap object: address, type name, and size in bytes.</summary>
public sealed record HeapObject(ulong Address, string Type, ulong Size);
