using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>
/// Walks managed thread stacks. When <paramref name="threadOsId"/> is null, every
/// thread is returned; otherwise only the thread with that OS id.
/// </summary>
public sealed class ClrStackCommand(uint? threadOsId) : IAnalysisCommand<IReadOnlyList<ThreadStack>>
{
    public IReadOnlyList<ThreadStack> Execute(ClrRuntime runtime, CancellationToken ct)
    {
        var threads = new List<ThreadStack>();

        foreach (var thread in runtime.Threads)
        {
            ct.ThrowIfCancellationRequested();
            if (threadOsId is { } id && thread.OSThreadId != id)
            {
                continue;
            }

            var frames = new List<StackFrame>();
            foreach (var frame in thread.EnumerateStackTrace())
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

            threads.Add(new ThreadStack(
                thread.OSThreadId,
                thread.ManagedThreadId,
                thread.IsAlive,
                frames));
        }

        return threads;
    }
}
