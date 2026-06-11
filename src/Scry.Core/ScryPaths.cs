namespace Scry.Core;

/// <summary>Well-known filesystem paths used by scry and scryd.</summary>
public static class ScryPaths
{
    /// <summary>Root config and log directory: <c>~/.scry</c>.</summary>
    public static string HomeDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".scry");

    /// <summary>Main config file: <c>~/.scry/scry.config.json</c>.</summary>
    public static string ConfigFile => Path.Combine(HomeDir, "scry.config.json");

    /// <summary>Default directory for per-process log files: <c>~/.scry/logs</c>.</summary>
    public static string DefaultLogsDir => Path.Combine(HomeDir, "logs");
}
