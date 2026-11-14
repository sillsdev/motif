namespace SIL.Motif.Contract.Query;

/// <summary>Parses the CLI's bounded selector syntax into the shared typed record.</summary>
public static class QueryConditionParser
{
    /// <summary>Parses one selector; its column and operator are checked when the query is evaluated.</summary>
    public static QueryCondition Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var end = 0;
        while (end < expression.Length && (char.IsLetterOrDigit(expression[end]) || expression[end] is '.' or '-' or '_')) end++;
        var col = expression[..end];
        var remainder = expression[end..].TrimStart();
        foreach (var (marker, op) in new[] { ("is ", "is"), ("in ", "in"), ("not ", "not"), ("has ", "has"),
                     ("=", "is"), ("~", "~"), ("<", "<"), (">", ">") })
        {
            if (col.Length == 0 || !remainder.StartsWith(marker, StringComparison.Ordinal)) continue;
            return new QueryCondition(col, op, remainder[marker.Length..].Trim());
        }
        throw new ArgumentException("A selector needs a column, operator and value.");
    }
}
