namespace SIL.Motif.App.Compare;

/// <summary>Lines up two token lists by their text: the longest common subsequence, after the shared start and end.</summary>
internal static class Alignment
{
    // Past this many cells the middle counts as replaced whole, to bound memory.
    private const long MaxCells = 4_000_000;

    /// <summary>For each token of <paramref name="from"/>, the index of the token of <paramref name="to"/> it matches, or -1.</summary>
    public static int[] Match(IReadOnlyList<Token> from, IReadOnlyList<Token> to)
    {
        var map = Enumerable.Repeat(-1, from.Count).ToArray();
        var head = 0;
        while (head < from.Count && head < to.Count && Equal(from[head], to[head]))
        {
            map[head] = head;
            head++;
        }
        var tail = 0;
        while (tail < from.Count - head && tail < to.Count - head && Equal(from[from.Count - 1 - tail], to[to.Count - 1 - tail]))
        {
            map[from.Count - 1 - tail] = to.Count - 1 - tail;
            tail++;
        }
        var (n, m) = (from.Count - head - tail, to.Count - head - tail);
        if (n == 0 || m == 0 || (long)n * m > MaxCells) return map;
        var lengths = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lengths[i, j] = Equal(from[head + i], to[head + j]) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var (a, b) = (0, 0);
        while (a < n && b < m)
        {
            if (Equal(from[head + a], to[head + b]))
            {
                map[head + a] = head + b;
                a++;
                b++;
            }
            else if (lengths[a + 1, b] >= lengths[a, b + 1]) a++;
            else b++;
        }
        return map;
    }

    private static bool Equal(Token left, Token right) => left.Kind == right.Kind && string.Equals(left.Text, right.Text, StringComparison.Ordinal);
}
