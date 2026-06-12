using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Scry.Client.Cli;

/// <summary>
/// Shared command-action plumbing: builds the DI container (honoring <c>--verbose</c>),
/// resolves <see cref="ScryCommands"/>, and runs the verb body. Also applies the
/// <c>[handle]</c> argument vs <c>--handle</c> option precedence.
/// </summary>
internal static class CliRunner
{
    public static async Task<int> Run(
        ParseResult parseResult,
        Option<bool> verbose,
        Func<ScryCommands, CancellationToken, Task<int>> action,
        CancellationToken ct)
    {
        await using var sp = Bootstrap.Build(parseResult.GetValue(verbose));
        return await action(sp.GetRequiredService<ScryCommands>(), ct);
    }

    public static string? Handle(ParseResult parseResult, Argument<string?> handleArg, Option<string?> handleOption)
        => parseResult.GetValue(handleArg) ?? parseResult.GetValue(handleOption);
}
