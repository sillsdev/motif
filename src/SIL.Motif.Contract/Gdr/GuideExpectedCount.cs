using System.Globalization;

namespace SIL.Motif.Contract.Gdr;

/// <summary>
/// The count a <c>motif-query</c> block's <c>expect</c> key declares: a number (<c>127</c>), an inclusive range (<c>100..150</c>)
/// or a bound (<c>&gt;=1</c>, <c>&gt;1</c>, <c>&lt;=5</c>, <c>&lt;5</c>). The count is the block's row count, as the check reports it.
/// </summary>
public sealed record GuideExpectedCount(long? Min, long? Max, string Text)
{
    /// <summary>True when <paramref name="count"/> lies inside the expectation.</summary>
    public bool Contains(long count) => (Min is null || count >= Min) && (Max is null || count <= Max);

    /// <inheritdoc/>
    public override string ToString() => Text;

    /// <summary>Reads an <c>expect</c> value; false for anything that is not one of the forms above.</summary>
    public static bool TryParse(string text, out GuideExpectedCount? expected)
    {
        expected = null;
        text = text.Trim();
        if (text.Length == 0) return false;
        if (text.Contains("..", StringComparison.Ordinal))
        {
            var parts = text.Split("..");
            if (parts.Length != 2 || !Number(parts[0], out var low) || !Number(parts[1], out var high) || low > high) return false;
            expected = new GuideExpectedCount(low, high, text);
            return true;
        }
        foreach (var (op, lower) in new[] { (">=", true), ("<=", false), (">", true), ("<", false) })
        {
            if (!text.StartsWith(op, StringComparison.Ordinal)) continue;
            if (!Number(text[op.Length..], out var bound)) return false;
            var exclusive = op.Length == 1;
            if (lower) expected = new GuideExpectedCount(exclusive ? bound + 1 : bound, null, text);
            else
            {
                if (exclusive && bound == 0) return false;
                expected = new GuideExpectedCount(null, exclusive ? bound - 1 : bound, text);
            }
            return true;
        }
        if (!Number(text, out var exact)) return false;
        expected = new GuideExpectedCount(exact, exact, text);
        return true;
    }

    private static bool Number(string text, out long value) =>
        long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
