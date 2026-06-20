using System.CommandLine;

namespace Scry.Client.Cli;

/// <summary>Meta verbs that don't touch a session: version.</summary>
internal static class MetaCommands
{
    public static IEnumerable<Command> Build(Option<bool> verbose)
    {
        yield return Version(verbose);
    }

    private static Command Version(Option<bool> verbose)
    {
        var cmd = new Command("version", "Print scry's version and host runtime as JSON.");
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) => commands.VersionAsync(c), ct));
        return cmd;
    }
}
