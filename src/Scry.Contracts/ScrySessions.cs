using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scry.Contracts;

/// <summary>Persistent descriptor for a live <c>scryd</c> session.</summary>
public sealed record SessionDescriptor(
    string Handle,
    string DumpPath,
    int Pid,
    DateTimeOffset StartedUtc,
    string ScrydVersion);

/// <summary>
/// Shared session registry: both <c>scryd</c> (register/unregister) and <c>scry</c>
/// (list/resolve/prune) use this to discover live sessions without a central daemon.
/// </summary>
public static class ScrySessions
{
    private static readonly JsonSerializerOptions s_write = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions s_read = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Directory where session descriptors are stored. Reads <c>SCRY_SESSIONS_DIR</c>
    /// env var on every access (so tests can override per-test).
    /// </summary>
    public static string RegistryDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SCRY_SESSIONS_DIR");
            return !string.IsNullOrEmpty(env)
                ? env
                : Path.Combine(Path.GetTempPath(), "scry", "sessions");
        }
    }

    /// <summary>
    /// Writes a descriptor file for <paramref name="d"/> atomically.
    /// </summary>
    public static void Register(SessionDescriptor d)
    {
        var dir = RegistryDir;
        Directory.CreateDirectory(dir);

        var path = DescriptorPath(dir, d.Handle);
        var tmp = path + ".tmp";

        var json = JsonSerializer.Serialize(d, s_write);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Removes the descriptor file for <paramref name="handle"/>; silently ignores missing files.
    /// </summary>
    public static void Unregister(string handle)
    {
        var path = DescriptorPath(RegistryDir, handle);
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Swallow — best-effort cleanup.
        }
    }

    /// <summary>
    /// Returns all live sessions. Stale (dead-pid) files are deleted lazily.
    /// Malformed or unreadable files are skipped and deleted.
    /// </summary>
    public static IReadOnlyList<SessionDescriptor> List()
    {
        var dir = RegistryDir;
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var results = new List<SessionDescriptor>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            SessionDescriptor? descriptor;
            try
            {
                var json = File.ReadAllText(file);
                descriptor = JsonSerializer.Deserialize<SessionDescriptor>(json, s_read);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                TryDelete(file);
                continue;
            }

            if (descriptor is null)
            {
                TryDelete(file);
                continue;
            }

            if (IsAlive(descriptor))
            {
                results.Add(descriptor);
            }
            else
            {
                TryDelete(file);
            }
        }

        return results;
    }

    /// <summary>
    /// Returns <c>true</c> if a process with <see cref="SessionDescriptor.Pid"/> is still running.
    /// </summary>
    public static bool IsAlive(SessionDescriptor d)
    {
        try
        {
            var proc = Process.GetProcessById(d.Pid);
            return !proc.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string DescriptorPath(string dir, string handle) =>
        Path.Combine(dir, handle + ".json");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore.
        }
    }
}
