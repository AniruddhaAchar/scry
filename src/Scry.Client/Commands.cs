using System.Net.Sockets;
using Grpc.Core;
using Scry.Contracts;
using Scry.Contracts.V1;
using ScryGrpc = Scry.Contracts.V1.Scry;

namespace Scry.Client;

/// <summary>Implements the CLI verbs. M0: <c>health</c> and <c>shutdown</c>.</summary>
internal static class Commands
{
    public static async Task<int> HealthAsync(string dumpPath, int timeoutSeconds, CancellationToken ct)
    {
        var endpointId = ScryEndpoint.DeriveId(dumpPath);
        try
        {
            using var channel = ScryChannel.ForDump(dumpPath);
            var client = new ScryGrpc.ScryClient(channel);
            using var cts = LinkTimeout(ct, timeoutSeconds);

            var response = await client.HealthAsync(new HealthRequest(), cancellationToken: cts.Token);

            JsonOut.Write(new
            {
                endpoint = endpointId,
                state = response.State.ToString().ToUpperInvariant(),
                runtimeVersion = response.RuntimeVersion,
                detail = response.Detail,
            });
            return 0;
        }
        catch (Exception ex)
        {
            return JsonOut.WriteError(ConnectError(ex, dumpPath, endpointId));
        }
    }

    public static async Task<int> ShutdownAsync(string dumpPath, int timeoutSeconds, CancellationToken ct)
    {
        var endpointId = ScryEndpoint.DeriveId(dumpPath);
        try
        {
            using var channel = ScryChannel.ForDump(dumpPath);
            var client = new ScryGrpc.ScryClient(channel);
            using var cts = LinkTimeout(ct, timeoutSeconds);

            await client.ShutdownAsync(new ShutdownRequest(), cancellationToken: cts.Token);

            JsonOut.Write(new { endpoint = endpointId, shutdown = "requested" });
            return 0;
        }
        catch (Exception ex)
        {
            return JsonOut.WriteError(ConnectError(ex, dumpPath, endpointId));
        }
    }

    private static CancellationTokenSource LinkTimeout(CancellationToken ct, int seconds)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (seconds > 0)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        return cts;
    }

    private static CliError ConnectError(Exception ex, string dumpPath, string endpointId)
    {
        var rpc = ex as RpcException;

        // A genuine application status from a *running* host (e.g. INVALID_ARGUMENT,
        // NOT_FOUND, FAILED_PRECONDITION) is worth surfacing verbatim. A transport
        // failure — the host isn't listening — is not; map it to a helpful hint.
        if (rpc is not null && !IsTransportFailure(rpc))
        {
            return new CliError(rpc.StatusCode.ToString().ToUpperInvariant(), rpc.Status.Detail);
        }

        // The host almost certainly isn't running yet (M1 adds spawn-on-miss).
        return new CliError(
            Code: "UNAVAILABLE",
            Message: $"no scryd host is reachable for this dump (endpoint {endpointId})",
            Hint: $"start one with: scryd --dump \"{dumpPath}\"");
    }

    private static bool IsTransportFailure(RpcException rpc) =>
        rpc.StatusCode is StatusCode.Unavailable ||
        rpc.Status.DebugException is HttpRequestException or IOException or SocketException or TimeoutException;
}
