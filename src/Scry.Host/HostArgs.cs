namespace Scry.Host;

/// <summary>
/// Minimal argument parsing for scryd. The host is launched by the scry client
/// (or by hand), so it keeps a tiny hand-rolled parser rather than taking a
/// dependency on System.CommandLine.
/// </summary>
internal sealed record HostArgs(string DumpPath, TimeSpan IdleTimeout, bool Verbose)
{
    public static bool TryParse(string[] args, out HostArgs? parsed, out string? error)
    {
        string? dump = null;
        var idle = TimeSpan.FromMinutes(10);
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dump":
                    if (++i >= args.Length)
                    {
                        return Fail("--dump requires a path", out parsed, out error);
                    }

                    dump = args[i];
                    break;

                case "--idle-timeout":
                    if (++i >= args.Length || !int.TryParse(args[i], out var minutes) || minutes < 0)
                    {
                        return Fail("--idle-timeout requires a non-negative integer (minutes; 0 disables)", out parsed, out error);
                    }

                    idle = TimeSpan.FromMinutes(minutes);
                    break;

                case "--verbose":
                case "-v":
                    verbose = true;
                    break;

                default:
                    return Fail($"unknown argument '{args[i]}'", out parsed, out error);
            }
        }

        if (string.IsNullOrWhiteSpace(dump))
        {
            return Fail("--dump <path> is required", out parsed, out error);
        }

        parsed = new HostArgs(dump, idle, verbose);
        error = null;
        return true;
    }

    private static bool Fail(string message, out HostArgs? parsed, out string? error)
    {
        parsed = null;
        error = $"scryd: {message}";
        return false;
    }
}
