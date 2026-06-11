using System.Diagnostics;
using System.Net.Sockets;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Scry.Contracts;
using Scry.Contracts.V1;
using ScryGrpc = Scry.Contracts.V1.Scry;

namespace Scry.Client;

/// <summary>Implements the CLI verbs as instance methods, with ILogger injected.</summary>
internal sealed class ScryCommands(ILogger<ScryCommands> logger)
{
    // -------------------------------------------------------------------------
    // analyze
    // -------------------------------------------------------------------------

    public async Task<int> AnalyzeAsync(
        string dumpPath,
        int idleTimeoutMin,
        int readyTimeoutSec,
        bool verbose,
        CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(dumpPath);
        if (!File.Exists(fullPath))
        {
            logger.LogWarning("Dump not found: {Path}", fullPath);
            return JsonOut.WriteError(new CliError("NOT_FOUND", $"dump file not found: {fullPath}"));
        }

        var handle = ScryEndpoint.DeriveId(fullPath);
        logger.LogInformation("analyze: dump={Dump} handle={Handle}", fullPath, handle);

        var sessions = ScrySessions.List();

        // Idempotent: same dump already live.
        var existing = sessions.FirstOrDefault(s => s.Handle == handle);
        if (existing is not null)
        {
            logger.LogInformation("Session already running: {Handle}", handle);
            JsonOut.Write(new
            {
                handle = existing.Handle,
                dumpPath = existing.DumpPath,
                pid = existing.Pid,
                status = "already-running",
            });
            return 0;
        }

        // Different dump already live → refuse.
        if (sessions.Count > 0)
        {
            var running = sessions[0];
            logger.LogWarning("Another session is already running: {Handle}", running.Handle);
            return JsonOut.WriteError(new CliError(
                "FAILED_PRECONDITION",
                $"a scryd session is already running for a different dump (handle: {running.Handle}, dump: {running.DumpPath})",
                $"stop it first: scry stop {running.Handle}"));
        }

        // Locate scryd binary.
        var scrydPath = FindScryd();
        if (scrydPath is null)
        {
            logger.LogError("scryd binary not found");
            return JsonOut.WriteError(new CliError(
                "NOT_FOUND",
                "scryd binary not found",
                "set SCRYD_PATH env var or ensure scryd is next to scry"));
        }

        logger.LogInformation("Spawning scryd: {Path} --dump {Dump} --idle-timeout {Idle}", scrydPath, fullPath, idleTimeoutMin);

        var psi = new ProcessStartInfo(scrydPath)
        {
            UseShellExecute = false,   // run the binary directly (no shell stream sharing)
            CreateNoWindow = true,     // no console window for the detached daemon

            // Leave the standard streams un-redirected: scry does not pump the
            // daemon's output. Inheritance of scry's own handles is blocked
            // separately via StdHandle.MakeNonInheritable() below so a caller
            // that pipes `scry analyze` doesn't hang on the detached daemon.
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };
        psi.ArgumentList.Add("--dump");
        psi.ArgumentList.Add(fullPath);
        psi.ArgumentList.Add("--idle-timeout");
        psi.ArgumentList.Add(idleTimeoutMin.ToString());
        if (verbose)
        {
            psi.ArgumentList.Add("--verbose");
        }

        Process proc;
        try
        {
            // Prevent the detached daemon from inheriting our standard handles, so
            // a caller reading scry's stdout to EOF doesn't hang (see StdHandle).
            StdHandle.MakeNonInheritable();
            proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to spawn scryd");
            return JsonOut.WriteError(new CliError("UNAVAILABLE", $"failed to spawn scryd: {ex.Message}"));
        }

        // Poll Health until READY or timeout.
        var deadline = DateTime.UtcNow.AddSeconds(readyTimeoutSec);
        logger.LogInformation("Waiting for scryd READY (timeout {Sec}s) ...", readyTimeoutSec);

        while (DateTime.UtcNow < deadline)
        {
            if (proc.HasExited)
            {
                logger.LogError("scryd exited prematurely (exit code {Code})", proc.ExitCode);
                return JsonOut.WriteError(new CliError(
                    "UNAVAILABLE",
                    $"scryd exited prematurely (exit code {proc.ExitCode})"));
            }

            try
            {
                using var channel = ScryChannel.ForEndpoint(handle);
                var client = new ScryGrpc.ScryClient(channel);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                var response = await client.HealthAsync(new HealthRequest(), cancellationToken: cts.Token);
                if (response.State == HealthResponse.Types.State.Ready)
                {
                    logger.LogInformation("scryd READY (pid={Pid})", proc.Id);
                    JsonOut.Write(new
                    {
                        handle,
                        dumpPath = fullPath,
                        pid = proc.Id,
                        state = "READY",
                        runtimeVersion = response.RuntimeVersion,
                    });
                    return 0;
                }
            }
            catch (Exception ex) when (ex is RpcException or OperationCanceledException or TaskCanceledException)
            {
                // Not ready yet; retry after delay.
            }

            await Task.Delay(200, ct);
        }

        logger.LogError("Timed out waiting for scryd READY after {Sec}s", readyTimeoutSec);
        return JsonOut.WriteError(new CliError(
            "DEADLINE_EXCEEDED",
            $"timed out waiting for scryd to become READY after {readyTimeoutSec}s"));
    }

