using System.CommandLine;

namespace Scry.Client.Cli;

/// <summary>Session-lifecycle verbs: analyze, ps, health, stop, kill.</summary>
internal static class SessionCommands
{
    public static IEnumerable<Command> Build(Option<bool> verbose)
    {
        yield return Analyze(verbose);
        yield return Ps(verbose);
        yield return Health(verbose);
        yield return Stop(verbose);
        yield return Kill(verbose);
    }

    private static Command Analyze(Option<bool> verbose)
    {
        var dumpArg = new Argument<string>("dump") { Description = "Path to the .NET memory dump to analyze." };
        var idleTimeout = new Option<int>("--idle-timeout")
        {
            Description = "Idle timeout in minutes for scryd (0 disables).",
            DefaultValueFactory = _ => 10,
        };
        var readyTimeout = new Option<int>("--ready-timeout")
        {
            Description = "How many seconds to wait for scryd to become READY.",
            DefaultValueFactory = _ => 30,
        };
        var cmd = new Command("analyze", "Spawn a scryd host for a dump and wait until READY.");
        cmd.Arguments.Add(dumpArg);
        cmd.Options.Add(idleTimeout);
        cmd.Options.Add(readyTimeout);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.AnalyzeAsync(
                pr.GetValue(dumpArg)!,
                pr.GetValue(idleTimeout),
                pr.GetValue(readyTimeout),
                pr.GetValue(verbose),
                c), ct));
        return cmd;
    }

    private static Command Ps(Option<bool> verbose)
    {
        var cmd = new Command("ps", "List live scryd sessions.");
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) => commands.PsAsync(c), ct));
        return cmd;
    }

    private static Command Health(Option<bool> verbose)
    {
        var handleArg = CliOptions.HandleArg();
        var handleOption = CliOptions.HandleOption();
        var dumpOption = CliOptions.DumpOption();
        var timeout = CliOptions.TimeoutOption(10);
        var cmd = new Command("health", "Print the health of the active (or specified) scryd session.");
        cmd.Arguments.Add(handleArg);
        cmd.Options.Add(handleOption);
        cmd.Options.Add(dumpOption);
        cmd.Options.Add(timeout);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.HealthAsync(
                CliRunner.Handle(pr, handleArg, handleOption),
                pr.GetValue(dumpOption),
                pr.GetValue(timeout),
                c), ct));
        return cmd;
    }

    private static Command Stop(Option<bool> verbose)
    {
        var handleArg = CliOptions.HandleArg();
        var handleOption = CliOptions.HandleOption();
        var dumpOption = CliOptions.DumpOption();
        var cmd = new Command("stop", "Gracefully stop a scryd session (force-kill fallback).");
        cmd.Arguments.Add(handleArg);
        cmd.Options.Add(handleOption);
        cmd.Options.Add(dumpOption);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.StopAsync(
                CliRunner.Handle(pr, handleArg, handleOption),
                pr.GetValue(dumpOption),
                c), ct));
        return cmd;
    }

    private static Command Kill(Option<bool> verbose)
    {
        var handleArg = CliOptions.HandleArg();
        var handleOption = CliOptions.HandleOption();
        var dumpOption = CliOptions.DumpOption();
        var cmd = new Command("kill", "Force-terminate a scryd session.");
        cmd.Arguments.Add(handleArg);
        cmd.Options.Add(handleOption);
        cmd.Options.Add(dumpOption);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.KillAsync(
                CliRunner.Handle(pr, handleArg, handleOption),
                pr.GetValue(dumpOption),
                c), ct));
        return cmd;
    }
}
