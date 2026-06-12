using System.CommandLine;
using Scry.Client.Cli;
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

var root = new RootCommand("scry — structured .NET dump analysis for AI agents.");
root.Options.Add(verboseOption);

// Command builders live in Scry.Client.Cli, grouped by area. Adding a verb is a new
// builder in the relevant group — Program.cs stays this thin.
foreach (var command in SessionCommands.Build(verboseOption))
{
    root.Subcommands.Add(command);
}

foreach (var command in AnalysisCommands.Build(verboseOption))
{
    root.Subcommands.Add(command);
}

return await root.Parse(args).InvokeAsync();
