namespace Scry.Analysis.Model;

/// <summary>A paged slice of results plus the total match count.</summary>
public sealed record Page<T>(long TotalMatches, bool Truncated, IReadOnlyList<T> Items);

/// <summary>Factory for <see cref="Page{T}"/>. Single-pass over the source.</summary>
public static class Page
{
    public const int DefaultLimit = 1000;

    public static Page<T> From<T>(IEnumerable<T> source, int offset, int limit)
    {
        if (limit <= 0)
        {
            limit = DefaultLimit;
        }

        if (offset < 0)
        {
            offset = 0;
        }

        long total = 0;
        var items = new List<T>();
        foreach (var item in source)
        {
            if (total >= offset && items.Count < limit)
            {
                items.Add(item);
            }

            total++;
        }

        var truncated = total > offset + items.Count;
        return new Page<T>(total, truncated, items);
    }
}
