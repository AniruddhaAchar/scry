using Microsoft.AspNetCore.Server.Kestrel.Core;
using Scry.Contracts;

namespace Scry.Host;

/// <summary>
/// Binds Kestrel to the dump-derived endpoint: a named pipe on Windows, a Unix
/// domain socket elsewhere. Both serve HTTP/2 cleartext (h2c) — there is no TLS
/// because access is scoped by filesystem permissions, not the network.
/// </summary>
internal static class ScryListener
{
    public static void Configure(IWebHostBuilder webHost, string endpointId)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipeName = ScryEndpoint.PipeName(endpointId);
            webHost.UseNamedPipes();
            webHost.ConfigureKestrel(options =>
                options.ListenNamedPipe(pipeName, listen => listen.Protocols = HttpProtocols.Http2));
        }
        else
        {
            var socketPath = ScryEndpoint.SocketPath(endpointId);

            // A leftover socket file from a crashed host would block bind().
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }

            webHost.ConfigureKestrel(options =>
                options.ListenUnixSocket(socketPath, listen => listen.Protocols = HttpProtocols.Http2));
        }
    }
}
