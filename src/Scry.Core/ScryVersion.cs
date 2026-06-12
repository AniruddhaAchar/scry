using System.Reflection;

namespace Scry.Core;

/// <summary>
/// The scry product version, read from the running assembly so a single source —
/// <c>&lt;Version&gt;</c> in Directory.Build.props — drives the binary, the tool package,
/// and everywhere a version is reported (e.g. the session descriptor). Updating the
/// version in one place keeps them all in sync.
/// </summary>
public static class ScryVersion
{
    /// <summary>e.g. "0.0.1". Any build metadata after '+' (SourceLink hash) is stripped.</summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var asm = typeof(ScryVersion).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }
}
