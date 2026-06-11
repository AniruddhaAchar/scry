using System.Text.Json;

namespace Scry.Core;

/// <summary>Logging-related configuration from <c>scry.config.json</c>.</summary>
public sealed record LoggingConfig
{
    /// <summary>Directory for log files; <c>null</c> means use <see cref="ScryPaths.DefaultLogsDir"/>.</summary>
    public string? Folder { get; init; }

    /// <summary>Minimum log level string (e.g. "Information", "Debug"); <c>null</c> means default.</summary>
    public string? Level { get; init; }
}

/// <summary>Root configuration record loaded from <c>~/.scry/scry.config.json</c>.</summary>
public sealed record ScryConfig
{
    /// <summary>Logging configuration block.</summary>
    public LoggingConfig Logging { get; init; } = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Loads configuration from <see cref="ScryPaths.ConfigFile"/>.
    /// Returns defaults if the file is missing or cannot be parsed.
    /// </summary>
    public static ScryConfig Load() => Load(ScryPaths.ConfigFile);

    /// <summary>
    /// Loads configuration from <paramref name="path"/>.
    /// Returns defaults if the file is missing or cannot be parsed.
    /// </summary>
    public static ScryConfig Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ScryConfig>(json, s_jsonOptions) ?? new ScryConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ScryConfig();
        }
    }
}
