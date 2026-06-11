namespace Scry.Analysis.Model;

/// <summary>One frame of a managed stack walk.</summary>
public sealed record StackFrame(
    string Kind,
    ulong InstructionPointer,
    ulong StackPointer,
    string? Method,
    string? Type,
    string? Module);
