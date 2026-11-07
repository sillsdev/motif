using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing
{
    // Find and replace (ADR 0021, ADR 0025): the host's dialog owns the options and the buttons; the editor searches, selects,
    // and replaces as single undo steps. A match is a TextRange inside one paragraph. Text a line limit hides is not searched,
    // because a selection cannot reach it (ADR 0024).
    public sealed partial class DocumentEditor
    {
        /// <summary>True when the last find, find-all or replace gave up on a regular expression that ran past
        /// <see cref="FindOptions.RegexTimeout"/>: it found nothing, so the host can say the pattern is too slow. Each search starts it afresh.</summary>
        public bool FindTimedOut { get; private set; }

        FindPlan NewPlan(FindOptions options)
        {
            FindTimedOut = false;
            return FindPlan.Create(options, WordRules);
        }

        List<Hit> HitsIn(int paragraph, FindPlan plan, bool overlapping, string? replacement = null)
        {
            var hits = plan.Matches(Document.Paragraphs[paragraph], overlapping, replacement);
            if (plan.TimedOut) FindTimedOut = true;
            int limit = LimitOf(paragraph);
            if (limit < Document.Paragraphs[paragraph].Length) hits.RemoveAll(h => h.End > limit);
            return hits;
        }

        /// <summary>Finds the next match (<paramref name="forward"/>) or the previous one from the selection: a forward search
        /// starts at the end of the selection, a backward search ends at its start, as FieldWorks' Find Next and Find
        /// Previous do. With <paramref name="wrap"/> it continues from the other end of the document, and
        /// <paramref name="wrapped"/> says it did; the host decides whether to ask the user first. Nothing is selected: call
        /// <see cref="SelectRange"/>. False when there is no match or the options cannot search
        /// (<see cref="FindOptions.IsValid"/>).</summary>
        public bool TryFind(FindOptions options, bool forward, bool wrap, out TextRange match, out bool wrapped)
        {
            var plan = NewPlan(options);
            bool found = TryFind(plan, forward, wrap, out match, out wrapped);
            if (plan.TimedOut) { match = default; wrapped = false; return false; }   // a search that timed out anywhere finds nothing
            return found;
        }

        bool TryFind(FindPlan plan, bool forward, bool wrap, out TextRange match, out bool wrapped)
        {
            match = default;
            wrapped = false;
            if (!plan.Valid) return false;
            Ordered(out var a, out var b);
            int count = Document.Paragraphs.Count;
            if (forward)
            {
                for (int pi = b.Paragraph; pi < count; pi++)
                    foreach (var h in HitsIn(pi, plan, true))
                    {
                        if (pi == b.Paragraph && h.Start < b.Offset) continue;
                        match = new TextRange(pi, h.Start, h.End);
                        return true;
                    }
                if (!wrap) return false;
                for (int pi = 0; pi <= b.Paragraph; pi++)
                {
                    var hits = HitsIn(pi, plan, true);
                    if (hits.Count == 0) continue;
                    match = new TextRange(pi, hits[0].Start, hits[0].End);
                    wrapped = true;
                    return true;
                }
                return false;
            }
            for (int pi = a.Paragraph; pi >= 0; pi--)
                foreach (var h in Enumerable.Reverse(HitsIn(pi, plan, true)))
                {
                    if (pi == a.Paragraph && h.End > a.Offset) continue;
                    match = new TextRange(pi, h.Start, h.End);
                    return true;
                }
            if (!wrap) return false;
            for (int pi = count - 1; pi >= a.Paragraph; pi--)
            {
                var hits = HitsIn(pi, plan, true);
                if (hits.Count == 0) continue;
                match = new TextRange(pi, hits[hits.Count - 1].Start, hits[hits.Count - 1].End);
                wrapped = true;
                return true;
            }
            return false;
        }

        /// <summary>Every match in the document, in order and without overlaps (the highlight of all matches, and what
        /// <see cref="ReplaceAll"/> replaces).</summary>
        public IReadOnlyList<TextRange> FindAll(FindOptions options)
        {
            var result = new List<TextRange>();
            var plan = NewPlan(options);
            if (!plan.Valid) return result;
            for (int pi = 0; pi < Document.Paragraphs.Count; pi++)
                foreach (var h in HitsIn(pi, plan, false)) result.Add(new TextRange(pi, h.Start, h.End));
            if (plan.TimedOut) result.Clear();   // a search that timed out anywhere finds nothing
            return result;
        }

        /// <summary>The matches in one paragraph, in order and without overlaps.</summary>
        public IReadOnlyList<TextRange> FindAllIn(int paragraph, FindOptions options)
        {
            var result = FindAllIn(paragraph, options, out bool timedOut);
            FindTimedOut = timedOut;
            return result;
        }

        /// <summary>As <see cref="FindAllIn(int, FindOptions)"/>, saying whether the search timed out instead of setting <see cref="FindTimedOut"/>, so the
        /// highlight the field draws never disturbs what the host's own searches reported.</summary>
        public IReadOnlyList<TextRange> FindAllIn(int paragraph, FindOptions options, out bool timedOut)
        {
            var result = new List<TextRange>();
            var plan = FindPlan.Create(options, WordRules);
            timedOut = false;
            if (!plan.Valid) return result;
            foreach (var h in plan.Matches(Document.Paragraphs[paragraph], false)) if (h.End <= LimitOf(paragraph)) result.Add(new TextRange(paragraph, h.Start, h.End));
            timedOut = plan.TimedOut;
            if (timedOut) result.Clear();
            return result;
        }

        /// <summary>True when the range is exactly a match of the options now (FieldWorks checks this before it replaces the
        /// selection: the user may have moved it since the find).</summary>
        public bool IsMatch(TextRange range, FindOptions options)
        {
            var plan = NewPlan(options);
            return plan.Valid && range.Start.Paragraph == range.End.Paragraph && !range.IsEmpty && FindHit(range, plan, null) != null;
        }

        Hit? FindHit(TextRange range, FindPlan plan, string? replacement)
        {
            int pi = range.Start.Paragraph;
            if (pi < 0 || pi >= Document.Paragraphs.Count) return null;
            foreach (var h in HitsIn(pi, plan, true, replacement))
                if (h.Start == range.Start.Offset && h.End == range.End.Offset) return h;
            return null;
        }

        /// <summary>The selection as a range, in document order.</summary>
        public TextRange SelectionRange
        {
            get
            {
                Ordered(out var a, out var b);
                return new TextRange(a, b);
            }
        }

        /// <summary>Selects a range exactly, even in text that may not be edited (a find match there is still selectable; the caret
        /// is not moved to the nearest editable place as <see cref="SetCaret"/> would). It ends the open undo step.</summary>
        public void SelectRange(TextRange range)
        {
            CloseUndoGroup();
            Anchor = range.Start;
            SetCaret(range.End, true);
        }

        /// <summary>True when the range can be replaced: the field is not read-only and every character in it is fully editable
        /// (FieldWorks refuses a replacement in read-only and in semi-editable text alike, as it refuses formatting there).</summary>
        public bool CanReplace(TextRange range) => CanFormatRange(range.Start, range.End);

        /// <summary>Replaces a match with <paramref name="replacement"/> as one undo step. The new text takes the properties of the
        /// first run of the match, with <paramref name="writingSystem"/> or <paramref name="characterStyle"/> (the empty string
        /// removes the style) when given; embedded objects inside the match are kept, after the new text. For a regular
        /// expression the replacement may refer to groups (<c>$1</c>). When the options search for a writing system or style
        /// alone and the replacement is empty, the text stays and only the writing system or style changes. A line break in the
        /// replacement becomes a hard line break. False, and nothing changes, when the range is not a match of the options now or
        /// cannot be replaced (<see cref="CanReplace"/>). The caret ends after the new text.</summary>
        public bool ReplaceMatch(TextRange range, FindOptions options, string replacement, string? writingSystem = null, string? characterStyle = null)
        {
            CheckAccess();
            var plan = NewPlan(options);
            if (!plan.Valid || range.IsEmpty || range.Start.Paragraph != range.End.Paragraph) return false;
            var hit = FindHit(range, plan, replacement);
            if (hit == null) return false;
            return ReplaceHit(range.Start.Paragraph, hit.Value, options, replacement, writingSystem, characterStyle) != int.MinValue;
        }

        /// <summary>Replaces the selection when it is a match of the options (<see cref="IsMatch"/>), as
        /// <see cref="ReplaceMatch"/> does; otherwise does nothing and returns false. The host then finds the next match.</summary>
        public bool ReplaceCurrent(FindOptions options, string replacement, string? writingSystem = null, string? characterStyle = null) =>
            HasSelection && ReplaceMatch(SelectionRange, options, replacement, writingSystem, characterStyle);

        /// <summary>Replaces every match in the document, in one undo step, and returns how many it replaced. Matches that cannot be
        /// replaced (<see cref="CanReplace"/>) are left. The text that was put in is not searched again. The selection is kept,
        /// moved with the text. The replacement is as for <see cref="ReplaceMatch"/>.</summary>
        public int ReplaceAll(FindOptions options, string replacement, string? writingSystem = null, string? characterStyle = null)
        {
            CheckAccess();
            var plan = NewPlan(options);
            if (!plan.Valid || IsReadOnly) return 0;
            var todo = new List<(int Paragraph, Hit Hit)>();
            for (int pi = 0; pi < Document.Paragraphs.Count; pi++)
                foreach (var h in HitsIn(pi, plan, false, replacement))
                    if (CanReplace(new TextRange(pi, h.Start, h.End))) todo.Add((pi, h));
            if (plan.TimedOut || todo.Count == 0) return 0;   // a search that timed out anywhere replaces nothing
            var anchor = Anchor;
            var caret = Caret;
            var deltas = new int[todo.Count];
            // From the last match to the first, so an earlier replacement never moves a later match.
            InOneUndoStep(() =>
            {
                for (int i = todo.Count - 1; i >= 0; i--)
                    deltas[i] = ReplaceHit(todo[i].Paragraph, todo[i].Hit, options, replacement, writingSystem, characterStyle);
            });
            TextPosition Move(TextPosition p)
            {
                int shift = 0;
                for (int i = 0; i < todo.Count && todo[i].Paragraph <= p.Paragraph; i++)
                {
                    if (todo[i].Paragraph != p.Paragraph || deltas[i] == int.MinValue) continue;
                    var h = todo[i].Hit;
                    if (h.End <= p.Offset) shift += deltas[i];
                    else if (h.Start < p.Offset) return new TextPosition(p.Paragraph, h.Start + shift);
                }
                return new TextPosition(p.Paragraph, p.Offset + shift);
            }
            Anchor = Move(anchor);
            SetCaret(Move(caret), true);
            return deltas.Count(d => d != int.MinValue);
        }

        /// <summary>Replaces one hit; returns the change in the paragraph's length, or <see cref="int.MinValue"/> when it was
        /// refused.</summary>
        int ReplaceHit(int paragraph, Hit hit, FindOptions options, string replacement, string? writingSystem, string? characterStyle)
        {
            var a = new TextPosition(paragraph, hit.Start);
            var b = new TextPosition(paragraph, hit.End);
            if (!CanFormatRange(a, b)) return int.MinValue;
            var para = Document.Paragraphs[paragraph];
            int before = para.Length;
            var slice = para.Slice(hit.Start, hit.End);
            var model = slice.FirstOrDefault(r => !r.IsObject) ?? slice.FirstOrDefault();
            var text = hit.Expansion ?? replacement;
            // A plain fragment: a one-paragraph fragment's style and properties are discarded (the paragraph under the replaced text keeps its own), and a styled one
            // would be taken for a pasted block and flattened (ADR 0033), raising a false BlocksFlattened inside a block the field keeps.
            var frag = new Paragraph { DefaultWs = para.DefaultWs };
            void Restyle(Run r)
            {
                if (writingSystem != null) r.Ws = writingSystem;
                if (characterStyle == null) return;
                if (characterStyle.Length == 0) r.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                else r.Props.SetStr(TextPropertyNumbers.NamedStyle, characterStyle);
            }
            if (options.PropertiesOnly && text.Length == 0)
            {
                foreach (var r in slice)
                {
                    if (!r.IsObject) Restyle(r);
                    frag.Runs.Add(r);
                }
            }
            else
            {
                text = text.Replace("\r\n", LineSeparator.ToString()).Replace('\r', LineSeparator).Replace('\n', LineSeparator);
                if (text.Length > 0)
                {
                    var run = new Run { Text = text, Ws = model?.Ws ?? para.DefaultWs, Props = model?.Props.Clone() ?? new PropertyBag() };
                    Restyle(run);
                    frag.Runs.Add(run);
                }
                foreach (var r in slice) if (r.IsObject) frag.Runs.Add(r);
            }
            ReplaceRange(a, b, frag.Runs.Count == 0 ? null : new List<Paragraph> { frag }, "replace");
            return Document.Paragraphs[paragraph].Length - before;
        }
    }
}
