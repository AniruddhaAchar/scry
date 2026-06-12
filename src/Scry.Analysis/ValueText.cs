namespace Scry.Analysis;

/// <summary>Renders string values for object/array dumps: quoted, control chars escaped, length-capped.</summary>
public static class ValueText
{
    public const int DefaultMaxLength = 256;

    /// <summary>Quotes <paramref name="s"/>, escaping <c>\ " \r \n \t</c>, and truncates with a
    /// <c>…(+N more)</c> marker beyond <paramref name="maxLength"/> characters.</summary>
    public static string Quote(string s, int maxLength = DefaultMaxLength)
    {
        var truncatedCount = 0;
        var body = s;
        if (s.Length > maxLength)
        {
            truncatedCount = s.Length - maxLength;
            body = s[..maxLength];
        }

        var sb = new System.Text.StringBuilder(body.Length + 2);
        sb.Append('"');
        foreach (var c in body)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        if (truncatedCount > 0)
        {
            sb.Append("…(+").Append(truncatedCount).Append(" more)");
        }

        return sb.ToString();
    }
}
