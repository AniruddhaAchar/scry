using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Scry.Client;
using Scry.Host;

// Hidden daemon mode: `scry __host --dump ...` runs the gRPC host (ADR 0007).
// Not registered as a subcommand, so it never appears in --help.
if (args.Length > 0 && args[0] == "__host")
{
    return await HostMode.RunAsync(args[1..]);
}

// Global --verbose / -v (recursive so it is visible on all subcommands).
var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Enable verbose (Debug) logging to the log file.",
    Recursive = true,
};

#region analyze
var dumpArg = new Argument<string>("dump")
{
    Description = "Path to the .NET memory dump to analyze.",
};
var idleTimeoutOption = new Option<int>("--idle-timeout")
{
    Description = "Idle timeout in minutes for scryd (0 disables).",
    DefaultValueFactory = _ => 10,
};
var readyTimeoutOption = new Option<int>("--ready-timeout")
{
    Description = "How many seconds to wait for scryd to become READY.",
    DefaultValueFactory = _ => 30,
};
var analyzeCommand = new Command("analyze", "Spawn a scryd host for a dump and wait until READY.");
analyzeCommand.Arguments.Add(dumpArg);
analyzeCommand.Options.Add(idleTimeoutOption);
analyzeCommand.Options.Add(readyTimeoutOption);
analyzeCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    return await sp.GetRequiredService<ScryCommands>().AnalyzeAsync(
        parseResult.GetValue(dumpArg)!,
        parseResult.GetValue(idleTimeoutOption),
        parseResult.GetValue(readyTimeoutOption),
        parseResult.GetValue(verboseOption),
        ct);
});
#endregion

#region ps
var psCommand = new Command("ps", "List live scryd sessions.");
psCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    return await sp.GetRequiredService<ScryCommands>().PsAsync(ct);
});
#endregion

#region health
var healthHandleArg = new Argument<string?>("handle")
{
    Description = "Session handle (optional).",
    Arity = ArgumentArity.ZeroOrOne,
};
var healthHandleOption = new Option<string?>("--handle")
{
    Description = "Explicit session handle.",
};
var healthDumpOption = new Option<string?>("--dump")
{
    Description = "Derive target from this dump path.",
};
var healthTimeoutOption = new Option<int>("--timeout")
{
    Description = "RPC timeout in seconds (0 = no timeout).",
    DefaultValueFactory = _ => 10,
};
var healthCommand = new Command("health", "Print the health of the active (or specified) scryd session.");
healthCommand.Arguments.Add(healthHandleArg);
healthCommand.Options.Add(healthHandleOption);
healthCommand.Options.Add(healthDumpOption);
healthCommand.Options.Add(healthTimeoutOption);
healthCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    var handle = parseResult.GetValue(healthHandleArg) ?? parseResult.GetValue(healthHandleOption);
    return await sp.GetRequiredService<ScryCommands>().HealthAsync(
        handle,
        parseResult.GetValue(healthDumpOption),
        parseResult.GetValue(healthTimeoutOption),
        ct);
});
#endregion

#region stop
// ---- stop -------------------------------------------------------------------
var stopHandleArg = new Argument<string?>("handle")
{
    Description = "Session handle (optional).",
    Arity = ArgumentArity.ZeroOrOne,
};
var stopHandleOption = new Option<string?>("--handle")
{
    Description = "Explicit session handle.",
};
var stopDumpOption = new Option<string?>("--dump")
{
    Description = "Derive target from this dump path.",
};
var stopCommand = new Command("stop", "Gracefully stop a scryd session (force-kill fallback).");
stopCommand.Arguments.Add(stopHandleArg);
stopCommand.Options.Add(stopHandleOption);
stopCommand.Options.Add(stopDumpOption);
stopCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    var handle = parseResult.GetValue(stopHandleArg) ?? parseResult.GetValue(stopHandleOption);
    return await sp.GetRequiredService<ScryCommands>().StopAsync(
        handle,
        parseResult.GetValue(stopDumpOption),
        ct);
});
#endregion


#region kill

var killHandleArg = new Argument<string?>("handle")
{
    Description = "Session handle (optional).",
    Arity = ArgumentArity.ZeroOrOne,
};
var killHandleOption = new Option<string?>("--handle")
{
    Description = "Explicit session handle.",
};
var killDumpOption = new Option<string?>("--dump")
{
    Description = "Derive target from this dump path.",
};
var killCommand = new Command("kill", "Force-terminate a scryd session.");
killCommand.Arguments.Add(killHandleArg);
killCommand.Options.Add(killHandleOption);
killCommand.Options.Add(killDumpOption);
killCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    var handle = parseResult.GetValue(killHandleArg) ?? parseResult.GetValue(killHandleOption);
    return await sp.GetRequiredService<ScryCommands>().KillAsync(
        handle,
        parseResult.GetValue(killDumpOption),
        ct);
});
#endregion

#region stack
var stackHandleArg = new Argument<string?>("handle")
{
    Description = "Session handle (optional).",
    Arity = ArgumentArity.ZeroOrOne,
};
var stackHandleOption = new Option<string?>("--handle")
{
    Description = "Explicit session handle.",
};
var stackThreadOption = new Option<uint?>("--thread")
{
    Description = "Only walk this OS thread id (default: all managed threads).",
};
var stackTimeoutOption = new Option<int>("--timeout")
{
    Description = "RPC timeout in seconds (0 = no timeout).",
    DefaultValueFactory = _ => 10,
};
var stackCommand = new Command("stack", "Print managed thread stack traces as JSON.");
stackCommand.Arguments.Add(stackHandleArg);
stackCommand.Options.Add(stackHandleOption);
stackCommand.Options.Add(stackThreadOption);
stackCommand.Options.Add(stackTimeoutOption);
stackCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    var handle = parseResult.GetValue(stackHandleArg) ?? parseResult.GetValue(stackHandleOption);
    return await sp.GetRequiredService<ScryCommands>().StackAsync(
        handle,
        parseResult.GetValue(stackThreadOption),
        parseResult.GetValue(stackTimeoutOption),
        ct);
});
#endregion

