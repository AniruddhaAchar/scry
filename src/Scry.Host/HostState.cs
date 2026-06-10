using Scry.Contracts.V1;

namespace Scry.Host;

/// <summary>
/// Thread-safe snapshot of the host's readiness, surfaced by the Health RPC.
/// In M0 the host transitions straight to <see cref="State.Ready"/>; from M1 it
/// stays <see cref="State.Loading"/> until the dump and DAC are resolved.
/// </summary>
public sealed class HostState
{
    private readonly Lock _gate = new();
    private HealthResponse.Types.State _state = HealthResponse.Types.State.Loading;
    private string _runtimeVersion = string.Empty;
    private string _detail = "starting up";

    public HealthResponse Snapshot()
    {
        lock (_gate)
        {
            return new HealthResponse
            {
                State = _state,
                RuntimeVersion = _runtimeVersion,
                Detail = _detail,
            };
        }
    }

    public void MarkReady(string runtimeVersion, string detail)
    {
        lock (_gate)
        {
            _state = HealthResponse.Types.State.Ready;
            _runtimeVersion = runtimeVersion;
            _detail = detail;
        }
    }

    public void MarkFailed(string detail)
    {
        lock (_gate)
        {
            _state = HealthResponse.Types.State.Failed;
            _detail = detail;
        }
    }
}
