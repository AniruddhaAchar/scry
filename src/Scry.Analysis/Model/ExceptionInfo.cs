namespace Scry.Analysis.Model;

/// <summary>An exception object's surface detail (no stack trace).</summary>
public sealed record ExceptionInfo(
    ulong Address,
    string Type,
    string? Message,
    int HResult,
    IReadOnlyList<ExceptionLink> Inner);
