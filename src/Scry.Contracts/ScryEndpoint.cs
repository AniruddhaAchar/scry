using System.Security.Cryptography;
using System.Text;

namespace Scry.Contracts;

/// <summary>
/// Deterministic endpoint naming shared by the <c>scry</c> client and the
/// <c>scryd</c> host. Both derive the same id from a dump path so any client
/// invocation for a given dump finds (or spawns) the one host serving it.
/// </summary>
public static class ScryEndpoint
{
    /// <summary>
    /// Derives a stable, filesystem-safe id of the form <c>scry-&lt;16 hex&gt;</c>
    /// from a dump path. Paths are fully-qualified first, and compared
    /// case-insensitively on Windows, so equivalent spellings of the same dump
    /// map to the same host.
    /// </summary>
    public static string DeriveId(string dumpPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dumpPath);

        var full = Path.GetFullPath(dumpPath);
        var normalized = OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexStringLower(digest.AsSpan(0, 8));
        return $"scry-{hash}";
    }

    /// <summary>Windows named-pipe name (used as <c>\\.\pipe\&lt;name&gt;</c>).</summary>
    public static string PipeName(string id) => id;

    /// <summary>Unix domain socket path for the host endpoint.</summary>
    public static string SocketPath(string id) =>
        Path.Combine(Path.GetTempPath(), id + ".sock");

    /// <summary>Lock file guarding the spawn race (one host per dump).</summary>
    public static string LockFilePath(string id) =>
        Path.Combine(Path.GetTempPath(), id + ".lock");
}
