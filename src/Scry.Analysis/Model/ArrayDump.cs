namespace Scry.Analysis.Model;

/// <summary>An array's type, element type, length, and a (possibly truncated) page of elements.</summary>
public sealed record ArrayDump(
    ulong Address,
    string Type,
    string ElementType,
    int Length,
    bool Truncated,
    IReadOnlyList<ArrayElement> Elements);
