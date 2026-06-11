using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Scry.Client;

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

#region root

var root = new RootCommand("scry — structured .NET dump analysis for AI agents.");
root.Options.Add(verboseOption);
root.Subcommands.Add(analyzeCommand);
root.Subcommands.Add(psCommand);
root.Subcommands.Add(healthCommand);
root.Subcommands.Add(stopCommand);
root.Subcommands.Add(killCommand);

return await root.Parse(args).InvokeAsync();
#endregion
