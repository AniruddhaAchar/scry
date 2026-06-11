using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis;

/// <summary>
/// A compact, immutable snapshot of the managed heap, built once per dump (dumps never change).
/// Parallel arrays keep millions of objects cheap; type names are interned in a small type table.
/// All query methods are pure (no ClrMD) so they are fast and unit-testable.
/// </summary>
public sealed class HeapSnapshot
{
    private readonly string[] _typeNames;
    private readonly ulong[] _typeMethodTables;
    private readonly ulong[] _addresses;
    private readonly int[] _typeIndex;
    private readonly ulong[] _sizes;
    private readonly int[] _exceptionIndices;

    public HeapSnapshot(
        string[] typeNames,
        ulong[] typeMethodTables,
        ulong[] addresses,
        int[] typeIndex,
        ulong[] sizes,
        int[] exceptionIndices)
    {
        _typeNames = typeNames;
        _typeMethodTables = typeMethodTables;
        _addresses = addresses;
        _typeIndex = typeIndex;
        _sizes = sizes;
        _exceptionIndices = exceptionIndices;
    }

    public int ObjectCount => _addresses.Length;

    public int ExceptionCount => _exceptionIndices.Length;

    /// <summary>Walks the heap once (ClrMD) and builds the snapshot. Cancellation discards the partial build.</summary>
    public static HeapSnapshot Build(ClrRuntime runtime, CancellationToken ct)
    {
        var indexByMethodTable = new Dictionary<ulong, int>();
        var typeNames = new List<string>();
        var typeMethodTables = new List<ulong>();
        var addresses = new List<ulong>();
        var typeIndex = new List<int>();
        var sizes = new List<ulong>();
        var exceptionIndices = new List<int>();

        var seen = 0;
        foreach (var obj in runtime.Heap.EnumerateObjects())
        {
            if ((seen++ & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            var type = obj.Type;
            if (type is null)
            {
                continue; // free / unrooted-unknown
            }

            var mt = type.MethodTable;
            if (!indexByMethodTable.TryGetValue(mt, out var ti))
            {
                ti = typeNames.Count;
                indexByMethodTable[mt] = ti;
                typeNames.Add(type.Name ?? "<unknown>");
                typeMethodTables.Add(mt);
            }

            var pos = addresses.Count;
            addresses.Add(obj.Address);
            typeIndex.Add(ti);
            sizes.Add(obj.Size);
            if (obj.IsException)
            {
                exceptionIndices.Add(pos);
            }
        }

        return new HeapSnapshot(
            typeNames.ToArray(),
            typeMethodTables.ToArray(),
            addresses.ToArray(),
            typeIndex.ToArray(),
            sizes.ToArray(),
            exceptionIndices.ToArray());
    }

    /// <summary>Per-type aggregates (optionally filtered by a case-sensitive substring), sorted by total size desc.</summary>
    public IReadOnlyList<HeapTypeStat> Stat(string? typeFilter)
    {
        // typeIndex → (count, totalSize)
        var counts = new long[_typeNames.Length];
        var totals = new ulong[_typeNames.Length];
        for (var i = 0; i < _addresses.Length; i++)
        {
            var ti = _typeIndex[i];
            counts[ti]++;
            totals[ti] += _sizes[i];
        }

        var result = new List<HeapTypeStat>();
        for (var ti = 0; ti < _typeNames.Length; ti++)
        {
            if (counts[ti] == 0)
            {
                continue;
            }

            if (!HeapMatch.Matches(_typeNames[ti], typeFilter))
            {
                continue;
            }

            result.Add(new HeapTypeStat(_typeNames[ti], _typeMethodTables[ti], counts[ti], totals[ti]));
        }

        result.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));
        return result;
    }

    /// <summary>Paged object listing filtered by a case-sensitive substring on the type name.</summary>
    public Page<HeapObject> Objects(string typeFilter, int offset, int limit)
    {
        // Pre-compute matching type indices over the small type table, then scan objects.
        var matchedType = new bool[_typeNames.Length];
        for (var ti = 0; ti < _typeNames.Length; ti++)
        {
            matchedType[ti] = HeapMatch.Matches(_typeNames[ti], typeFilter);
        }

        return Page.From(EnumerateObjects(matchedType), offset, limit);
    }

    private IEnumerable<HeapObject> EnumerateObjects(bool[] matchedType)
    {
        for (var i = 0; i < _addresses.Length; i++)
        {
            if (matchedType[_typeIndex[i]])
            {
                yield return new HeapObject(_addresses[i], _typeNames[_typeIndex[i]], _sizes[i]);
            }
        }
    }

    /// <summary>Paged addresses of exception objects (detail is read on demand from ClrMD by the caller).</summary>
    public Page<ulong> ExceptionAddresses(int offset, int limit) =>
        Page.From(_exceptionIndices.Select(pos => _addresses[pos]), offset, limit);
}
