namespace Scry.Analysis.Model;

/// <summary>One instance field of an object: name, declared type, byte offset, and display value.</summary>
public sealed record ObjectField(string Name, string Type, int Offset, string? Value);
