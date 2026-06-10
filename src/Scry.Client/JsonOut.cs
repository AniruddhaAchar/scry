using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scry.Client;

/// <summary>Structured error payload printed to stdout on failure.</summary>
internal sealed record CliError(string Code, string Message, string? Hint = null);

/// <summary>
/// Renders results as indented camelCase JSON to stdout — the whole point of
/// scry is machine-readable output, so every command's success and error path
/// goes through here.
/// </summary>
internal static class JsonOut
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Local CLI output, not HTML: render quotes/slashes literally rather than
        // as \u00XX escapes so the JSON is pleasant for an agent (or human) to read.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, Options));

    public static int WriteError(CliError error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { error }, Options));
        return 1;
    }
}
