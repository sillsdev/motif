using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIL.TextEditing
{
    // Paste's preparation of incoming paragraphs, shared with a source-mode return (ADR 0035), so text that comes back from a field's Markdown is held to the
    // same rules as text pasted into it: the field's block features, single-paragraph and single-line rules, no tabs, the host's normalisation form.
    public sealed partial class DocumentEditor
    {
        /// <summary>Copies of the incoming paragraphs as the field may hold them: no editability of their own (semi-editable when they land in such a run), blocks
        /// the field does not allow made plain, one paragraph in a single-paragraph field, no tabs, and the host's normalisation form.</summary>
        List<Paragraph> PrepareFragment(IEnumerable<Paragraph> incoming, bool semi, out BlockFlattening? flattening)
        {
            var fragment = incoming.Select(p => p.Clone()).ToList();
            foreach (var p in fragment)
                foreach (var r in p.Runs)
                {
                    r.Props.IntProps.Remove(TextPropertyNumbers.Editable);
                    if (semi) r.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableSemi);
                }
            // A block the field does not allow becomes plain paragraphs first (ADR 0033), so a table's cell tabs are spaces before tabs are removed below.
            flattening = FlattenFragment(fragment);
            if (SingleParagraph)
            {
                if (fragment.Count > 1) fragment = JoinIntoOne(fragment);
                foreach (var p in fragment) SeparatorsToSpaces(p);
                if (SingleLine) LineBreaksToSpaces(fragment);
            }
            else
            {
                foreach (var p in fragment) RemoveTabs(p);
            }
            // Every insertion passes through here or TypeText, so the host's normalisation form always applies.
            if (Document.Normalization is NormalizationForm form)
            {
                fragment = fragment.Select(p => p.Clone()).ToList();
                foreach (var p in fragment)
                    foreach (var r in p.Runs)
                        if (!r.IsObject) r.Text = TextNormalizer.Normalize(r.Text, form);
            }
            return fragment;
        }

        /// <summary>Without a fingerprint, the most calls of <c>same</c> the matching of a changed stretch may make (its length times the source's): beyond it the
        /// stretch's keys go by position, so a long text with changes far apart is not compared pair by pair on the UI thread.</summary>
        public const int PairwiseComparisonBudget = 10000;

        /// <summary>With a fingerprint, the largest matching table: the changed stretch's old paragraphs times its new ones. Beyond it keys go by position.</summary>
        const long FingerprintTableBudget = 2100L * 2100;

        /// <summary>Makes the field's paragraphs <paramref name="paragraphs"/>, as one undo step labelled <paramref name="label"/>, the way a source-mode return
        /// does (ADR 0035), comparing paragraphs with <paramref name="same"/> alone (see the overload that takes a fingerprint).</summary>
        public bool ReplaceParagraphs(IReadOnlyList<Paragraph> paragraphs, Func<Paragraph, Paragraph, bool> same, string label) =>
            ReplaceParagraphs(paragraphs, same, null, label);

        /// <summary>Makes the field's paragraphs <paramref name="paragraphs"/>, as one undo step labelled <paramref name="label"/>, the way a source-mode return does
        /// (ADR 0035). The incoming paragraphs are prepared as pasted ones are (block features, single paragraph, tabs, normalisation). The paragraphs at the start and
        /// the end that <paramref name="same"/> says are unchanged are left alone, objects and keys and all. In the stretch between, the longest common subsequence
        /// under <paramref name="same"/> finds the old paragraphs the source left as they were, and those stay too; a changed paragraph takes the key of the old one
        /// it replaces (ADR 0006), and its own style, properties and runs, so keys stay on their own text when paragraphs are added or removed. A moved paragraph
        /// is a removal and an addition, so it loses its key, as cut and paste does.
        /// <para><paramref name="fingerprint"/>, when given, must be the same for any two paragraphs <paramref name="same"/> accepts
        /// (<see cref="ISourceFormFingerprint"/>); <paramref name="same"/> is then taken to be an equivalence, the paragraphs are grouped once, and a stretch of up to
        /// whose old paragraphs times its new ones is at most 2100² is matched in a few comparisons a paragraph. Without one, the stretch is compared pair by pair up to
        /// <see cref="PairwiseComparisonBudget"/> comparisons. Beyond either limit the stretch's keys go by position: each new paragraph takes the key of the old one
        /// at its place.</para>
        /// In a single-paragraph field the paragraph keeps its style and properties (a source form of one paragraph has no paragraph syntax). False, and nothing
        /// changes, when nothing differs, the field is read-only, or the stretch may not be edited. An exception from a <see cref="Committed"/> handler reaches the
        /// caller after the change has been made and announced.</summary>
        public bool ReplaceParagraphs(IReadOnlyList<Paragraph> paragraphs, Func<Paragraph, Paragraph, bool> same, Func<Paragraph, string>? fingerprint, string label)
        {
            CheckAccess();
            if (paragraphs == null) throw new ArgumentNullException(nameof(paragraphs));
            if (same == null) throw new ArgumentNullException(nameof(same));
            if (Refusing) return false;
            var old = Document.Paragraphs;
            var incoming = PrepareFragment(paragraphs, false, out var flattening);
            if (incoming.Count == 0) incoming.Add(new Paragraph { DefaultWs = old.Count > 0 ? old[0].DefaultWs : string.Empty });
            if (SingleParagraph && old.Count > 0)
            {
                var kept = old[0];
                foreach (var p in incoming)
                {
                    p.Style = kept.Style;
                    p.Props = kept.Props.Clone();
                    p.Rtl = kept.Rtl;
                }
            }
            int head = 0;
            while (head < old.Count && head < incoming.Count && same(old[head], incoming[head])) head++;
            int tail = 0;
            while (tail < old.Count - head && tail < incoming.Count - head && same(old[old.Count - 1 - tail], incoming[incoming.Count - 1 - tail])) tail++;
            int oldEnd = old.Count - tail, newEnd = incoming.Count - tail;
            if (head == oldEnd && head == newEnd) return false;
            if (head < oldEnd && !CanEditRange(new TextPosition(head, 0), new TextPosition(oldEnd - 1, old[oldEnd - 1].Length))) return false;
            var replacement = Matched(old, head, oldEnd, incoming, newEnd, same, fingerprint);
            bool changed = false;
            try
            {
                InOneUndoStep(() =>
                {
                    var d = Begin(head, oldEnd - 1, label);
                    old.RemoveRange(head, oldEnd - head);
                    old.InsertRange(head, replacement);
                    changed = true;
                    d.After.AddRange(replacement.Select(p => p.Clone()));
                    var caret = replacement.Count > 0 ? new TextPosition(head + replacement.Count - 1, replacement[replacement.Count - 1].Length)
                        : new TextPosition(Math.Min(head, old.Count - 1), head < old.Count ? 0 : old[old.Count - 1].Length);
                    Finish(d, caret);
                });
            }
            finally
            {
                // A host's commit handler that throws comes after the change, so the flattening the change made is still reported.
                if (changed) RaiseBlocksFlattened(flattening);
            }
            return true;
        }

        /// <summary>The paragraphs of the stretch [head, oldEnd) as they become [head, newEnd) of <paramref name="incoming"/>. The longest common
        /// subsequence under <paramref name="same"/> finds the old paragraphs the source left as they were; those stay, the very objects with their
        /// keys (ADR 0006). Between two of them, a changed run of old paragraphs gives its keys to the new run's paragraphs by position, as far as
        /// both go; a paragraph beyond the old run's end is new and has no key. A stretch over its budget is one changed run.</summary>
        static List<Paragraph> Matched(List<Paragraph> old, int head, int oldEnd, List<Paragraph> incoming, int newEnd, Func<Paragraph, Paragraph, bool> same,
            Func<Paragraph, string>? fingerprint)
        {
            int n = oldEnd - head, m = newEnd - head;
            var result = new List<Paragraph>(m);
            var oldRun = new List<Paragraph>();
            var newRun = new List<Paragraph>();
            void Flush()
            {
                for (int k = 0; k < newRun.Count; k++)
                {
                    var p = newRun[k].Clone(false);
                    if (k < oldRun.Count) p.Key = oldRun[k].Key;
                    p.Normalize();
                    result.Add(p);
                }
                oldRun.Clear();
                newRun.Clear();
            }
            Func<int, int, bool> eq;
            if (fingerprint != null && (long)n * m <= FingerprintTableBudget)
            {
                var (oldClass, newClass) = Classes(old, head, n, incoming, m, same, fingerprint);
                eq = (i, j) => oldClass[i] == newClass[j];
            }
            else if (fingerprint == null && (long)n * m <= PairwiseComparisonBudget)
            {
                eq = (i, j) => same(old[head + i], incoming[head + j]);
            }
            else
            {
                oldRun.AddRange(old.GetRange(head, n));
                newRun.AddRange(incoming.GetRange(head, m));
                Flush();
                return result;
            }
            // lcs[i, j]: the length of the longest common subsequence of old[head + i ..] and incoming[head + j ..]; at most the shorter side, which the budgets keep
            // near two thousand, so a short holds it and the table is half the size.
            var lcs = new short[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = eq(i, j) ? (short)(lcs[i + 1, j + 1] + 1) : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int a = 0, b = 0;
            while (a < n || b < m)
            {
                if (a < n && b < m && eq(a, b) && lcs[a, b] == lcs[a + 1, b + 1] + 1)
                {
                    Flush();
                    result.Add(old[head + a]);
                    a++;
                    b++;
                }
                else if (b < m && (a == n || lcs[a, b + 1] >= lcs[a + 1, b])) newRun.Add(incoming[head + b++]);
                else oldRun.Add(old[head + a++]);
            }
            Flush();
            return result;
        }

        /// <summary>A class number for each paragraph of both stretches, equal for paragraphs <paramref name="same"/> calls equivalent. Each paragraph is compared
        /// only with one paragraph of each class already found under its fingerprint, so a text of distinct paragraphs costs about one comparison a paragraph,
        /// and a text of many empty or repeated paragraphs one per kind.</summary>
        static (int[] Old, int[] New) Classes(List<Paragraph> old, int head, int n, List<Paragraph> incoming, int m, Func<Paragraph, Paragraph, bool> same,
            Func<Paragraph, string> fingerprint)
        {
            var found = new Dictionary<string, List<(Paragraph Paragraph, int Class)>>(StringComparer.Ordinal);
            int classes = 0;
            int ClassOf(Paragraph p)
            {
                string print = fingerprint(p) ?? string.Empty;
                if (!found.TryGetValue(print, out var kinds)) found[print] = kinds = new List<(Paragraph, int)>();
                foreach (var kind in kinds)
                    if (same(kind.Paragraph, p)) return kind.Class;
                kinds.Add((p, classes));
                return classes++;
            }
            var oldClass = new int[n];
            for (int i = 0; i < n; i++) oldClass[i] = ClassOf(old[head + i]);
            var newClass = new int[m];
            for (int j = 0; j < m; j++) newClass[j] = ClassOf(incoming[head + j]);
            return (oldClass, newClass);
        }
    }
}
