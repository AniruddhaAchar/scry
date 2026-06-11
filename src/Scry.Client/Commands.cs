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
                if (response.State == HealthResponse.Types.State.Failed)
                {
                    logger.LogError("scryd reported FAILED: {Detail}", response.Detail);
                    return JsonOut.WriteError(new CliError(
                        "FAILED_PRECONDITION",
                        $"dump load failed: {response.Detail}"));
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
    // stack
    // -------------------------------------------------------------------------

    public async Task<int> StackAsync(
        string? handle,
        uint? threadOsId,
        int timeoutSeconds,
        CancellationToken ct)
    {
        // Analysis verbs target an already-established session by handle (or the
        // single active one); a dump path is a session-management selector, not an
        // analysis input — see health/stop/kill for the --dump form.
        var resolveResult = ResolveTarget(handle, dumpPath: null);
        if (resolveResult.Error is not null)
        {
            return JsonOut.WriteError(resolveResult.Error);
        }

        var target = resolveResult.Handle!;
        logger.LogInformation("stack: handle={Handle} thread={Thread}", target, threadOsId);

        try
        {
            using var channel = ScryChannel.ForEndpoint(target);
            var client = new ScryGrpc.ScryClient(channel);
            using var cts = LinkTimeout(ct, timeoutSeconds);
            var request = new ClrStackRequest
            {
                AllThreads = threadOsId is null,
                ThreadOsId = threadOsId ?? 0,
            };
            var response = await client.ClrStackAsync(request, cancellationToken: cts.Token);

            JsonOut.Write(new
            {
                handle = target,
                threads = response.Threads.Select(t => new
                {
                    osThreadId = t.OsThreadId,
                    managedThreadId = t.ManagedThreadId,
                    isAlive = t.IsAlive,
                    frames = t.Frames.Select(f => new
                    {
                        kind = f.Kind,
                        ip = $"0x{f.InstructionPointer:x}",
                        sp = $"0x{f.StackPointer:x}",
                        method = string.IsNullOrEmpty(f.Method) ? null : f.Method,
                        type = string.IsNullOrEmpty(f.Type) ? null : f.Type,
                        module = string.IsNullOrEmpty(f.Module) ? null : f.Module,
                    }).ToArray(),
                }).ToArray(),
            });
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "stack RPC failed for {Handle}", target);
            return JsonOut.WriteError(ConnectError(ex, target));
        }
    }

    // -------------------------------------------------------------------------
    // dumpheap
    // -------------------------------------------------------------------------

    public async Task<int> DumpHeapAsync(
        string? handle, string? type, bool stat, int limit, int offset, int timeoutSeconds, CancellationToken ct)
    {
        var resolveResult = ResolveTarget(handle, dumpPath: null);
        if (resolveResult.Error is not null)
        {
            return JsonOut.WriteError(resolveResult.Error);
        }

        var target = resolveResult.Handle!;
        var listObjects = !stat && !string.IsNullOrEmpty(type);
        logger.LogInformation("dumpheap: handle={Handle} type={Type} stat={Stat}", target, type, stat);

        try
        {
            using var channel = ScryChannel.ForEndpoint(target);
            var client = new ScryGrpc.ScryClient(channel);
            using var cts = LinkTimeout(ct, timeoutSeconds);
            var request = new DumpHeapRequest
            {
                TypeFilter = type ?? string.Empty,
                Stat = stat || string.IsNullOrEmpty(type),
                Limit = limit,
                Offset = offset,
            };
            var response = await client.DumpHeapAsync(request, cancellationToken: cts.Token);

            if (listObjects)
            {
                JsonOut.Write(new
                {
                    handle = target,
                    totalMatches = response.TotalMatches,
                    truncated = response.Truncated,
                    offset,
                    limit,
                    objects = response.Objects.Select(o => new
                    {
                        address = $"0x{o.Address:x}",
                        type = o.Type,
                        size = o.Size,
                    }).ToArray(),
                });
            }
            else
            {
                JsonOut.Write(new
                {
                    handle = target,
                    stats = response.Stats.Select(s => new
                    {
                        type = s.Type,
                        methodTable = $"0x{s.MethodTable:x}",
                        count = s.Count,
                        totalSize = s.TotalSize,
                    }).ToArray(),
                });
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "dumpheap RPC failed for {Handle}", target);
            return JsonOut.WriteError(ConnectError(ex, target));
        }
    }

    // -------------------------------------------------------------------------
    // dumpexceptions
    // -------------------------------------------------------------------------

    public async Task<int> DumpExceptionsAsync(
        string? handle, int limit, int offset, int timeoutSeconds, CancellationToken ct)
    {
        var resolveResult = ResolveTarget(handle, dumpPath: null);
        if (resolveResult.Error is not null)
        {
            return JsonOut.WriteError(resolveResult.Error);
        }

        var target = resolveResult.Handle!;
        logger.LogInformation("dumpexceptions: handle={Handle}", target);

        try
        {
            using var channel = ScryChannel.ForEndpoint(target);
            var client = new ScryGrpc.ScryClient(channel);
            using var cts = LinkTimeout(ct, timeoutSeconds);
            var response = await client.DumpExceptionsAsync(
                new DumpExceptionsRequest { Limit = limit, Offset = offset }, cancellationToken: cts.Token);

            JsonOut.Write(new
            {
                handle = target,
                totalMatches = response.TotalMatches,
                truncated = response.Truncated,
                offset,
                limit,
                exceptions = response.Exceptions.Select(MapException).ToArray(),
            });
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "dumpexceptions RPC failed for {Handle}", target);
            return JsonOut.WriteError(ConnectError(ex, target));
        }
    }

    // -------------------------------------------------------------------------
    // printexception
    // -------------------------------------------------------------------------

    public async Task<int> PrintExceptionAsync(
        string? handle, string? addressText, int timeoutSeconds, CancellationToken ct)
    {
        if (!TryParseAddress(addressText, out var address))
        {
            return JsonOut.WriteError(new CliError(
                "INVALID_ARGUMENT", $"--address is required; could not parse '{addressText}' (expected hex, e.g. 0x7ff...)"));
        }

        var resolveResult = ResolveTarget(handle, dumpPath: null);
        if (resolveResult.Error is not null)
        {
            return JsonOut.WriteError(resolveResult.Error);
        }

        var target = resolveResult.Handle!;
        logger.LogInformation("printexception: handle={Handle} address=0x{Address:x}", target, address);

        try
        {
            using var channel = ScryChannel.ForEndpoint(target);
            var client = new ScryGrpc.ScryClient(channel);
            using var cts = LinkTimeout(ct, timeoutSeconds);
            var response = await client.PrintExceptionAsync(
                new PrintExceptionRequest { Address = address }, cancellationToken: cts.Token);

            if (!response.Found)
            {
                JsonOut.Write(new { handle = target, address = $"0x{address:x}", found = false });
                return 0;
            }

            var e = response.Exception;
            JsonOut.Write(new
            {
                handle = target,
                found = true,
                address = $"0x{e.Address:x}",
                type = e.Type,
                message = string.IsNullOrEmpty(e.Message) ? null : e.Message,
                hResult = $"0x{(uint)e.Hresult:x8}",
                inner = e.Inner.Select(l => new
                {
                    type = l.Type,
                    message = string.IsNullOrEmpty(l.Message) ? null : l.Message,
                }).ToArray(),
                stackTrace = response.StackTrace.Select(f => new
                {
                    kind = f.Kind,
                    ip = $"0x{f.InstructionPointer:x}",
                    sp = $"0x{f.StackPointer:x}",
                    method = string.IsNullOrEmpty(f.Method) ? null : f.Method,
                    type = string.IsNullOrEmpty(f.Type) ? null : f.Type,
                    module = string.IsNullOrEmpty(f.Module) ? null : f.Module,
                }).ToArray(),
            });
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "printexception RPC failed for {Handle}", target);
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
        string? dumpPath) =>
        ResolveTarget(handle, dumpPath, ScrySessions.List());

    /// <summary>
    /// Pure target-resolution logic over a supplied session list (no I/O), so it can be
    /// unit-tested. Explicit handle wins; then a dump path (derive its handle); otherwise
    /// default to the single active session, erroring on zero or more than one.
    /// </summary>
    internal static (string? Handle, SessionDescriptor? Descriptor, CliError? Error) ResolveTarget(
        string? handle,
        string? dumpPath,
        IReadOnlyList<SessionDescriptor> sessions)
    {
        // Explicit handle takes top priority.
        if (!string.IsNullOrEmpty(handle))
        {
            var d = sessions.FirstOrDefault(s => s.Handle == handle);
            return (handle, d, null);
        }

        // Explicit dump path → derive handle.
        if (!string.IsNullOrEmpty(dumpPath))
        {
            var derived = ScryEndpoint.DeriveId(dumpPath);
            var d = sessions.FirstOrDefault(s => s.Handle == derived);
            return (derived, d, null);
        }

        // Default: the single active session.
        if (sessions.Count == 0)
        {
            return (null, null, new CliError(
                "FAILED_PRECONDITION",
                "no active scryd session",
                "scry analyze <dump>"));
        }

        if (sessions.Count > 1)
        {
            var handleList = string.Join(", ", sessions.Select(s => s.Handle));
            return (null, null, new CliError(
                "FAILED_PRECONDITION",
                $"more than one live session ({handleList}) — pass an explicit handle or --dump",
                null));
        }

        return (sessions[0].Handle, sessions[0], null);
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

    private static object MapException(ExceptionInfo e) => new
    {
        address = $"0x{e.Address:x}",
        type = e.Type,
        message = string.IsNullOrEmpty(e.Message) ? null : e.Message,
        hResult = $"0x{(uint)e.Hresult:x8}",
        inner = e.Inner.Select(l => new
        {
            type = l.Type,
            message = string.IsNullOrEmpty(l.Message) ? null : l.Message,
        }).ToArray(),
    };

    private static bool TryParseAddress(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }

        return ulong.TryParse(
            s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out address);
    }
}