#region dumpheap
var dumpheapHandleArg = new Argument<string?>("handle") { Description = "Session handle (optional).", Arity = ArgumentArity.ZeroOrOne };
var dumpheapHandleOption = new Option<string?>("--handle") { Description = "Explicit session handle." };
var dumpheapTypeOption = new Option<string?>("--type") { Description = "Case-sensitive substring filter on the full type name." };
var dumpheapStatOption = new Option<bool>("--stat") { Description = "Force per-type statistics (default when no --type)." };
var dumpheapLimitOption = new Option<int>("--limit") { Description = "Max objects per listing page.", DefaultValueFactory = _ => 1000 };
var dumpheapOffsetOption = new Option<int>("--offset") { Description = "Object-listing page offset.", DefaultValueFactory = _ => 0 };
var dumpheapTimeoutOption = new Option<int>("--timeout") { Description = "RPC timeout in seconds (0 = none). The first heap command warms a snapshot.", DefaultValueFactory = _ => 30 };
var dumpheapCommand = new Command("dumpheap", "Heap statistics, or a paged object listing with --type.");
dumpheapCommand.Arguments.Add(dumpheapHandleArg);
dumpheapCommand.Options.Add(dumpheapHandleOption);
dumpheapCommand.Options.Add(dumpheapTypeOption);
dumpheapCommand.Options.Add(dumpheapStatOption);
dumpheapCommand.Options.Add(dumpheapLimitOption);
dumpheapCommand.Options.Add(dumpheapOffsetOption);
dumpheapCommand.Options.Add(dumpheapTimeoutOption);
dumpheapCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    var handle = parseResult.GetValue(dumpheapHandleArg) ?? parseResult.GetValue(dumpheapHandleOption);
    return await sp.GetRequiredService<ScryCommands>().DumpHeapAsync(
        handle,
        parseResult.GetValue(dumpheapTypeOption),
        parseResult.GetValue(dumpheapStatOption),
        parseResult.GetValue(dumpheapLimitOption),
        parseResult.GetValue(dumpheapOffsetOption),
        parseResult.GetValue(dumpheapTimeoutOption),
        ct);
});
#endregion

#region dumpexceptions
var dumpexHandleArg = new Argument<string?>("handle") { Description = "Session handle (optional).", Arity = ArgumentArity.ZeroOrOne };
var dumpexHandleOption = new Option<string?>("--handle") { Description = "Explicit session handle." };
var dumpexLimitOption = new Option<int>("--limit") { Description = "Max exceptions per page.", DefaultValueFactory = _ => 1000 };
var dumpexOffsetOption = new Option<int>("--offset") { Description = "Page offset.", DefaultValueFactory = _ => 0 };
var dumpexTimeoutOption = new Option<int>("--timeout") { Description = "RPC timeout in seconds (0 = none).", DefaultValueFactory = _ => 30 };
var dumpexCommand = new Command("dumpexceptions", "List live exceptions on the heap (address, type, message, HResult, inner chain).");
dumpexCommand.Arguments.Add(dumpexHandleArg);
dumpexCommand.Options.Add(dumpexHandleOption);
dumpexCommand.Options.Add(dumpexLimitOption);
dumpexCommand.Options.Add(dumpexOffsetOption);
dumpexCommand.Options.Add(dumpexTimeoutOption);
dumpexCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    var handle = parseResult.GetValue(dumpexHandleArg) ?? parseResult.GetValue(dumpexHandleOption);
    return await sp.GetRequiredService<ScryCommands>().DumpExceptionsAsync(
        handle,
        parseResult.GetValue(dumpexLimitOption),
        parseResult.GetValue(dumpexOffsetOption),
        parseResult.GetValue(dumpexTimeoutOption),
        ct);
});
#endregion

#region printexception
var peHandleOption = new Option<string?>("--handle") { Description = "Explicit session handle." };
var peAddressOption = new Option<string?>("--address") { Description = "Exception object address (hex, e.g. 0x7ff...)." };
var peTimeoutOption = new Option<int>("--timeout") { Description = "RPC timeout in seconds (0 = none).", DefaultValueFactory = _ => 10 };
var peCommand = new Command("printexception", "Full detail for one exception by address, including its stack trace.");
peCommand.Options.Add(peHandleOption);
peCommand.Options.Add(peAddressOption);
peCommand.Options.Add(peTimeoutOption);
peCommand.SetAction(async (parseResult, ct) =>
{
    await using var sp = Bootstrap.Build(parseResult.GetValue(verboseOption));
    return await sp.GetRequiredService<ScryCommands>().PrintExceptionAsync(
        parseResult.GetValue(peHandleOption),
        parseResult.GetValue(peAddressOption),
        parseResult.GetValue(peTimeoutOption),
        ct);
});
#endregion

#region root

var root = new RootCommand("scry — structured .NET dump analysis for AI agents.");
root.Options.Add(verboseOption);
root.Subcommands.Add(analyzeCommand);
root.Subcommands.Add(psCommand);
root.Subcommands.Add(healthCommand);
root.Subcommands.Add(stackCommand);
root.Subcommands.Add(dumpheapCommand);
root.Subcommands.Add(dumpexCommand);
root.Subcommands.Add(peCommand);
root.Subcommands.Add(stopCommand);
root.Subcommands.Add(killCommand);

return await root.Parse(args).InvokeAsync();
#endregion
