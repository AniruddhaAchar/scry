using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;
using Grpc.Net.Client;
using Scry.Contracts;

namespace Scry.Client;

/// <summary>
/// Opens a gRPC channel to a scryd host over its dump-derived endpoint — a named
/// pipe on Windows, a Unix domain socket elsewhere — by handing Kestrel's HTTP/2
/// stack a pre-dialed stream via <see cref="SocketsHttpHandler.ConnectCallback"/>.
/// </summary>
internal static class ScryChannel
{
    // Bounds how long we wait for the host's named pipe to appear. Without this,
    // a missing host blocks until the RPC deadline and surfaces as CANCELLED
    // instead of a clean "host not running" (UNAVAILABLE). M1's spawn-on-miss
    // creates the pipe well within this window.
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(2);

    public static GrpcChannel ForDump(string dumpPath)
    {
        var endpointId = ScryEndpoint.DeriveId(dumpPath);

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (_, ct) => ConnectAsync(endpointId, ct),
            EnableMultipleHttp2Connections = true,
        };

        // The address host/port is a placeholder: the real connection is the
        // stream from ConnectCallback. http:// selects HTTP/2 cleartext.
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
    }

    private static async ValueTask<Stream> ConnectAsync(string endpointId, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(
                serverName: ".",
                pipeName: ScryEndpoint.PipeName(endpointId),
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                TokenImpersonationLevel.Anonymous);
            try
            {
                await pipe.ConnectAsync(PipeConnectTimeout, ct);
                return pipe;
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(ScryEndpoint.SocketPath(endpointId)), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
