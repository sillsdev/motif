using System;
using System.Collections.Generic;

namespace SIL.TextEditing
{
    // Which text may be edited (ADR 0019): the field-wide read-only flag and the per-run editable property.
    public sealed partial class DocumentEditor
    {
        /// <summary>When true nothing the user does changes the text: typing, deletion, paste, cut, Enter and input
        /// method commits are ignored. Selection, caret movement and copy still work. Host-driven changes
        /// (<see cref="Apply"/>, <see cref="ExternalReplaceParagraph"/>) are not blocked.</summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                if (_isReadOnly == value) return;
                _isReadOnly = value;
                IsReadOnlyChanged?.Invoke();
            }
        }

        bool _isReadOnly;

        /// <summary>Raised when <see cref="IsReadOnly"/> changes, whoever changed it. A field restarts its spell checking on it: read-only
        /// text is not checked.</summary>
        public event Action? IsReadOnlyChanged;

        /// <summary>Text the user may change: editable and semi-editable runs. FieldWorks' views treat semi-editable text (used
        /// in type-ahead edit boxes) as editable for typing and deletion and refuse only formatting in it.</summary>
        internal static bool AllowsEditing(Run r) => r.Editable != RunEditable.NotEditable;

        /// <summary>Set by a field that hides text (a line limit): for a paragraph, the last offset the caret can rest at. The
        /// caret never rests beyond it, and the search for an editable place stops there, so text hidden behind a
        /// non-editable run is not an editable place: when the only one is hidden, the caret stays at the visible end and
        /// there is nowhere to edit. Null means no limit.</summary>
        public Func<int, int>? CaretLimit { get; set; }

        int LimitOf(int paragraph)
        {
            int len = Document.Paragraphs[paragraph].Length;
            var limit = CaretLimit;
            return limit == null ? len : Math.Max(0, Math.Min(len, limit(paragraph)));
        }

        /// <summary>Called each time an insertion point is asked whether it takes typing, so a test can count that work (and stop a
        /// scan that asks about every offset) instead of timing it.</summary>
        internal Action? EditableAtProbe { get; set; }

        /// <summary>True when an insertion point at <paramref name="p"/> can take typing: the paragraph is empty, or the
        /// run on either side is editable (FieldWorks asks for both sides of the point).</summary>
        public bool IsEditableAt(TextPosition p) => TakesTypingAt(p, true);

        bool TakesTypingAt(TextPosition p, bool hiddenTextCounts)
        {
            EditableAtProbe?.Invoke();
            var para = Document.Paragraphs[p.Paragraph];
            if (para.Length == 0) return true;
            var before = para.RunBefore(p.Offset, out _);
            var after = para.RunAfter(p.Offset, out _);
            if (before != null && AllowsEditing(before)) return true;
            // Text a line limit hides is not a place to edit, so the run after the visible end does not count: a point whose only
            // editable neighbour is hidden is not editable (ADR 0024). A paragraph mark is not such a place: a join at it changes
            // no hidden text, so it asks without this rule.
            return after != null && AllowsEditing(after) && (!hiddenTextCounts || !IsHiddenFrom(p.Paragraph, p.Offset));
        }

        /// <summary>True when the text from <paramref name="offset"/> on is hidden by the line limit. A lone break that ends the
        /// paragraph is not hidden text: it sits on the last visible line, only the empty line after it is hidden, so the caret
        /// stops before it and typing there works (ADR 0024).</summary>
        bool IsHiddenFrom(int paragraph, int offset)
        {
            int limit = LimitOf(paragraph);
            if (offset < limit) return false;
            if (offset != limit) return true;
            var runs = Document.Paragraphs[paragraph].Runs;
            var last = runs.Count > 0 ? runs[runs.Count - 1].Text : string.Empty;
            return !(last.Length > 0 && last[last.Length - 1] == LineSeparator && Document.Paragraphs[paragraph].Length == limit + 1);
        }

        /// <summary>True when every character in [a, b) is in an editable run (a collapsed range asks about the point), and
        /// the text on both sides of every paragraph mark in the range may be edited at the mark, because a paragraph mark
        /// belongs to both of its neighbours.</summary>
        public bool CanEditRange(TextPosition a, TextPosition b)
        {
            if (a.CompareTo(b) > 0) { var t = a; a = b; b = t; }
            if (a.Equals(b)) return IsEditableAt(a);
            for (int pi = a.Paragraph; pi < b.Paragraph; pi++)
                if (!TakesTypingAt(new TextPosition(pi, Document.Paragraphs[pi].Length), false) || !TakesTypingAt(new TextPosition(pi + 1, 0), false))
                    return false;
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var p = Document.Paragraphs[pi];
                int s = pi == a.Paragraph ? a.Offset : 0;
                int e = pi == b.Paragraph ? b.Offset : p.Length;
                int pos = 0;
                foreach (var r in p.Runs)
                {
                    int rs = pos, re = pos + r.Text.Length;
                    pos = re;
                    if (re > s && rs < e && !AllowsEditing(r)) return false;
                }
            }
            return true;
        }

        /// <summary>True when typing, Enter or paste would change the text now: the field is not read-only and the selection
        /// (or, with none, the caret) lies in editable text.</summary>
        public bool CanEdit => !IsReadOnly && CanEditSelectionOrCaret();

        /// <summary>True when the selection can be formatted: it is not read-only and every run in it is fully editable.
        /// FieldWorks refuses formatting in not-editable and semi-editable text alike.</summary>
        public bool CanFormatSelection
        {
            get
            {
                if (!HasSelection || !CanEdit) return false;
                Ordered(out var a, out var b);
                return AllFullyEditable(a, b);
            }
        }

        /// <summary>True when the field is not read-only and the range can be edited and every run in it is fully editable: the
        /// test for formatting and for a find replacement.</summary>
        internal bool CanFormatRange(TextPosition a, TextPosition b) => !IsReadOnly && CanEditRange(a, b) && AllFullyEditable(a, b);

        bool AllFullyEditable(TextPosition a, TextPosition b)
        {
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var p = Document.Paragraphs[pi];
                int s = pi == a.Paragraph ? a.Offset : 0;
                int e = pi == b.Paragraph ? b.Offset : p.Length;
                int pos = 0;
                foreach (var r in p.Runs)
                {
                    int rs = pos, re = pos + r.Text.Length;
                    pos = re;
                    if (re > s && rs < e && r.Editable != RunEditable.Editable) return false;
                }
            }
            return true;
        }

        bool CanEditSelectionOrCaret()
        {
            if (IsReadOnly) return false;
            if (!HasSelection) return IsEditableAt(Caret);
            Ordered(out var a, out var b);
            return CanEditRange(a, b);
        }

        /// <summary>The editable places of the document, in order: each is a stretch of insertion points [Start, End] in one
        /// paragraph that can take typing, with no read-only text between them. A paragraph with no read-only text has one
        /// place; a field-level read-only document has none. An insertion point takes typing when a run next to it allows
        /// editing, so each editable run covers the points from its start to its end and touching runs merge: the places come
        /// from the run boundaries, one pass over the runs, not from asking every offset. Text a line limit hides
        /// (<see cref="CaretLimit"/>) is not a place.</summary>
        List<(TextPosition Start, TextPosition End)> EditablePlaces()
        {
            var places = new List<(TextPosition, TextPosition)>();
            if (IsReadOnly) return places;
            for (int pi = 0; pi < Document.Paragraphs.Count; pi++)
                foreach (var (s, e) in PlacesIn(pi)) places.Add((new TextPosition(pi, s), new TextPosition(pi, e)));
            return places;
        }

        /// <summary>The editable places of one paragraph as offset ranges [Start, End], in order, from its run boundaries (see
        /// <see cref="EditablePlaces"/>); the field-wide read-only flag is not asked.</summary>
        List<(int Start, int End)> PlacesIn(int pi)
        {
            var places = new List<(int, int)>();
            var para = Document.Paragraphs[pi];
            int limit = LimitOf(pi);
            void Add(int s, int e)
            {
                if (!IsHiddenFrom(pi, s)) places.Add((s, Math.Min(e, limit)));
            }
            if (para.Length == 0) { places.Add((0, 0)); return places; }
            int start = -1, end = -1, pos = 0;
            foreach (var r in para.Runs)
            {
                int rs = pos, re = pos + r.Text.Length;
                pos = re;
                if (re == rs || !AllowsEditing(r)) continue;
                if (start >= 0 && rs <= end) { end = re; continue; }
                if (start >= 0) Add(start, end);
                start = rs;
                end = re;
            }
            if (start >= 0) Add(start, end);
            return places;
        }

        /// <summary>Finds where Tab (<see cref="EditablePlace.Next"/>), Shift+Tab (<see cref="EditablePlace.Previous"/>),
        /// Ctrl+Tab (<see cref="EditablePlace.Last"/>) or Ctrl+Shift+Tab (<see cref="EditablePlace.First"/>) goes, as FieldWorks
        /// moves among the editable places of a view: the start of the next place, the end of the previous one, the end of
        /// the last or the start of the first. False when there is no such place or the caret is already there; the host then
        /// moves focus out of the field.</summary>
        public bool TryGetEditablePlace(EditablePlace which, out TextPosition position)
        {
            position = Caret;
            var places = EditablePlaces();
            if (places.Count == 0) return false;
            TextPosition target;
            switch (which)
            {
                case EditablePlace.First: target = places[0].Start; break;
                case EditablePlace.Last: target = places[places.Count - 1].End; break;
                case EditablePlace.Next:
                {
                    int i = places.FindIndex(pl => pl.Start.CompareTo(Caret) > 0);
                    if (i < 0) return false;
                    target = places[i].Start;
                    break;
                }
                default:
                {
                    int i = places.FindLastIndex(pl => pl.End.CompareTo(Caret) < 0);
                    if (i < 0) return false;
                    target = places[i].End;
                    break;
                }
            }
            if (target.Equals(Caret) && !HasSelection) return false;
            position = target;
            return true;
        }

        /// <summary>The closest insertion point to <paramref name="p"/>, in its paragraph, where editing is allowed: p itself
        /// when it already is. A direction (+1 forward, -1 backward) searches that way first, so an arrow key steps over
        /// read-only text instead of bouncing back; with none, the nearer side wins and backward wins a tie. When the
        /// paragraph has no such place, p is kept.</summary>
        /// <remarks>The search walks outward from the point one run at a time, on both sides at once when no direction is given,
        /// and stops as soon as the other side cannot be nearer, so a point beside an editable run costs a few runs however many
        /// the paragraph has; each run looked at counts as one probe.</remarks>
        public TextPosition NearestEditable(TextPosition p, int direction = 0)
        {
            if (IsEditableAt(p)) return p;
            int pi = p.Paragraph;
            var runs = Document.Paragraphs[pi].Runs;
            int limit = LimitOf(pi);

            // The run that holds the character at p, from the paragraph's run starts (no probes).
            int idx = Document.Paragraphs[pi].RunIndexAt(p.Offset, out int idxStart);

            // Forward: the start of the first editable run at or after idx (p is not editable, so none touches p from the left).
            int f = idx, fpos = idxStart;
            bool fdone = false;
            // Backward: the end of the nearest editable chain before p, and only when p is within the visible text.
            int b = idx - 1, bend = idxStart;
            bool bdone = p.Offset - 1 > limit;
            int fwd = -1, back = -1;

            void StepForward()
            {
                if (f >= runs.Count || fpos > limit) { fdone = true; return; }
                EditableAtProbe?.Invoke();
                var r = runs[f];
                int len = r.Text.Length;
                if (len > 0 && AllowsEditing(r))
                {
                    if (!IsHiddenFrom(pi, fpos)) fwd = fpos;
                    fdone = true;
                    return;
                }
                fpos += len;
                f++;
            }

            void StepBackward()
            {
                if (b < 0) { bdone = true; return; }
                EditableAtProbe?.Invoke();
                var r = runs[b];
                int len = r.Text.Length;
                if (len == 0 || !AllowsEditing(r))
                {
                    bend -= len;
                    b--;
                    return;
                }
                // An editable run: it ends a chain of touching editable runs. The chain is hidden only when it starts at or past the
                // visible end, which needs its start only when it ends there, so the walk back through the chain is rare.
                int chainEnd = bend, chainStart = bend - len;
                int j = b - 1;
                while (chainEnd >= limit && j >= 0)
                {
                    EditableAtProbe?.Invoke();
                    int jl = runs[j].Text.Length;
                    if (jl == 0) { j--; continue; }
                    if (!AllowsEditing(runs[j])) break;
                    chainStart -= jl;
                    j--;
                }
                if (chainEnd < limit || !IsHiddenFrom(pi, chainStart))
                {
                    back = Math.Min(chainEnd, limit);
                    bdone = true;
                    return;
                }
                // A hidden chain is no place: keep looking further back, past it.
                bend = chainStart;
                b = j;
            }

            while (true)
            {
                if (fwd >= 0) fdone = true;
                if (back >= 0) bdone = true;
                if (direction > 0 && fwd >= 0) break;
                if (direction < 0 && back >= 0) break;
                if (fdone && bdone) break;
                if (direction == 0)
                {
                    // The other side cannot be nearer once the run it is looking at is farther than what was found (a tie goes backward).
                    if (fwd >= 0 && p.Offset - bend > fwd - p.Offset) break;
                    if (back >= 0 && fpos - p.Offset >= p.Offset - back) break;
                }
                bool stepForward = direction > 0 ? !fdone : direction < 0 ? bdone : !fdone && (bdone || fpos - p.Offset <= p.Offset - bend);
                if (stepForward) StepForward(); else StepBackward();
            }

            int pick;
            if (fwd < 0) pick = back;
            else if (back < 0) pick = fwd;
            else if (direction > 0) pick = fwd;
            else if (direction < 0) pick = back;
            else pick = p.Offset - back <= fwd - p.Offset ? back : fwd;
            return pick < 0 ? p : new TextPosition(pi, pick);
        }
    }

    /// <summary>Where Tab and its variants go among the editable places of a field.</summary>
    public enum EditablePlace { Next, Previous, First, Last }
}
