using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;

namespace Scry.Analysis;

/// <summary>
/// Owns the ClrMD <see cref="DataTarget"/> and <see cref="ClrRuntime"/> for one dump.
/// All members must be touched only from the analysis thread.
/// </summary>
public sealed class DumpSession : IDisposable
{
    private DataTarget? _dataTarget;

    public ClrRuntime Runtime { get; private set; } = null!;
    public string RuntimeVersion { get; private set; } = string.Empty;

    /// <summary>Loads the dump, resolves the DAC, and opens the runtime. Throws on failure.</summary>
    public void Load(string dumpPath)
    {
        var dt = DataTarget.LoadDump(dumpPath);
        try
        {
            // x64 + arm64 only for v0.0.1; reject x86 dumps (CLAUDE.md core constraint #3).
            if (dt.DataReader.Architecture == Architecture.X86)
            {
                throw new NotSupportedException("x86 dumps are not supported (x64/arm64 only).");
            }

            var clrInfo = dt.ClrVersions.FirstOrDefault()
                ?? throw new InvalidOperationException("no .NET runtime found in dump");

            Runtime = clrInfo.CreateRuntime();
            RuntimeVersion = clrInfo.Version.ToString();
            _dataTarget = dt;
        }
        catch
        {
            dt.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Runtime?.Dispose();
        _dataTarget?.Dispose();
    }
}
