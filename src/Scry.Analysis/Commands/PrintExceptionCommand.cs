using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>Full detail for one exception object by address, including its reconstructed stack trace.</summary>
public sealed class PrintExceptionCommand(ulong address) : IAnalysisCommand<ExceptionDetail?>
{
    public ExceptionDetail? Execute(DumpSession session, CancellationToken ct)
    {
        // Guard before AsException(): for a non-exception (or invalid) object ClrMD
        // throws rather than returning null, so we'd surface a gRPC handler fault
        // instead of a clean "found: false".
        var obj = session.Runtime.Heap.GetObject(address);
        if (!obj.IsValid || !obj.IsException)
        {
            return null;
        }

        var ex = obj.AsException();
        if (ex is null)
        {
            return null;
        }

        var frames = new List<StackFrame>();
        foreach (var frame in ex.StackTrace)
        {
            ct.ThrowIfCancellationRequested();
            frames.Add(new StackFrame(
                frame.Kind.ToString(),
                frame.InstructionPointer,
                frame.StackPointer,
                frame.Method?.Name ?? frame.FrameName,
                frame.Method?.Type?.Name,
                frame.Method?.Type?.Module?.Name));
        }

        return new ExceptionDetail(DumpExceptionsCommand.ToInfo(ex), frames);
    }
}
