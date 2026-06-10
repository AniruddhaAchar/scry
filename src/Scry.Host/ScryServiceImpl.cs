using Grpc.Core;
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
}
