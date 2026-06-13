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
    private void RequireReady()
    {
        var snapshot = state.Snapshot();
        if (snapshot.State != HealthResponse.Types.State.Ready)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, $"runtime not loaded: {snapshot.Detail}"));
        }
    }

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

    public override async Task<DumpHeapResponse> DumpHeap(DumpHeapRequest request, ServerCallContext context)
    {
        activity.Touch();
        RequireReady();

        var response = new DumpHeapResponse();
        var listObjects = !request.Stat && !string.IsNullOrEmpty(request.TypeFilter);
        if (listObjects)
        {
            var page = await worker.RunAsync(
                new DumpHeapObjectsCommand(request.TypeFilter, request.Offset, request.Limit),
                context.CancellationToken);
            foreach (var o in page.Items)
            {
                response.Objects.Add(new HeapObject { Address = o.Address, Type = o.Type, Size = o.Size });
            }

            response.TotalMatches = page.TotalMatches;
            response.Truncated = page.Truncated;
        }
        else
        {
            var filter = string.IsNullOrEmpty(request.TypeFilter) ? null : request.TypeFilter;
            var stats = await worker.RunAsync(new DumpHeapStatCommand(filter), context.CancellationToken);
            foreach (var s in stats)
            {
                response.Stats.Add(new HeapTypeStat
                {
                    Type = s.Type,
                    MethodTable = s.MethodTable,
                    Count = s.Count,
                    TotalSize = s.TotalSize,
                });
            }
        }

        return response;
    }

    public override async Task<DumpExceptionsResponse> DumpExceptions(DumpExceptionsRequest request, ServerCallContext context)
    {
        activity.Touch();
        RequireReady();

        var page = await worker.RunAsync(
            new DumpExceptionsCommand(request.Offset, request.Limit), context.CancellationToken);

        var response = new DumpExceptionsResponse { TotalMatches = page.TotalMatches, Truncated = page.Truncated };
        foreach (var e in page.Items)
        {
            var proto = new ExceptionInfo
            {
                Address = e.Address,
                Type = e.Type,
                Message = e.Message ?? string.Empty,
                Hresult = e.HResult,
            };
            foreach (var link in e.Inner)
            {
                proto.Inner.Add(new ExceptionLink { Type = link.Type, Message = link.Message ?? string.Empty });
            }

            response.Exceptions.Add(proto);
        }

        return response;
    }

    public override async Task<PrintExceptionResponse> PrintException(PrintExceptionRequest request, ServerCallContext context)
    {
        activity.Touch();
        RequireReady();

        var detail = await worker.RunAsync(new PrintExceptionCommand(request.Address), context.CancellationToken);
        if (detail is null)
        {
            return new PrintExceptionResponse { Found = false };
        }

        var response = new PrintExceptionResponse
        {
            Found = true,
            Exception = new ExceptionInfo
            {
                Address = detail.Info.Address,
                Type = detail.Info.Type,
                Message = detail.Info.Message ?? string.Empty,
                Hresult = detail.Info.HResult,
            },
        };
        foreach (var link in detail.Info.Inner)
        {
            response.Exception.Inner.Add(new ExceptionLink { Type = link.Type, Message = link.Message ?? string.Empty });
        }

        foreach (var f in detail.StackTrace)
        {
            response.StackTrace.Add(new StackFrame
            {
                Kind = f.Kind,
                InstructionPointer = f.InstructionPointer,
                StackPointer = f.StackPointer,
                Method = f.Method ?? string.Empty,
                Type = f.Type ?? string.Empty,
                Module = f.Module ?? string.Empty,
            });
        }

        return response;
    }

    public override async Task<DumpObjectResponse> DumpObject(DumpObjectRequest request, ServerCallContext context)
    {
        activity.Touch();
        RequireReady();

        var dump = await worker.RunAsync(new DumpObjectCommand(request.Address), context.CancellationToken);
        if (dump is null)
        {
            return new DumpObjectResponse { Found = false };
        }

        var response = new DumpObjectResponse
        {
            Found = true,
            Address = dump.Address,
            Type = dump.Type,
            MethodTable = dump.MethodTable,
            Size = dump.Size,
        };
        foreach (var f in dump.Fields)
        {
            response.Fields.Add(new ObjectField
            {
                Name = f.Name,
                Type = f.Type,
                Offset = f.Offset,
                Value = f.Value ?? string.Empty,
            });
        }

        return response;
    }

    public override async Task<DumpArrayResponse> DumpArray(DumpArrayRequest request, ServerCallContext context)
    {
        activity.Touch();
        RequireReady();

        var dump = await worker.RunAsync(
            new DumpArrayCommand(request.Address, request.Offset, request.Limit), context.CancellationToken);
        if (dump is null)
        {
            return new DumpArrayResponse { Found = false };
        }

        var response = new DumpArrayResponse
        {
            Found = true,
            Address = dump.Address,
            Type = dump.Type,
            ElementType = dump.ElementType,
            Length = dump.Length,
            Truncated = dump.Truncated,
        };
        foreach (var e in dump.Elements)
        {
            response.Elements.Add(new ArrayElement { Index = e.Index, Value = e.Value ?? string.Empty });
        }

        return response;
    }

    public override async Task<ClrThreadsResponse> ClrThreads(ClrThreadsRequest request, ServerCallContext context)
    {
        activity.Touch();
        RequireReady();

        var threads = await worker.RunAsync(new ClrThreadsCommand(), context.CancellationToken);

        var response = new ClrThreadsResponse();
        foreach (var t in threads)
        {
            var proto = new ThreadInfo
            {
                OsThreadId = t.OsThreadId,
                ManagedThreadId = t.ManagedThreadId,
                IsAlive = t.IsAlive,
                IsBackground = t.IsBackground,
                IsFinalizer = t.IsFinalizer,
                IsGc = t.IsGc,
                GcMode = t.GcMode,
                LockCount = t.LockCount,
            };
            proto.State.AddRange(t.State);
            if (t.CurrentException is { } ex)
            {
                proto.CurrentException = new ExceptionRef
                {
                    Address = ex.Address,
                    Type = ex.Type,
                    Message = ex.Message ?? string.Empty,
                };
            }

            response.Threads.Add(proto);
        }

        return response;
    }

    public override async Task<GcRootResponse> GcRoot(GcRootRequest request, ServerCallContext context)
    {
        activity.Touch();
        RequireReady();

        var result = await worker.RunAsync(
            new GcRootCommand(request.Address, request.MaxPaths), context.CancellationToken);
        if (result is null)
        {
            return new GcRootResponse { Found = false };
        }

        var response = new GcRootResponse
        {
            Found = true,
            Target = result.Target,
            Rooted = result.Rooted,
            Truncated = result.Truncated,
        };
        foreach (var p in result.Roots)
        {
            var path = new GcRootPath
            {
                RootKind = p.RootKind,
                RootAddress = p.RootAddress,
                StackFrame = p.StackFrame ?? string.Empty,
            };
            foreach (var n in p.Chain)
            {
                path.Chain.Add(new GcRootNode { Address = n.Address, Type = n.Type ?? string.Empty });
            }

            response.Roots.Add(path);
        }

        return response;
    }
}
