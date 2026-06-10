using Scry.Contracts;
using Scry.Host;

// --- Parse arguments (the dump path is needed even in M0 to derive the endpoint) ---
if (!HostArgs.TryParse(args, out var hostArgs, out var argError))
{
    Console.Error.WriteLine(argError);
    Console.Error.WriteLine("usage: scryd --dump <path> [--idle-timeout <minutes>]");
    return 2;
}

var endpointId = ScryEndpoint.DeriveId(hostArgs!.DumpPath);

var builder = WebApplication.CreateSlimBuilder();

// Logs go to stderr so stdout stays clean for any future structured output.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

ScryListener.Configure(builder.WebHost, endpointId);

builder.Services.AddGrpc();
builder.Services.AddSingleton<HostState>();
builder.Services.AddSingleton<ActivityTracker>();
builder.Services.AddSingleton<IHostedService>(sp => new IdleShutdownService(
    hostArgs.IdleTimeout,
    sp.GetRequiredService<ActivityTracker>(),
    sp.GetRequiredService<IHostApplicationLifetime>(),
    sp.GetRequiredService<ILogger<IdleShutdownService>>()));

var app = builder.Build();
app.MapGrpcService<ScryServiceImpl>();

var state = app.Services.GetRequiredService<HostState>();
var log = app.Services.GetRequiredService<ILogger<Program>>();

// M0: no dump load yet. From M1, DataTarget.LoadDump + DAC resolution happen
// here and the host stays LOADING until the runtime is open.
state.MarkReady(runtimeVersion: string.Empty, detail: "M0 skeleton: no runtime loaded");
log.LogInformation("scryd ready on endpoint {Endpoint} for dump {Dump}", endpointId, hostArgs.DumpPath);

app.Run();
return 0;
