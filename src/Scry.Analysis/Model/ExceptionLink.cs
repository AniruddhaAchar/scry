namespace Scry.Analysis.Model;

/// <summary>One link in an inner-exception chain.</summary>
public sealed record ExceptionLink(string Type, string? Message);
