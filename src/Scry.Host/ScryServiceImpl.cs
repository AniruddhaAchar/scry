using Grpc.Core;
using Scry.Analysis;
using Scry.Analysis.Commands;
using Scry.Contracts.V1;
using ScryGrpc = Scry.Contracts.V1.Scry;

namespace Scry.Host;

/// <summary>
/// gRPC surface of the host. M0 implements Health and Shutdown only; analysis
/// RPCs land from M2 onward and will enqueue work onto the single analysis
/// worker rather than touching ClrMD inline.
/// </summary>
public sealed class ScryServiceImpl(
    HostState state,
    ActivityTracker activity,
    AnalysisWorker worker,
    IHostApplicationLifetime lifetime,
    ILogger<ScryServiceImpl> logger) : ScryGrpc.ScryBase
{
    public override Task<HealthResponse> Health(HealthRequest request, ServerCallContext context)
    {
        activity.Touch();
        return Task.FromResult(state.Snapshot());
    }

    public override Task<ShutdownResponse> Shutdown(ShutdownRequest request, ServerCallContext context)
    {
        activity.Touch();
        logger.LogInformation("Shutdown requested via RPC");
        lifetime.StopApplication();
        return Task.FromResult(new ShutdownResponse());
    }

    public override async Task<ClrStackResponse> ClrStack(ClrStackRequest request, ServerCallContext context)
    {
        activity.Touch();

        var snapshot = state.Snapshot();
        if (snapshot.State != HealthResponse.Types.State.Ready)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, $"runtime not loaded: {snapshot.Detail}"));
        }

        // thread_os_id == 0 or all_threads ⇒ every thread.
        uint? threadFilter = request.AllThreads || request.ThreadOsId == 0
            ? null
            : request.ThreadOsId;

        var threads = await worker.RunAsync(new ClrStackCommand(threadFilter), context.CancellationToken);

        var response = new ClrStackResponse();
        foreach (var t in threads)
        {
            var protoThread = new ThreadStack
            {
                OsThreadId = t.OsThreadId,
                ManagedThreadId = t.ManagedThreadId,
                IsAlive = t.IsAlive,
            };
            foreach (var f in t.Frames)
            {
                protoThread.Frames.Add(new StackFrame
                {
                    Kind = f.Kind,
                    InstructionPointer = f.InstructionPointer,
                    StackPointer = f.StackPointer,
                    Method = f.Method ?? string.Empty,
                    Type = f.Type ?? string.Empty,
                    Module = f.Module ?? string.Empty,
                });
            }

            response.Threads.Add(protoThread);
        }

        return response;
    }
}
