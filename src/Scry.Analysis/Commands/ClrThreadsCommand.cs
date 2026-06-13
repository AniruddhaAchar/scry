using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>
/// Lists every managed thread with its state flags, GC mode, lock count, and current
/// exception (if any) — the triage view SOS prints as <c>!Threads</c>.
/// </summary>
public sealed class ClrThreadsCommand : IAnalysisCommand<IReadOnlyList<ThreadInfo>>
{
    public IReadOnlyList<ThreadInfo> Execute(DumpSession session, CancellationToken ct)
    {
        var threads = new List<ThreadInfo>();

        foreach (var thread in session.Runtime.Threads)
        {
            ct.ThrowIfCancellationRequested();

            ExceptionRef? current = null;
            var ex = thread.CurrentException;
            if (ex is not null)
            {
                current = new ExceptionRef(ex.Address, ex.Type?.Name ?? "<unknown>", ex.Message);
            }

            threads.Add(new ThreadInfo(
                thread.OSThreadId,
                thread.ManagedThreadId,
                thread.IsAlive,
                thread.State.HasFlag(ClrThreadState.TS_Background),
                thread.IsFinalizer,
                thread.IsGc,
                thread.GCMode.ToString(),
                thread.LockCount,
                DecodeState(thread.State),
                current));
        }

        return threads;
    }

    /// <summary>Expands the <see cref="ClrThreadState"/> bitfield into the set names that are set.</summary>
    private static IReadOnlyList<string> DecodeState(ClrThreadState state)
    {
        var names = new List<string>();
        foreach (var flag in Enum.GetValues<ClrThreadState>())
        {
            if (flag != 0 && state.HasFlag(flag))
            {
                names.Add(flag.ToString());
            }
        }

        return names;
    }
}
