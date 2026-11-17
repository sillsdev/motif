using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown
{
    /// <summary>Suggests the names of a host's list closest to a misspelt one, the way the reader fills <see cref="MarkdownProblem.Nearest"/>.
    /// A host that builds its own <see cref="MarkdownProblem"/> uses it to give the same suggestions.</summary>
    public static class NearestNamesFinder
    {
        /// <summary>The names of <paramref name="known"/> closest to <paramref name="name"/>, nearest first (at most three), or null when none is
        /// close: within a third of the length, at least one edit, a swap of two neighbours counting as one.</summary>
        public static IReadOnlyList<string>? Find(string name, IEnumerable<string> known)
        {
            int limit = Math.Max(1, name.Length / 3);
            var found = new List<KeyValuePair<int, string>>();
            foreach (var candidate in known)
            {
                int d = EditDistance(name.ToLowerInvariant(), candidate.ToLowerInvariant());
                if (d <= limit) found.Add(new KeyValuePair<int, string>(d, candidate));
            }
            if (found.Count == 0) return null;
            // Stable: equally near names keep the host's order.
            var ordered = new List<string>();
            for (int d = 0; d <= limit && ordered.Count < 3; d++)
                foreach (var f in found) if (f.Key == d && ordered.Count < 3) ordered.Add(f.Value);
            return ordered;
        }

        /// <summary>Edits to turn one string into the other: insert, delete, substitute, or swap two neighbours.</summary>
        static int EditDistance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
