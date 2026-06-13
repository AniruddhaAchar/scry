using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>
/// Lists async state machines in flight on the heap — the boxed <c>async</c> methods the runtime
/// keeps alive while they await (SOS <c>!DumpAsync</c>). The primary signal for an async hang:
/// which methods are suspended, and at which await point. Paged; candidates are pulled from the
/// cached heap snapshot, then cracked open one page at a time.
/// </summary>
public sealed class DumpAsyncCommand(int offset, int limit) : IAnalysisCommand<Page<AsyncStateMachine>>
{
    // Boxed state machines are objects whose type name contains this marker, e.g.
    // System.Runtime.CompilerServices.AsyncTaskMethodBuilder+AsyncStateMachineBox<App.Worker+<Run>d__3>.
    private const string BoxMarker = "AsyncStateMachineBox";
    private const string StateField = "<>1__state";

    public Page<AsyncStateMachine> Execute(DumpSession session, CancellationToken ct)
    {
        var heap = session.Runtime.Heap;
        var page = session.GetHeap(ct).Objects(BoxMarker, offset, limit);

        var machines = new List<AsyncStateMachine>(page.Items.Count);
        foreach (var candidate in page.Items)
        {
            ct.ThrowIfCancellationRequested();
            machines.Add(Describe(heap.GetObject(candidate.Address), candidate.Type));
        }

        return new Page<AsyncStateMachine>(page.TotalMatches, page.Truncated, machines);
    }

    private static AsyncStateMachine Describe(ClrObject box, string boxType)
    {
        var type = boxType;
        int? state = null;

        // The box wraps the compiler-generated state-machine struct in its StateMachine field; that
        // struct carries the user's async method type and the await-point counter (<>1__state).
        if (box.TryReadValueTypeField("StateMachine", out var sm) && sm.Type is not null)
        {
            type = sm.Type.Name ?? boxType;
            if (sm.Type.Fields.Any(f => f.Name == StateField))
            {
                state = sm.ReadField<int>(StateField);
            }
        }

        var status = state switch
        {
            null => "unknown",
            -2 => "completed",
            -1 => "running",
            >= 0 => $"suspended at await {state}",
            _ => "unknown",
        };

        // Best-effort single hop of the continuation chain (who resumes when this completes).
        ulong continuationAddress = 0;
        string? continuationType = null;
        if (box.TryReadObjectField("m_continuationObject", out var continuation)
            && continuation.IsValid && continuation.Address != 0)
        {
            continuationAddress = continuation.Address;
            continuationType = continuation.Type?.Name;
        }

        return new AsyncStateMachine(box.Address, type, state, status, continuationAddress, continuationType);
    }
}
