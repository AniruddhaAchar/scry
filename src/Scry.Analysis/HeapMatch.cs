namespace Scry.Analysis;

/// <summary>Type-name matching for heap queries: case-sensitive substring (SOS `!dumpheap -type` parity).</summary>
public static class HeapMatch
{
    public static bool Matches(string typeName, string? filter) =>
        string.IsNullOrEmpty(filter) || typeName.Contains(filter, System.StringComparison.Ordinal);
}
