namespace Scry.Analysis.Model;

/// <summary>Full detail for one exception, including its reconstructed stack trace.</summary>
public sealed record ExceptionDetail(ExceptionInfo Info, IReadOnlyList<StackFrame> StackTrace);
