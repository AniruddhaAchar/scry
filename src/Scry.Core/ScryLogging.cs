using Microsoft.Extensions.Logging;

namespace Scry.Core;

/// <summary>Resolved logging parameters derived from config and command-line flags.</summary>
public readonly record struct ResolvedLogging(LogLevel Level, string Folder, string FilePath);

/// <summary>
/// Helpers for resolving and wiring up scry's file-based logging.
/// </summary>
public static class ScryLogging
{
    /// <summary>
    /// Resolves the effective logging configuration for <paramref name="appName"/>.
    /// </summary>
    /// <param name="appName">Binary name — included in the log file name (e.g. "scry", "scryd").</param>
    /// <param name="verbose">When <c>true</c>, forces <see cref="LogLevel.Debug"/> regardless of config.</param>
    /// <param name="config">Loaded <see cref="ScryConfig"/>.</param>
    public static ResolvedLogging Resolve(string appName, bool verbose, ScryConfig config)
    {
        var folder = !string.IsNullOrEmpty(config.Logging.Folder)
            ? config.Logging.Folder
            : ScryPaths.DefaultLogsDir;

        LogLevel level;
        if (verbose)
        {
            level = LogLevel.Debug;
        }
        else if (!string.IsNullOrEmpty(config.Logging.Level) &&
                 Enum.TryParse<LogLevel>(config.Logging.Level, ignoreCase: true, out var parsed))
        {
            level = parsed;
        }
        else
        {
            level = LogLevel.Warning;
        }

        var fileName = $"{appName}-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log";
        var filePath = Path.Combine(folder, fileName);

        return new ResolvedLogging(level, folder, filePath);
    }

    /// <summary>
    /// Adds the scry file logger provider to <paramref name="builder"/>.
    /// </summary>
    public static ILoggingBuilder AddScryFile(this ILoggingBuilder builder, ResolvedLogging resolved)
    {
        builder.AddProvider(new ScryFileLoggerProvider(resolved.FilePath, resolved.Level));
        builder.SetMinimumLevel(resolved.Level);
        return builder;
    }
}
