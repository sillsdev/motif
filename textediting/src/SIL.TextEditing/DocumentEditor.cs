using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>
    /// A UI-free editing session over a <see cref="Document"/>. Every mutation is reported as an
    /// <see cref="EditDelta"/> through <see cref="Committed"/>; the editor keeps no undo stack, because the host
    /// owns it (see <see cref="SimpleUndoStack"/> for a stand-in). Paragraph identity follows the rules in
    /// <see cref="ReplaceRange"/> and <see cref="Enter"/>.
    /// </summary>
    public sealed partial class DocumentEditor
    {
        bool _wsOverride;

        public DocumentEditor(Document document)
        {
            Document = document;
            ActiveWs = WsAt(Caret);
        }

        public Document Document { get; }
        public TextPosition Caret { get; set; }
        public TextPosition Anchor { get; set; }
        /// <summary>The writing system new text gets: the run at the caret, unless the host or user chose one.</summary>
        public string ActiveWs { get; private set; }

        /// <summary>Raised after each edit with the reversible change.</summary>
        public event Action<EditDelta>? Committed;
        /// <summary>Raised when the active writing system changes: old, new (ADR 0008). That happens when the caret moves into
        /// text of another writing system, when a selection's first writing system changes (FieldWorks sets the keyboard for
        /// the first writing system a selection covers, not for the end the caret is at), when an edit or the host's
        /// <see cref="Apply"/> leaves the caret in another one, and when <see cref="SetActiveWs"/> chooses one for typing. It is
        /// raised once per change and never when the writing system stays the same. The editor has no notion of focus, so it
        /// is raised whether or not the field has focus. A host that switches keyboards subscribes to
        /// <c>RichTextField.ActiveWsChanged</c> instead, which is raised only while the field has focus.</summary>
        public event Action<string, string>? ActiveWsChanged;
        public event Action? CaretMoved;
        /// <summary>Raised on any change to the document: start paragraph, removed paragraphs, added paragraphs.</summary>
        public event Action<int, int, int>? DocumentChanged;

        /// <summary>Raised just before <see cref="DocumentChanged"/> for an edit whose range is known (typing, deleting, pasting, Enter and a
        /// line break): which text was replaced and where the new text ends, so a listener can move what it keeps by offset (a field keeps its
        /// spelling marks outside the edit). Not raised for <see cref="Apply"/>, <see cref="ExternalReplaceParagraph"/> or an edit that
        /// leaves the paragraph that follows the range as the one that keeps the identity; those only raise <see cref="DocumentChanged"/>.</summary>
        public event Action<TextEdit>? TextEdited;

        /// <summary>Called first by everything that changes the document (typing, paste, deletion, Enter, line breaks, and the
        /// host's <see cref="Apply"/> and <see cref="ExternalReplaceParagraph"/>), before it touches anything. A host that has a
        /// thread rule sets it to throw when the caller breaks the rule. <c>RichTextField</c> sets it to require the UI thread:
        /// a document edit raises events its control answers by changing Avalonia state, which only the UI thread may do, and
        /// FieldWorks makes its edits there too, so an edit from another thread throws an <see cref="InvalidOperationException"/>
        /// at once and leaves the document as it was. Null (the default) allows any thread.</summary>
        public Action? AccessCheck { get; set; }

        void CheckAccess() => AccessCheck?.Invoke();

        public bool HasSelection => !Caret.Equals(Anchor);

        public void Ordered(out TextPosition start, out TextPosition end)
        {
            if (Anchor.CompareTo(Caret) <= 0) { start = Anchor; end = Caret; }
            else { start = Caret; end = Anchor; }
        }

        public string WsAtCaret() => WsAt(Caret);

        /// <summary>The writing system of the selection: that of its first run, or at the caret when nothing is selected. Unlike <see cref="ActiveWs"/> it ignores a
        /// writing system chosen for typing (a keyboard switch). Paste maps a writing system the target lacks to this one, as FieldWorks does.</summary>
        public string SelectionWs => HasSelection ? FirstWsOfSelection() : WsAt(Caret);

        public string WsAt(TextPosition p)
        {
            var para = Document.Paragraphs[p.Paragraph];
            var before = para.RunBefore(p.Offset, out _);
            if (before != null) return before.Ws;
            var after = para.RunAfter(p.Offset, out _);
            if (after != null) return after.Ws;
            return para.DefaultWs;
        }

        /// <summary>Writing systems touched by the selection, or the single caret writing system.</summary>
        public List<string> SelectionWsSet()
        {
            var set = new List<string>();
            if (!HasSelection)
            {
                set.Add(ActiveWs);
                return set;
            }
            Ordered(out var a, out var b);
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
                    if (re > s && rs < e && !set.Contains(r.Ws)) set.Add(r.Ws);
                }
            }
            if (set.Count == 0) set.Add(ActiveWs);
            return set;
        }

        public void SetCaret(TextPosition p, bool extend = false, int direction = 0)
        {
            p.Paragraph = Math.Max(0, Math.Min(Document.Paragraphs.Count - 1, p.Paragraph));
            p.Offset = Math.Max(0, Math.Min(LimitOf(p.Paragraph), p.Offset));
            if (!extend && !IsReadOnly) p = NearestEditable(p, direction);
            if (!p.Equals(Caret) || (!extend && !p.Equals(Anchor))) CloseUndoGroup();   // a caret move ends a typing step (ADR 0023)
            Caret = p;
            if (!extend) Anchor = p;
            _wsOverride = false;
            RefreshWs();
            CaretMoved?.Invoke();
        }

        /// <summary>The writing system of the first text a range selection covers, which is what FieldWorks sets the keyboard
        /// for, whichever end the caret is at.</summary>
        string FirstWsOfSelection()
        {
            Ordered(out var a, out var b);
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                int s = pi == a.Paragraph ? a.Offset : 0;
                int e = pi == b.Paragraph ? b.Offset : Document.Paragraphs[pi].Length;
                int pos = 0;
                foreach (var r in Document.Paragraphs[pi].Runs)
                {
                    int rs = pos, re = pos + r.Text.Length;
                    pos = re;
                    if (re > s && rs < e) return r.Ws;
                }
            }
            return WsAt(Caret);
        }

        void RefreshWs()
        {
            var n = _wsOverride ? ActiveWs : HasSelection ? FirstWsOfSelection() : WsAt(Caret);
            if (n == ActiveWs) return;
            var o = ActiveWs;
            ActiveWs = n;
            ActiveWsChanged?.Invoke(o, n);
        }

        /// <summary>The host or user picks a writing system (a keyboard switch, say): typing uses it even when the
        /// preceding run differs.</summary>
        public void SetActiveWs(string ws)
        {
            _wsOverride = true;
            var o = ActiveWs;
            ActiveWs = ws;
            if (o != ws) ActiveWsChanged?.Invoke(o, ws);
        }

        PropertyBag? InheritProps(TextPosition p, string ws)
        {
            var para = Document.Paragraphs[p.Paragraph];
            var b = para.RunBefore(p.Offset, out _);
            if (b != null && !b.IsObject && b.Ws == ws && AllowsEditing(b)) return b.Props;
            var a = para.RunAfter(p.Offset, out _);
            if (a != null && !a.IsObject && a.Ws == ws && AllowsEditing(a)) return a.Props;
            return null;
        }

        EditDelta Begin(int paraStart, int paraEnd, string label)
        {
            var d = new EditDelta { Start = paraStart, CaretBefore = Caret, Label = label };
            for (int i = paraStart; i <= paraEnd; i++) d.Before.Add(Document.Paragraphs[i].Clone());
            return d;
        }

        void Finish(EditDelta d, TextPosition caret, TextEdit? edit = null)
        {
            AssignUndoGroup(d);
            Caret = Anchor = caret;
            d.CaretAfter = caret;
            _wsOverride = false;
            RefreshWs();
            Committed?.Invoke(d);
            if (edit != null) TextEdited?.Invoke(edit.Value);
            DocumentChanged?.Invoke(d.Start, d.Before.Count, d.After.Count);
            CaretMoved?.Invoke();
            EndSingleEditGroup();
        }

        // ---- editing ----

        string Normalized(string text) => Document.Normalization is NormalizationForm form ? TextNormalizer.Normalize(text, form) : text;

        public void TypeText(string text)
        {
            CheckAccess();
            if (string.IsNullOrEmpty(text)) return;
            text = Normalized(text);
            if (SingleParagraph) text = OneLine(text);
            else text = text.Replace("\t", "").Replace("\r\n", "\n");
            if (SingleLine) text = text.Replace(LineSeparator, ' ');
            if (text.Length == 0) return;
            var parts = text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) Enter();
                if (parts[i].Length > 0) InsertOne(parts[i]);
            }
        }

        /// <summary>Inserts text a user pasted (the clipboard had plain text only): the same result as <see cref="TypeText"/>, but
        /// the whole insertion, line breaks and the replaced selection included, is one undo step of its own that neither
        /// joins the typing before it nor the typing after it.</summary>
        public void PasteText(string text)
        {
            CheckAccess();
            if (string.IsNullOrEmpty(text) || !CanEditSelectionOrCaret()) return;
            // Text that is only separators adds nothing in single-paragraph mode, but a paste still replaces the selection.
            if (SingleParagraph && HasSelection && OneLine(Normalized(text)).Length == 0) { InOneUndoStep(() => DeleteSelection("type")); return; }
            InOneUndoStep(() => TypeText(text));
        }

        /// <summary>The hard line break Shift+Enter inserts: U+2028, a line break inside the paragraph (FieldWorks does the same in
        /// multi-paragraph views).</summary>
        public const char LineSeparator = (char)0x2028;

        /// <summary>Shift+Enter: inserts a <see cref="LineSeparator"/> at the caret (over the selection) without splitting the
        /// paragraph, in single-paragraph mode too (there it is our default: FieldWorks passes Shift+Enter to the parent of a view
        /// that does not accept Return). It is one undo step of its own.</summary>
        public void InsertLineBreak()
        {
            CheckAccess();
            if (SingleLine || !CanEditSelectionOrCaret()) return;
            InOneUndoStep(() => InsertOne(LineSeparator.ToString()));
        }

        /// <summary>The paragraph <see cref="InsertLineBreak"/> would leave in place of the selection (or the caret), without
        /// changing the document, so a field can lay it out and refuse a break whose result a line limit would hide. Null when
        /// the break would change nothing. <paramref name="kept"/> is the index of the paragraph that keeps its identity
        /// (<see cref="KeptParagraph"/>).</summary>
        public Paragraph? PreviewLineBreak(out int kept)
        {
            kept = -1;
            if (!CanEditSelectionOrCaret()) return null;
            Ordered(out var a, out var b);
            kept = KeptParagraph(a, b);
            var scratch = new Document { Normalization = Document.Normalization };
            for (int i = a.Paragraph; i <= b.Paragraph; i++) scratch.Paragraphs.Add(Document.Paragraphs[i].Clone());
            var trial = new DocumentEditor(scratch) { Anchor = new TextPosition(0, a.Offset), Caret = new TextPosition(b.Paragraph - a.Paragraph, b.Offset) };
            trial.SetActiveWs(ActiveWs);
            trial.InsertLineBreak();
            return scratch.Paragraphs[0];
        }

        void InsertOne(string text)
        {
            if (!CanEditSelectionOrCaret()) return;
            // Typing over a selection is one step with the deletion; it never joins an earlier step.
            if (HasSelection && !_batch) CloseUndoGroup();
            _typingWs = ActiveWs;
            _typing = true;
            try
            {
                if (HasSelection) DeleteSelection("type");
                var ws = ActiveWs;
                var d = Begin(Caret.Paragraph, Caret.Paragraph, "type");
                var para = Document.Paragraphs[Caret.Paragraph];
                long stamp = para.ChangeStamp;
                var at = Caret;
                para.Insert(Caret.Offset, text, ws, InheritProps(Caret, ws));
                d.After.Add(para.Clone());
                bool keepOverride = _wsOverride;
                var after = new TextPosition(at.Paragraph, at.Offset + text.Length);
                Finish(d, after, new TextEdit(at, at, after, stamp, stamp));
                if (keepOverride)
                {
                    _wsOverride = true;
                    ActiveWs = ws;
                }
            }
            finally { _typing = false; }
        }

        public void DeleteSelection(string label = "delete")
        {
            if (!HasSelection) return;
            Ordered(out var a, out var b);
            ReplaceRange(a, b, null, label);
        }

        public void Backspace()
        {
            if (HasSelection) { DeleteSelection("backspace"); return; }
            if (Caret.Offset == 0)
            {
                if (Caret.Paragraph > 0)
                    ReplaceRange(new TextPosition(Caret.Paragraph - 1, Document.Paragraphs[Caret.Paragraph - 1].Length), Caret, null, "join");
                return;
            }
            var t = Document.Paragraphs[Caret.Paragraph].Text;
            int to = GraphemeClusters.BackspaceTarget(t, Caret.Offset);
            ReplaceRange(new TextPosition(Caret.Paragraph, to), Caret, null, "backspace");
        }

        public void DeleteForward()
        {
            if (HasSelection) { DeleteSelection("delete"); return; }
            var p = Document.Paragraphs[Caret.Paragraph];
            if (Caret.Offset >= p.Length)
            {
                if (Caret.Paragraph + 1 < Document.Paragraphs.Count)
                    ReplaceRange(Caret, new TextPosition(Caret.Paragraph + 1, 0), null, "join");
                return;
            }
            ReplaceRange(Caret, new TextPosition(Caret.Paragraph, GraphemeClusters.DeleteTarget(p.Text, Caret.Offset)), null, "delete");
        }

        /// <summary>The index of the paragraph that keeps its identity, style and properties when [a, b) is replaced: the last one
        /// when the range spans paragraphs, starts at the very start of the first and leaves text in the last (the non-empty side
        /// wins), otherwise the first.</summary>
        public int KeptParagraph(TextPosition a, TextPosition b) =>
            a.Paragraph != b.Paragraph && a.Offset == 0 && b.Offset < Document.Paragraphs[b.Paragraph].Length ? b.Paragraph : a.Paragraph;

        /// <summary>
        /// Replaces [a, b) with a fragment (null for nothing). When the range spans paragraphs, the surviving
        /// paragraph keeps its identity, style and direction: the non-empty side wins, and the first (left)
        /// paragraph when both sides are non-empty.
        /// </summary>
        public void ReplaceRange(TextPosition a, TextPosition b, List<Paragraph>? fragment, string label)
        {
            CheckAccess();
            if (IsReadOnly || !CanEditRange(a, b)) return;
            if (fragment != null)
            {
                // Pasted text takes the editability of where it lands: a copied read-only run must not become text the user
                // cannot remove from an editable field, and text landing in a semi-editable run is semi-editable too, as typed
                // text is (the run before the insertion point when it allows editing, else the one after).
                var landing = Document.Paragraphs[a.Paragraph].RunBefore(a.Offset, out _);
                if (landing == null || !AllowsEditing(landing)) landing = Document.Paragraphs[b.Paragraph].RunAfter(b.Offset, out _);
                bool semi = landing != null && landing.Editable == RunEditable.SemiEditable;
                fragment = fragment.Select(p => p.Clone()).ToList();
                foreach (var p in fragment)
                    foreach (var r in p.Runs)
                    {
                        r.Props.IntProps.Remove(TextPropertyNumbers.Editable);
                        if (semi) r.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableSemi);
                    }
            }
            // A block the field does not allow becomes plain paragraphs first (ADR 0033), so a table's cell tabs are spaces before tabs are removed below.
            var flattening = fragment != null ? FlattenFragment(fragment) : null;
            if (SingleParagraph && fragment != null)
            {
                if (fragment.Count > 1) fragment = JoinIntoOne(fragment);
                foreach (var p in fragment) SeparatorsToSpaces(p);
                if (SingleLine) LineBreaksToSpaces(fragment);
            }
            else if (fragment != null)
            {
                foreach (var p in fragment) RemoveTabs(p);
            }
            // Every insertion passes through here or TypeText, so the host's normalisation form always applies.
            if (fragment != null && Document.Normalization is NormalizationForm form)
            {
                fragment = fragment.Select(p => p.Clone()).ToList();
                foreach (var p in fragment)
                    foreach (var r in p.Runs)
                        if (!r.IsObject) r.Text = TextNormalizer.Normalize(r.Text, form);
            }
            var d = Begin(a.Paragraph, b.Paragraph, label);
            var first = Document.Paragraphs[a.Paragraph];
            var last = Document.Paragraphs[b.Paragraph];
            var head = first.Clone();
            head.Delete(a.Offset, head.Length - a.Offset);
            var tail = last.Clone();
            tail.Delete(0, b.Offset);
            var survivor = Document.Paragraphs[KeptParagraph(a, b)];
            var merged = survivor.Clone();
            merged.Runs = new RunList();
            foreach (var r in head.Runs) merged.Runs.Add(r.Clone());
            TextPosition caret;
            var newParas = new List<Paragraph>();
            if (fragment == null || fragment.Count == 0)
            {
                int off = merged.Length;
                foreach (var r in tail.Runs) merged.Runs.Add(r.Clone());
                merged.Normalize();
                newParas.Add(merged);
                caret = new TextPosition(a.Paragraph, off);
            }
            else
            {
                foreach (var r in fragment[0].Runs) merged.Runs.Add(r.Clone());
                if (fragment.Count == 1)
                {
                    int off = merged.Length;
                    foreach (var r in tail.Runs) merged.Runs.Add(r.Clone());
                    merged.Normalize();
                    newParas.Add(merged);
                    caret = new TextPosition(a.Paragraph, off);
                }
                else
                {
                    merged.Normalize();
                    newParas.Add(merged);
                    for (int i = 1; i < fragment.Count - 1; i++) newParas.Add(fragment[i].Clone(false));
                    var lastNew = tail.Clone(false);
                    // The last fragment paragraph holds the tail's new neighbour text, so its Style and Props travel together.
                    lastNew.Style = fragment[fragment.Count - 1].Style;
                    lastNew.Props = fragment[fragment.Count - 1].Props.Clone();
                    var runs = fragment[fragment.Count - 1].Runs.Select(r => r.Clone()).ToList();
                    int off = runs.Sum(r => r.Text.Length);
                    runs.AddRange(tail.Runs.Select(r => r.Clone()));
                    lastNew.Runs = new RunList(runs);
                    lastNew.Normalize();
                    newParas.Add(lastNew);
                    caret = new TextPosition(a.Paragraph + fragment.Count - 1, off);
                }
            }
            Document.Paragraphs.RemoveRange(a.Paragraph, b.Paragraph - a.Paragraph + 1);
            Document.Paragraphs.InsertRange(a.Paragraph, newParas);
            d.After.AddRange(newParas.Select(p => p.Clone()));
            bool sameProperties = (fragment == null || fragment.Count <= 1) && first.Style == last.Style && first.Props.ContentEquals(last.Props);
            Finish(d, caret, survivor == first ? new TextEdit(a, b, caret, first.ChangeStamp, last.ChangeStamp, sameProperties) : (TextEdit?)null);
            RaiseBlocksFlattened(flattening);
        }

        /// <summary>Enter. At offset 0 the new empty paragraph (new identity) goes before and the original identity
        /// stays on the right; anywhere else the left keeps the identity and the right is new.</summary>
        public void Enter()
        {
            CheckAccess();
            if (SingleParagraph || !CanEditSelectionOrCaret()) return;
            if (HasSelection) DeleteSelection("enter-del");
            var d = Begin(Caret.Paragraph, Caret.Paragraph, "enter");
            var orig = Document.Paragraphs[Caret.Paragraph];
            int off = Caret.Offset;
            Paragraph left, right;
            if (off == 0)
            {
                left = new Paragraph { Style = orig.Style, Rtl = orig.Rtl, DefaultWs = orig.DefaultWs, Props = orig.Props.Clone() };
                right = orig.Clone();
            }
            else
            {
                left = orig.Clone();
                left.Delete(off, left.Length - off);
                right = orig.Clone(false);
                right.Delete(0, off);
            }
            FlattenNewParagraph(off == 0 ? left : right);   // the new paragraph of the split starts plain when its block kind is off (ADR 0033)
            Document.Paragraphs[Caret.Paragraph] = left;
            Document.Paragraphs.Insert(Caret.Paragraph + 1, right);
            d.After.Add(left.Clone());
            d.After.Add(right.Clone());
            Finish(d, new TextPosition(Caret.Paragraph + 1, 0), new TextEdit(Caret, Caret, new TextPosition(Caret.Paragraph + 1, 0), orig.ChangeStamp, orig.ChangeStamp));
        }

        // ---- fragments (clipboard) ----

        public List<Paragraph> GetFragment()
        {
            var result = new List<Paragraph>();
            if (!HasSelection) return result;
            Ordered(out var a, out var b);
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var p = Document.Paragraphs[pi];
                int s = pi == a.Paragraph ? a.Offset : 0;
                int e = pi == b.Paragraph ? b.Offset : p.Length;
                result.Add(new Paragraph { Style = p.Style, Rtl = p.Rtl, DefaultWs = p.DefaultWs, Props = p.Props.Clone(), Runs = new RunList(p.Slice(s, e)) });
            }
            return result;
        }

        public void Paste(List<Paragraph>? fragment)
        {
            if (fragment == null || fragment.Count == 0) return;
            Ordered(out var a, out var b);
            ReplaceRange(a, b, fragment, "paste");
        }

        // ---- host-driven ----

        /// <summary>Applies a delta, for undo and redo driven by the host.</summary>
        public void Apply(EditDelta d)
        {
            CheckAccess();
            Document.Paragraphs.RemoveRange(d.Start, d.Before.Count);
            Document.Paragraphs.InsertRange(d.Start, d.After.Select(p => p.Clone()));
            var c = d.CaretAfter;
            c.Paragraph = Math.Min(c.Paragraph, Document.Paragraphs.Count - 1);
            c.Offset = Math.Min(c.Offset, Document.Paragraphs[c.Paragraph].Length);
            CloseUndoGroup();   // a change the host applies (undo, redo, a command) ends any typing step
            Caret = Anchor = c;
            _wsOverride = false;
            RefreshWs();
            DocumentChanged?.Invoke(d.Start, d.Before.Count, d.After.Count);
            CaretMoved?.Invoke();
        }

        /// <summary>An external change to one paragraph (another view edited the same string). The caret moves with
        /// the text: changeStart, removedLength and insertedLength describe the text difference.</summary>
        public void ExternalReplaceParagraph(int paragraphIndex, Paragraph replacement, int changeStart, int removedLength, int insertedLength)
        {
            CheckAccess();
            Document.Paragraphs[paragraphIndex] = replacement;
            TextPosition Map(TextPosition p)
            {
                if (p.Paragraph != paragraphIndex) return p;
                if (p.Offset >= changeStart + removedLength) p.Offset += insertedLength - removedLength;
                else if (p.Offset > changeStart) p.Offset = changeStart + insertedLength;
                p.Offset = Math.Min(p.Offset, replacement.Length);
                return p;
            }
            CloseUndoGroup();
            Caret = Map(Caret);
            Anchor = Map(Anchor);
            RefreshWs();
            DocumentChanged?.Invoke(paragraphIndex, 1, 1);
            CaretMoved?.Invoke();
        }
    }

    /// <summary>A stand-in for the host's single undo stack (LCModel's, in FieldWorks): it receives deltas from the
    /// editor and applies them back. The editor never undoes itself. Deltas that share an
    /// <see cref="EditDelta.UndoGroup"/> are one step: undo and redo move a whole step at a time (ADR 0023).</summary>
    public sealed class SimpleUndoStack
    {
        DocumentEditor? _editor;

        /// <summary>Every delta done, oldest first (a typing step holds several).</summary>
        public List<EditDelta> Done { get; } = new List<EditDelta>();
        public List<EditDelta> Undone { get; } = new List<EditDelta>();

        /// <summary>The number of undo steps: a group of deltas counts once, an ungrouped delta counts as one.</summary>
        public int DoneSteps => Steps(Done);
        public int UndoneSteps => Steps(Undone);

        static int Steps(List<EditDelta> deltas)
        {
            int n = 0;
            for (int i = 0; i < deltas.Count; i++)
                if (i == 0 || deltas[i].UndoGroup == 0 || deltas[i].UndoGroup != deltas[i - 1].UndoGroup) n++;
            return n;
        }

        public void Attach(DocumentEditor editor)
        {
            _editor = editor;
            editor.Committed += d =>
            {
                Done.Add(d);
                Undone.Clear();
            };
        }

        public bool Undo()
        {
            if (Done.Count == 0 || _editor == null) return false;
            long group = Done[Done.Count - 1].UndoGroup;
            do
            {
                var d = Done[Done.Count - 1];
                Done.RemoveAt(Done.Count - 1);
                Undone.Add(d);
                _editor.Apply(d.Inverse());
            }
            while (Done.Count > 0 && group != 0 && Done[Done.Count - 1].UndoGroup == group);
            return true;
        }

        public bool Redo()
        {
            if (Undone.Count == 0 || _editor == null) return false;
            long group = Undone[Undone.Count - 1].UndoGroup;
            do
            {
                var d = Undone[Undone.Count - 1];
                Undone.RemoveAt(Undone.Count - 1);
                Done.Add(d);
                _editor.Apply(d);
            }
            while (Undone.Count > 0 && group != 0 && Undone[Undone.Count - 1].UndoGroup == group);
            return true;
        }
    }
}
