using Scry.Analysis;
using Scry.Contracts;
using Scry.Core;

namespace Scry.Host;

/// <summary>
/// The daemon ("scryd") entry point, now a mode of the single `scry` binary,
/// invoked as `scry __host --dump ...`. See docs/adr/0007.
/// </summary>
public static class HostMode
{
    public static async Task<int> RunAsync(string[] args)
    {
        // --- Parse arguments ---
        if (!HostArgs.TryParse(args, out var hostArgs, out var argError))
        {
            Console.Error.WriteLine(argError);
            Console.Error.WriteLine("usage: scry __host --dump <path> [--idle-timeout <minutes>] [--verbose]");
            return 2;
        }

        var endpointId = ScryEndpoint.DeriveId(hostArgs!.DumpPath);

        var builder = WebApplication.CreateSlimBuilder();

        // Resolve logging config: always keep stderr console; add file logger too.
        var cfg = ScryConfig.Load();
        var resolved = ScryLogging.Resolve("scryd", hostArgs.Verbose, cfg);

        // Honor a configured symbol path (e.g. "srv*C:\\sym*https://msdl.microsoft.com/download/symbols").
        // ClrMD's DataTarget reads _NT_SYMBOL_PATH when resolving the DAC (ADR 0008).
        var symbolPath = cfg.Symbols?.Path;
        if (!string.IsNullOrEmpty(symbolPath))
        {
            Environment.SetEnvironmentVariable("_NT_SYMBOL_PATH", symbolPath);
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        builder.Logging.AddScryFile(resolved);

        ScryListener.Configure(builder.WebHost, endpointId);

        builder.Services.AddGrpc();
        builder.Services.AddSingleton<HostState>();
        builder.Services.AddSingleton<ActivityTracker>();
        builder.Services.AddSingleton(new AnalysisWorker(Path.GetFullPath(hostArgs.DumpPath)));
        builder.Services.AddSingleton<IHostedService>(sp => new IdleShutdownService(
            hostArgs.IdleTimeout,
            sp.GetRequiredService<ActivityTracker>(),
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<ILogger<IdleShutdownService>>()));

        var app = builder.Build();
        app.MapGrpcService<ScryServiceImpl>();

        var state = app.Services.GetRequiredService<HostState>();
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("scryd");
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var worker = app.Services.GetRequiredService<AnalysisWorker>();

        // Register/unregister session descriptor around the host's lifetime.
        var descriptor = new SessionDescriptor(
            endpointId,
            Path.GetFullPath(hostArgs.DumpPath),
            Environment.ProcessId,
            DateTimeOffset.UtcNow,
            ScryVersion.Current);

        lifetime.ApplicationStarted.Register(() => ScrySessions.Register(descriptor));
        lifetime.ApplicationStopped.Register(() => ScrySessions.Unregister(endpointId));

        // Load the dump on the analysis thread once the host is up. The host stays
        // in LOADING (then READY or FAILED) so the client can poll Health; a failed
        // load keeps the process alive to report FAILED rather than vanishing.
        lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            var result = await worker.LoadAsync();
            if (result.Success)
            {
                state.MarkReady(result.RuntimeVersion, result.Detail);
                log.LogInformation("scryd ready on {Endpoint}: runtime {Version}", endpointId, result.RuntimeVersion);
            }
            else
            {
                state.MarkFailed(result.Detail);
                log.LogError("scryd dump load FAILED on {Endpoint}: {Detail}", endpointId, result.Detail);
            }
        }));

        lifetime.ApplicationStopped.Register(() => worker.Dispose());

        await app.RunAsync();
        return 0;
    }
}
