using System.CommandLine;
using Scry.Client;

var dumpOption = new Option<string>("--dump")
{
    Description = "Path to the .NET memory dump the host is serving.",
    Required = true,
};

var timeoutOption = new Option<int>("--timeout")
{
    Description = "RPC timeout in seconds (0 = no timeout).",
    DefaultValueFactory = _ => 10,
};

var healthCommand = new Command("health", "Print the scryd host's health for a dump.");
healthCommand.Options.Add(dumpOption);
healthCommand.Options.Add(timeoutOption);
healthCommand.SetAction((parseResult, ct) =>
    Commands.HealthAsync(parseResult.GetValue(dumpOption)!, parseResult.GetValue(timeoutOption), ct));

var shutdownCommand = new Command("shutdown", "Ask the scryd host for a dump to exit.");
shutdownCommand.Options.Add(dumpOption);
shutdownCommand.Options.Add(timeoutOption);
shutdownCommand.SetAction((parseResult, ct) =>
    Commands.ShutdownAsync(parseResult.GetValue(dumpOption)!, parseResult.GetValue(timeoutOption), ct));

var root = new RootCommand("scry — structured .NET dump analysis for AI agents.");
root.Subcommands.Add(healthCommand);
root.Subcommands.Add(shutdownCommand);

return await root.Parse(args).InvokeAsync();
