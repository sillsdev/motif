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

        /// <summary>Makes the field's paragraphs <paramref name="paragraphs"/>, as one undo step labelled <paramref name="label"/>, the way a source-mode return does
        /// (ADR 0035). The incoming paragraphs are prepared as pasted ones are (block features, single paragraph, tabs, normalisation). The paragraphs at the start and
        /// the end that <paramref name="same"/> says are unchanged are left alone, objects and keys and all; in the stretch between, each new paragraph takes the key
        /// of the old one at its place (ADR 0006), and its own style, properties and runs. In a single-paragraph field the paragraph keeps its style and properties
        /// (a source form of one paragraph has no paragraph syntax). False, and nothing changes, when nothing differs, the field is read-only, or the stretch may not
        /// be edited.</summary>
        public bool ReplaceParagraphs(IReadOnlyList<Paragraph> paragraphs, Func<Paragraph, Paragraph, bool> same, string label)
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
            var replacement = Matched(old, head, oldEnd, incoming, newEnd, same);
            InOneUndoStep(() =>
            {
                var d = Begin(head, oldEnd - 1, label);
                old.RemoveRange(head, oldEnd - head);
                old.InsertRange(head, replacement);
                d.After.AddRange(replacement.Select(p => p.Clone()));
                var caret = replacement.Count > 0 ? new TextPosition(head + replacement.Count - 1, replacement[replacement.Count - 1].Length)
                    : new TextPosition(Math.Min(head, old.Count - 1), head < old.Count ? 0 : old[old.Count - 1].Length);
                Finish(d, caret);
            });
            RaiseBlocksFlattened(flattening);
            return true;
        }

        /// <summary>The paragraphs of the stretch [head, oldEnd) as they become [head, newEnd) of <paramref name="incoming"/>. The longest common
        /// subsequence under <paramref name="same"/> finds the old paragraphs the source left as they were; those stay, the very objects with their
        /// keys (ADR 0006). Between two of them, a changed run of old paragraphs gives its keys to the new run's paragraphs by position, as far as
        /// both go; a paragraph beyond the old run's end is new and has no key.</summary>
        static List<Paragraph> Matched(List<Paragraph> old, int head, int oldEnd, List<Paragraph> incoming, int newEnd, Func<Paragraph, Paragraph, bool> same)
        {
            int n = oldEnd - head, m = newEnd - head;
            // lcs[i, j]: the length of the longest common subsequence of old[head + i ..] and incoming[head + j ..].
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = same(old[head + i], incoming[head + j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
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
            int a = 0, b = 0;
            while (a < n || b < m)
            {
                if (a < n && b < m && same(old[head + a], incoming[head + b]) && lcs[a, b] == lcs[a + 1, b + 1] + 1)
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
    }
}
