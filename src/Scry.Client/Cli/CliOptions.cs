using System.CommandLine;

namespace Scry.Client.Cli;

/// <summary>
/// Factories for the option/argument shapes shared across scry verbs, so each is defined
/// once with a canonical description. Every call returns a fresh instance (options can't be
/// shared between commands).
/// </summary>
internal static class CliOptions
{
    public static Argument<string?> HandleArg() => new("handle")
    {
        Description = "Session handle (optional).",
        Arity = ArgumentArity.ZeroOrOne,
    };

    public static Option<string?> HandleOption() => new("--handle")
    {
        Description = "Explicit session handle.",
    };

    public static Option<string?> DumpOption() => new("--dump")
    {
        Description = "Derive target from this dump path.",
    };

    public static Option<int> TimeoutOption(int seconds = 10, string? description = null) => new("--timeout")
    {
        Description = description ?? "RPC timeout in seconds (0 = no timeout).",
        DefaultValueFactory = _ => seconds,
    };

    public static Option<string?> AddressOption(string description) => new("--address")
    {
        Description = description,
    };

    public static Option<int> LimitOption(int defaultLimit = 1000, string? description = null) => new("--limit")
    {
        Description = description ?? "Max items per page.",
        DefaultValueFactory = _ => defaultLimit,
    };

    public static Option<int> OffsetOption(string? description = null) => new("--offset")
    {
        Description = description ?? "Page offset.",
        DefaultValueFactory = _ => 0,
    };
}