    // -------------------------------------------------------------------------
    // ps
    // -------------------------------------------------------------------------

    public Task<int> PsAsync(CancellationToken ct)
    {
        var sessions = ScrySessions.List();
        logger.LogInformation("ps: {Count} session(s)", sessions.Count);
        JsonOut.Write(new
        {
            sessions = sessions.Select(s => new
            {
                handle = s.Handle,
                dumpPath = s.DumpPath,
                pid = s.Pid,
                startedUtc = s.StartedUtc,
            }).ToArray(),
        });
        return Task.FromResult(0);
    }

    // -------------------------------------------------------------------------
    // health
    // -------------------------------------------------------------------------

    public async Task<int> HealthAsync(
        string? handle,
        string? dumpPath,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var resolveResult = ResolveTarget(handle, dumpPath);
        if (resolveResult.Error is not null)
        {
            return JsonOut.WriteError(resolveResult.Error);
        }

        var target = resolveResult.Handle!;
        logger.LogInformation("health: handle={Handle}", target);

        try
        {
            using var channel = ScryChannel.ForEndpoint(target);
            var client = new ScryGrpc.ScryClient(channel);
            using var cts = LinkTimeout(ct, timeoutSeconds);
            var response = await client.HealthAsync(new HealthRequest(), cancellationToken: cts.Token);
            JsonOut.Write(new
            {
                handle = target,
                state = response.State.ToString().ToUpperInvariant(),
                runtimeVersion = response.RuntimeVersion,
                detail = response.Detail,
            });
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "health RPC failed for {Handle}", target);
            return JsonOut.WriteError(ConnectError(ex, target));
        }
    }

    // -------------------------------------------------------------------------
    // stop
    // -------------------------------------------------------------------------

    public async Task<int> StopAsync(
        string? handle,
        string? dumpPath,
        CancellationToken ct)
    {
        var resolveResult = ResolveTarget(handle, dumpPath);
        if (resolveResult.Error is not null)
        {
            return JsonOut.WriteError(resolveResult.Error);
        }

        var target = resolveResult.Handle!;
        var descriptor = resolveResult.Descriptor;
        logger.LogInformation("stop: handle={Handle}", target);

        try
        {
            using var channel = ScryChannel.ForEndpoint(target);
            var client = new ScryGrpc.ScryClient(channel);
            using var cts = LinkTimeout(ct, 5);
            await client.ShutdownAsync(new ShutdownRequest(), cancellationToken: cts.Token);
            ScrySessions.Unregister(target);
            logger.LogInformation("Graceful stop: {Handle}", target);
            JsonOut.Write(new { handle = target, stopped = "graceful" });
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Graceful stop failed for {Handle}; attempting force kill", target);

            if (descriptor is not null)
            {
                try
                {
                    var proc = Process.GetProcessById(descriptor.Pid);
                    proc.Kill();
                    ScrySessions.Unregister(target);
                    logger.LogInformation("Force killed {Handle} (pid={Pid})", target, descriptor.Pid);
                    JsonOut.Write(new { handle = target, stopped = "forced", pid = descriptor.Pid });
                    return 0;
                }
                catch (Exception killEx)
                {
                    logger.LogError(killEx, "Force kill failed for {Handle}", target);
                }
            }

            return JsonOut.WriteError(new CliError(
                "UNAVAILABLE",
                $"could not stop session {target}: RPC failed and no pid available to kill"));
        }
    }

    // -------------------------------------------------------------------------
    // kill
    // -------------------------------------------------------------------------

    public Task<int> KillAsync(
        string? handle,
        string? dumpPath,
        CancellationToken ct)
    {
        var resolveResult = ResolveTarget(handle, dumpPath);
        if (resolveResult.Error is not null)
        {
            return Task.FromResult(JsonOut.WriteError(resolveResult.Error));
        }

        var target = resolveResult.Handle!;
        var descriptor = resolveResult.Descriptor;
        logger.LogInformation("kill: handle={Handle}", target);

        if (descriptor is null)
        {
            logger.LogWarning("No live descriptor for {Handle}", target);
            return Task.FromResult(JsonOut.WriteError(new CliError(
                "UNAVAILABLE",
                $"no live session descriptor found for handle {target}")));
        }

        try
        {
            var proc = Process.GetProcessById(descriptor.Pid);
            proc.Kill();
            ScrySessions.Unregister(target);
            logger.LogInformation("Killed {Handle} (pid={Pid})", target, descriptor.Pid);
            JsonOut.Write(new { handle = target, killed = descriptor.Pid });
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kill failed for {Handle}", target);
            return Task.FromResult(JsonOut.WriteError(new CliError(
                "UNAVAILABLE",
                $"could not kill session {target}: {ex.Message}")));
        }
    }

    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static (string? Handle, SessionDescriptor? Descriptor, CliError? Error) ResolveTarget(
        string? handle,
        string? dumpPath)
    {
        // Explicit handle takes top priority.
        if (!string.IsNullOrEmpty(handle))
        {
            var sessions = ScrySessions.List();
            var d = sessions.FirstOrDefault(s => s.Handle == handle);
            return (handle, d, null);
        }

        // Explicit dump path → derive handle.
        if (!string.IsNullOrEmpty(dumpPath))
        {
            var derived = ScryEndpoint.DeriveId(dumpPath);
            var sessions = ScrySessions.List();
            var d = sessions.FirstOrDefault(s => s.Handle == derived);
            return (derived, d, null);
        }

        // Default: the single active session.
        var all = ScrySessions.List();
        if (all.Count == 0)
        {
            return (null, null, new CliError(
                "FAILED_PRECONDITION",
                "no active scryd session",
                "scry analyze <dump>"));
        }

        if (all.Count > 1)
        {
            var handleList = string.Join(", ", all.Select(s => s.Handle));
            return (null, null, new CliError(
                "FAILED_PRECONDITION",
                $"more than one live session ({handleList}) — pass an explicit handle or --dump",
                null));
        }

        return (all[0].Handle, all[0], null);
    }

    private static string? FindScryd()
    {
        var envPath = Environment.GetEnvironmentVariable("SCRYD_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        var baseDir = AppContext.BaseDirectory;
        foreach (var name in new[] { "scryd.exe", "scryd" })
        {
            var candidate = Path.Combine(baseDir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
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

    private static CliError ConnectError(Exception ex, string handle)
    {
        var rpc = ex as RpcException;
        if (rpc is not null && !IsTransportFailure(rpc))
        {
            return new CliError(rpc.StatusCode.ToString().ToUpperInvariant(), rpc.Status.Detail);
        }

        return new CliError(
            Code: "UNAVAILABLE",
            Message: $"no scryd host is reachable for handle {handle}",
            Hint: "start one with: scry analyze <dump>");
    }

    private static bool IsTransportFailure(RpcException rpc) =>
        rpc.StatusCode is StatusCode.Unavailable ||
        rpc.Status.DebugException is HttpRequestException or IOException or SocketException or TimeoutException;
}
