using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace SIL.TextEditing.Avalonia
{
    // The host's language service (ADR 0030, owner decision Q58): token chips, diagnostic squiggles, hover, completion and fixes for a box whose text is
    // a notation. It follows the spell checker's rules: the field asks while it is idle, the answer is applied only to the paragraph it was asked about at
    // the stamp it had (a later one is dropped and the text asked again), the diagnostics are a view and never written into the document, and the host may
    // answer from another thread or process. A field of several paragraphs gives the service its paragraphs joined by line feeds (the line is the
    // paragraph's index); completion and insert commands serve a field of one paragraph only.
    public partial class RichTextField
    {
        ITextLanguageService? _lang;
        ITextLanguageService? _langListening;
        TokenStyleMap? _tokenStyles;
        LangState? _langState;
        LangRequest? _langOut;
        int _langEpoch;
        bool _langPosted;
        bool _langFailed;

        /// <summary>The paragraphs as they were when a text was asked about: each one, its stamp and where it starts in the text the service was given (the
        /// paragraphs joined by line feeds, so the line is the paragraph's index; one paragraph is its own text).</summary>
        sealed class LangSnapshot
        {
            public Paragraph[] Paragraphs = null!;
            public long[] Stamps = null!;
            public int[] Starts = null!;
            public string Text = string.Empty;

            public static LangSnapshot Of(IReadOnlyList<Paragraph> paragraphs)
            {
                var s = new LangSnapshot { Paragraphs = new Paragraph[paragraphs.Count], Stamps = new long[paragraphs.Count], Starts = new int[paragraphs.Count] };
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < paragraphs.Count; i++)
                {
                    if (i > 0) text.Append('\n');
                    s.Paragraphs[i] = paragraphs[i];
                    s.Stamps[i] = paragraphs[i].ChangeStamp;
                    s.Starts[i] = text.Length;
                    text.Append(paragraphs[i].Text);
                }
                s.Text = text.ToString();
                return s;
            }

            public bool Matches(IReadOnlyList<Paragraph> paragraphs)
            {
                if (paragraphs.Count != Paragraphs.Length) return false;
                for (int i = 0; i < Paragraphs.Length; i++)
                    if (!ReferenceEquals(paragraphs[i], Paragraphs[i]) || paragraphs[i].ChangeStamp != Stamps[i]) return false;
                return true;
            }

            public bool SameAs(LangSnapshot other) => other.Paragraphs.Length == Paragraphs.Length && other.Matches(Paragraphs) && Stamps.SequenceEqual(other.Stamps);

            /// <summary>Where paragraph <paramref name="index"/> starts in <see cref="Text"/>; the paragraph count gives one past the end, as if a paragraph followed.</summary>
            public int StartOf(int index) => index < Starts.Length ? Starts[index] : Text.Length + 1;

            public int EndOf(int index) => StartOf(index + 1) - 1;

            public int Global(TextPosition p) => Starts[p.Paragraph] + p.Offset;

            public TextPosition Local(int offset)
            {
                int i = Array.BinarySearch(Starts, offset);
                if (i < 0) i = ~i - 1;
                i = Math.Max(0, i);
                return new TextPosition(i, Math.Min(offset - Starts[i], EndOf(i) - Starts[i]));
            }
        }

        /// <summary>What the service found, in offsets of the text it was given, for the paragraphs as they were in <see cref="Snapshot"/>. After an edit, results that lie
        /// clear of it are carried to where they now are (<see cref="Provisional"/>) so the chips do not flicker while the text is asked again; a diagnostic
        /// carried this way has lost its fix, which was made for text that has changed.</summary>
        sealed class LangState
        {
            public LangSnapshot Snapshot = null!;
            public bool Provisional;
            public readonly List<LangToken> Tokens = new List<LangToken>();
            public readonly List<LangDiag> Diagnostics = new List<LangDiag>();
        }

        readonly struct LangToken
        {
            public LangToken(int start, int end, string kind, string? target)
            {
                Start = start;
                End = end;
                Kind = kind;
                Target = target;
            }

            public int Start { get; }
            public int End { get; }
            public string Kind { get; }
            public string? Target { get; }
        }

        sealed class LangDiag
        {
            public int Start, End;
            public LanguageDiagnostic Diagnostic = null!;
        }

        sealed class LangRequest
        {
            public LangSnapshot Snapshot = null!;
            public int Epoch;
            public string Text => Snapshot.Text;
            public CancellationTokenSource Cancel = null!;
        }

        // ------------------------------------------------ the host's side

        /// <summary>The host's language service for this field's text (ADR 0030). Null, the default, draws and asks nothing. Setting it starts over
        /// (<see cref="RestartLanguageAnalysis"/>); the field listens to <see cref="ITextLanguageService.Changed"/> while it is in the visual tree. The field
        /// then behaves as a single-line text box is expected to: set <see cref="SingleLine"/> as well for a notation box.</summary>
        public ITextLanguageService? LanguageService
        {
            get => _lang;
            set
            {
                if (ReferenceEquals(_lang, value)) return;
                StopLanguageListening();
                CloseCompletion();
                CancelLanguageRequest();
                _lang = value;
                _langState = null;
                _langFailed = false;
                _langCancelledRuns = 0;
                _langEpoch++;
                if (_onScreen) StartLanguageListening();
                RefreshLanguageLayout();
                ScheduleLanguageIfWanted();
            }
        }

        /// <summary>Single-line mode for a notation box (forwards to <see cref="DocumentEditor.SingleLine"/>): Enter goes to the host
        /// (<see cref="EnterRequested"/>) and there is no hard line break either (Shift+Enter does nothing, a line separator in typed or pasted text becomes a space).</summary>
        public bool SingleLine
        {
            get => Editor.SingleLine;
            set => Editor.SingleLine = value;
        }

        /// <summary>How the field draws each kind of token (a chip behind the glyphs), or null for none. The map has a style for the light theme and for the dark
        /// one; a high contrast theme gets no chips, so the text stays in the system's colours. Chips are decoration: copy, undo and the document never see them.</summary>
        public TokenStyleMap? TokenStyles
        {
            get => _tokenStyles;
            set
            {
                _tokenStyles = value;
                InvalidateVisual();
            }
        }

        /// <summary>Raised on the field's thread when what the field shows of the service's answer changed: new tokens or diagnostics arrived, or the old ones
        /// went. A host that lists the findings (a panel, an agent) reads <see cref="LanguageDiagnostics"/> then.</summary>
        public event EventHandler? LanguageAnalysisChanged;

        /// <summary>Raised on the field's thread when the service threw or its task failed. The field then keeps what it had and stops asking until
        /// <see cref="RestartLanguageAnalysis"/> or the service's <see cref="ITextLanguageService.Changed"/>. With no handler the failure is written to
        /// <see cref="System.Diagnostics.Trace"/>.</summary>
        public event Action<Exception>? LanguageServiceFailed;

        /// <summary>For a test: how idle work, and the arrival of an answer a service gave late, is posted. It is given the action to run; null (the default) posts it to
        /// the field's dispatcher at background priority.</summary>
        internal Action<Action>? LanguageIdlePost { get; set; }

        /// <summary>For a test: how many analyses have been asked of the service.</summary>
        internal int LanguageRequests { get; private set; }

        /// <summary>Forgets what is known of the text and asks the service again as idle time allows. Call it when the service's answers changed without
        /// <see cref="ITextLanguageService.Changed"/> being raised; a failed service is tried again. What is shown stays until the new answer arrives.</summary>
        public void RestartLanguageAnalysis()
        {
            _langEpoch++;
            _langFailed = false;
            _langCancelledRuns = 0;
            CancelLanguageRequest();
            if (_langState != null) _langState.Provisional = true;
            ScheduleLanguageIfWanted();
        }

        /// <summary>True when the service's answer is for the text as it is now (nothing is out, nothing carried from before an edit). Also true with no service.</summary>
        public bool IsLanguageAnalysisCurrent => !LanguageActive || (LiveLanguage() is LangState s && !s.Provisional && _langOut == null);

        bool LanguageActive => _lang != null && Editor.Document.Paragraphs.Count > 0;

        /// <summary>Completion and insert commands work in a field of one paragraph only (ADR 0030); a field of several has tokens, diagnostics, hover and fixes.</summary>
        bool LanguageSingle => _lang != null && Editor.Document.Paragraphs.Count == 1;

        /// <summary>The tokens as last answered (carried to where edits have put them), in order; empty when there is no answer for this text yet. Positions are
        /// those of the text the service was given: the paragraph's text, or for a field of several the paragraphs joined by line feeds, the line being the
        /// paragraph's index.</summary>
        public IReadOnlyList<LanguageToken> LanguageTokens
        {
            get
            {
                var s = LiveLanguage();
                return s == null ? new LanguageToken[0] : s.Tokens.Select(t => new LanguageToken(OffsetRange(s.Snapshot.Text, t.Start, t.End), t.Kind, t.Target)).ToList();
            }
        }

        /// <summary>The diagnostics as last answered, in order. After an edit those clear of it are carried to where they now are, without their fix
        /// (<see cref="ApplyDiagnosticFix"/> needs an answer for the text as it is). Diagnostics are a view: none of them is in the document.</summary>
        public IReadOnlyList<LanguageDiagnostic> LanguageDiagnostics => LiveLanguage()?.Diagnostics.Select(d => d.Diagnostic).ToList() ?? (IReadOnlyList<LanguageDiagnostic>)new LanguageDiagnostic[0];

        static LanguageRange OffsetRange(string text, int start, int end) => new LanguageRange(LanguageText.PositionOf(text, start), LanguageText.PositionOf(text, end));

        /// <summary>The diagnostics that cover the character at a position, or lie at the caret when it is at the end of one, worst first.</summary>
        public IReadOnlyList<LanguageDiagnostic> DiagnosticsAt(TextPosition position)
        {
            var s = LiveLanguage();
            if (s == null || position.Paragraph < 0 || position.Paragraph >= s.Snapshot.Paragraphs.Length) return new LanguageDiagnostic[0];
            int at = s.Snapshot.Global(position);
            return s.Diagnostics.Where(d => (at >= d.Start && at < d.End) || (d.Start == d.End && at == d.Start)).OrderBy(d => d.Diagnostic.Severity).Select(d => d.Diagnostic).ToList();
        }

        /// <summary>Applies the fix of a diagnostic this field showed, as one undo step. False, and nothing changes, when the field is read-only or the range may not be
        /// edited, the diagnostic has no fix, its edits do not all lie in one paragraph, or the answer it came from is not for the text as it is (an edit since, or
        /// one still being asked). The diagnostic is one of <see cref="LanguageDiagnostics"/>, or from <c>ContextInfo.Diagnostics</c>.</summary>
        public bool ApplyDiagnosticFix(LanguageDiagnostic diagnostic)
        {
            if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
            var s = LiveLanguage();
            var fix = diagnostic.Fix;
            if (s == null || s.Provisional || fix == null || fix.Edits.Count == 0 || !s.Diagnostics.Any(d => ReferenceEquals(d.Diagnostic, diagnostic) || d.Diagnostic.Equals(diagnostic))) return false;
            var snap = s.Snapshot;
            string text = snap.Text;
            var global = fix.Edits.Select(e => (Start: LanguageText.StartOffset(text, e.Range), End: LanguageText.EndOffset(text, e.Range), e.NewText)).OrderBy(e => e.Start).ThenBy(e => e.End).ToList();
            for (int i = 0; i < global.Count; i++)
                if (global[i].End < global[i].Start || (i > 0 && global[i].Start < global[i - 1].End)) return false;
            int paragraph = snap.Local(global[0].Start).Paragraph;
            int from = snap.StartOf(paragraph), to = snap.EndOf(paragraph);
            if (global.Any(e => e.Start < from || e.End > to)) return false;
            var edits = global.Select(e => (Start: e.Start - from, End: e.End - from, e.NewText)).ToList();
            return ReplaceLanguage(paragraph, edits.First().Start, edits.Last().End, ApplyEdits(snap.Paragraphs[paragraph].Text, edits), "language fix");
        }

        static string ApplyEdits(string text, List<(int Start, int End, string NewText)> edits)
        {
            int from = edits[0].Start, to = edits[edits.Count - 1].End;
            var sb = new System.Text.StringBuilder();
            int at = from;
            foreach (var e in edits)
            {
                sb.Append(text, at, e.Start - at).Append(e.NewText);
                at = e.End;
            }
            sb.Append(text, at, to - at);
            return sb.ToString();
        }

        /// <summary>Replaces [start, end) of a paragraph with text in the writing system and properties of the text there, as one undo step; false when the
        /// field is read-only or the range may not be edited.</summary>
        bool ReplaceLanguage(int index, int start, int end, string replacement, string label)
        {
            var a = new TextPosition(index, start);
            var b = new TextPosition(index, end);
            if (Editor.IsReadOnly || !Editor.CanEditRange(a, b)) return false;
            var paragraph = Editor.Document.Paragraphs[index];
            var run = paragraph.RunAfter(start, out _) ?? paragraph.RunBefore(start, out _);
            var fragment = new Paragraph { DefaultWs = run?.Ws ?? paragraph.DefaultWs };
            if (replacement.Length > 0) fragment.Runs.Add(new Run { Ws = run?.Ws ?? paragraph.DefaultWs, Text = replacement, Props = run != null && !run.IsObject ? run.Props.Clone() : new PropertyBag() });
            Editor.CloseUndoGroup();
            Editor.ReplaceRange(a, b, new List<Paragraph> { fragment }, label);
            Editor.CloseUndoGroup();
            return true;
        }

        /// <summary>Whether the host's insert command <paramref name="kind"/> is allowed at the selection, as the service says (FieldWorks' Insert menu items are enabled by it);
        /// false with no service, in a read-only field, or when the field is not of one paragraph.</summary>
        public bool CanInsert(string kind)
        {
            if (!LanguageSingle || Editor.IsReadOnly || !Editor.CanEdit) return false;
            Editor.Ordered(out var a, out var b);
            string text = Editor.Document.Paragraphs[0].Text;
            return _lang!.CanInsert(text, OffsetRange(text, a.Offset, b.Offset), kind);
        }

        // ------------------------------------------------ results

        LangState? LiveLanguage()
        {
            var s = _langState;
            if (s == null || !LanguageActive) return null;
            // Not dropped here: an edit moves the stamp before the field is told of it, and what the edit leaves alone is carried over then (LanguageAfterChange).
            return s.Snapshot.Matches(Editor.Document.Paragraphs) ? s : null;
        }

        static (int Start, int End) Offsets(string text, LanguageRange range)
        {
            int a = Math.Max(0, Math.Min(LanguageText.StartOffset(text, range), text.Length));
            int b = Math.Max(a, Math.Min(LanguageText.EndOffset(text, range), text.Length));
            return (a, b);
        }

        void ApplyAnalysis(LangRequest request, LanguageAnalysis analysis)
        {
            var state = new LangState { Snapshot = request.Snapshot };
            string text = request.Text;
            foreach (var t in analysis.Tokens)
            {
                var (a, b) = Offsets(text, t.Range);
                if (b > a) state.Tokens.Add(new LangToken(a, b, t.Kind, t.Target));
            }
            foreach (var d in analysis.Diagnostics)
            {
                var (a, b) = Offsets(text, d.Range);
                state.Diagnostics.Add(new LangDiag { Start = a, End = b, Diagnostic = d });
            }
            var old = _langState;
            bool same = old != null && old.Tokens.SequenceEqual(state.Tokens) && old.Diagnostics.Count == state.Diagnostics.Count
                && old.Diagnostics.Zip(state.Diagnostics, (x, y) => x.Start == y.Start && x.End == y.End && x.Diagnostic.Equals(y.Diagnostic)).All(v => v);
            // The fixes carried over an edit were dropped; a new answer for the same marks brings them back, so it is not "the same" while the old was carried.
            if (same && !old!.Provisional && old.Snapshot.SameAs(state.Snapshot)) return;
            _langState = state;
            RefreshLanguageLayout();
            LanguageAnalysisChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Puts the diagnostics on the paragraphs' layouts, and redraws. Tokens are drawn by the field itself, from the state.</summary>
        void RefreshLanguageLayout()
        {
            for (int i = 0; i < _layouts.Count; i++) ApplyDiagnostics(i, _layouts[i]);
            InvalidateVisual();
        }

        void ApplyDiagnostics(int index, ParagraphLayout? layout)
        {
            if (layout == null) return;
            var s = LiveLanguage();
            if (s == null || s.Diagnostics.Count == 0 || index >= s.Snapshot.Paragraphs.Length) { layout.SetDiagnosticMarks(null); return; }
            int from = s.Snapshot.StartOf(index), to = s.Snapshot.EndOf(index);
            var marks = new List<(int, int, IBrush)>();
            foreach (var d in s.Diagnostics)
            {
                var brush = SeverityBrush(d.Diagnostic.Severity);
                int a = Math.Max(d.Start, from), b = Math.Min(d.End, to);
                if (brush != null && b > a) marks.Add((a - from, b - from, brush));
            }
            layout.SetDiagnosticMarks(marks.Count == 0 ? null : marks);
        }

        static IBrush? SeverityBrush(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error: return Brushes.Red;
                case DiagnosticSeverity.Warning: return Brushes.Blue;
                case DiagnosticSeverity.Information: return Brushes.Gray;
                default: return null;
            }
        }

        // ------------------------------------------------ an edit carries the results it leaves alone

        void LanguageAfterChange(int start, int removed, int added)
        {
            if (_lang == null) return;
            CancelLanguageRequest();
            _langFailed = false;          // a failure holds off asking about that text only
            _langCancelledRuns = 0;
            ClearLanguageHover();         // the tip was about text that is not there now
            _langEdited = true;
            if (LanguageDebounce > TimeSpan.Zero) { _langDelay?.Dispose(); _langPosted = false; }   // every edit starts the wait again
            var old = _langState;
            var edit = _pendingEdit;
            _langState = null;
            if (old != null && Editor.Document.Paragraphs.Count > 0) _langState = Carry(old, edit, start, removed, added);
            RefreshLanguageLayout();
            ScheduleLanguageIfWanted();
            if (_completion != null) ScheduleCompletionRefresh();
        }

        /// <summary>The results of <paramref name="old"/> that the change of paragraphs [start, start + removed) into <paramref name="added"/> paragraphs left alone,
        /// moved to where they now are; null when the old results were not for the text as it was just before the change. Within the changed paragraphs only the
        /// editor's own edit (<see cref="TextEdit"/>) says what is untouched; a change without one (the host's undo) keeps only the paragraphs around it.</summary>
        LangState? Carry(LangState old, TextEdit? edit, int start, int removed, int added)
        {
            var paragraphs = Editor.Document.Paragraphs;
            var was = old.Snapshot;
            if (was.Paragraphs.Length - removed + added != paragraphs.Count || start + removed > was.Paragraphs.Length) return null;
            for (int i = 0; i < start; i++)
                if (!ReferenceEquals(was.Paragraphs[i], paragraphs[i]) || was.Stamps[i] != paragraphs[i].ChangeStamp) return null;
            for (int i = start + removed; i < was.Paragraphs.Length; i++)
            {
                var p = paragraphs[i - removed + added];
                if (!ReferenceEquals(was.Paragraphs[i], p) || was.Stamps[i] != p.ChangeStamp) return null;
            }
            var now = LangSnapshot.Of(paragraphs);
            // The stretch the change replaced, in the old text, and where its end now is.
            int a = was.StartOf(start), b = was.StartOf(start + removed) - 1, newB = now.StartOf(start + added) - 1;
            if (edit is TextEdit e && e.Start.Paragraph == start && e.OldEnd.Paragraph == start + removed - 1 && e.NewEnd.Paragraph == start + added - 1
                && was.Stamps[e.Start.Paragraph] == e.FirstOldStamp && was.Stamps[e.OldEnd.Paragraph] == e.LastOldStamp)
            {
                a = was.Global(e.Start);
                b = was.Global(e.OldEnd);
                newB = now.Global(e.NewEnd);
            }
            int shift = newB - b;
            var carried = new LangState { Snapshot = now, Provisional = true };
            foreach (var t in old.Tokens)
            {
                if (t.End <= a) carried.Tokens.Add(t);
                else if (t.Start >= b) carried.Tokens.Add(new LangToken(t.Start + shift, t.End + shift, t.Kind, t.Target));
            }
            foreach (var d in old.Diagnostics)
            {
                if (d.End <= a && d.End > d.Start) carried.Diagnostics.Add(new LangDiag { Start = d.Start, End = d.End, Diagnostic = WithoutFix(d.Diagnostic, d.Diagnostic.Range) });
                else if (d.Start >= b) carried.Diagnostics.Add(new LangDiag { Start = d.Start + shift, End = d.End + shift, Diagnostic = WithoutFix(d.Diagnostic, OffsetRange(now.Text, d.Start + shift, d.End + shift)) });
            }
            return carried;
        }

        static LanguageDiagnostic WithoutFix(LanguageDiagnostic d, LanguageRange range) => new LanguageDiagnostic(range, d.Severity, d.Message, d.Code, d.Source, null);

        // ------------------------------------------------ asking, when idle

        void ScheduleLanguageIfWanted()
        {
            if (!LanguageActive || _langPosted || !_onScreen || _langFailed || _langCancelledRuns > MaxCancelledRetries) return;   // a service that gave up is not asked again by a repaint or a stray notice
            var live = LiveLanguage();
            if (live != null && !live.Provisional) return;
            _langPosted = true;
            void Ask()
            {
                _langPosted = false;
                if (LanguageActive && _onScreen) AskLanguageService();
            }
            if (LanguageDebounce <= TimeSpan.Zero || live == null && _langState == null && _langOut == null && !_langEdited) Post(Ask);
            else StartLanguageDelay(() => Post(Ask));
        }

        bool _langEdited;
        IDisposable? _langDelay;

        /// <summary>How long after the last edit the field waits before it asks the service, so typing is not asked about key by key: a service in another process wants
        /// 100 to 250 ms. Zero, the default, asks as soon as the field is idle. The first answer for a new text or a new service is never held back.</summary>
        public TimeSpan LanguageDebounce { get; set; }

        /// <summary>For a test: starts a one-shot delay; given the interval and the action it returns what cancels it. Null (the default) uses a task delay that posts to the field's dispatcher.</summary>
        internal Func<TimeSpan, Action, IDisposable>? LanguageScheduler { get; set; }

        sealed class DelayHandle : IDisposable
        {
            readonly CancellationTokenSource _cancel = new CancellationTokenSource();

            public DelayHandle(TimeSpan interval, Action action)
            {
                Task.Delay(interval, _cancel.Token).ContinueWith(t => { if (!t.IsCanceled) action(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            public void Dispose() => _cancel.Cancel();
        }

        void StartLanguageDelay(Action action)
        {
            _langDelay?.Dispose();
            var scheduler = LanguageScheduler;
            _langDelay = scheduler != null ? scheduler(LanguageDebounce, action) : new DelayHandle(LanguageDebounce, action);
        }

        void Post(Action action)
        {
            var post = LanguageIdlePost;
            if (post != null) post(action);
            else OwnDispatcher.Post(action, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        void CancelLanguageRequest()
        {
            var out_ = _langOut;
            _langOut = null;
            if (out_ != null)
            {
                try { out_.Cancel.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        void AskLanguageService()
        {
            var service = _lang;
            if (service == null || _langFailed) return;
            var live = LiveLanguage();
            if (live != null && !live.Provisional) return;
            var asking = _langOut;
            if (asking != null && asking.Epoch == _langEpoch && asking.Snapshot.Matches(Editor.Document.Paragraphs)) return;
            CancelLanguageRequest();
            _langEdited = false;
            var request = new LangRequest { Snapshot = LangSnapshot.Of(Editor.Document.Paragraphs), Epoch = _langEpoch, Cancel = new CancellationTokenSource() };
            Task<LanguageAnalysis> task;
            try
            {
                LanguageRequests++;
                task = service.AnalyzeAsync(request.Text, request.Cancel.Token);
            }
            catch (Exception e)
            {
                LanguageFailure(e);
                return;
            }
            _langOut = request;
            if (task.IsCompleted) FinishLanguageRequest(request, task);
            else task.ContinueWith(t => Post(() => FinishLanguageRequest(request, t)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /// <summary>On the field's thread: the service has answered. The answer is applied only when the field has the same service, the same paragraphs at the same stamps and has not started over.</summary>
        void FinishLanguageRequest(LangRequest request, Task<LanguageAnalysis> task)
        {
            if (ReferenceEquals(_langOut, request)) _langOut = null;
            bool wanted = request.Epoch == _langEpoch && !request.Cancel.IsCancellationRequested;
            if (task.IsFaulted)
            {
                if (wanted) LanguageFailure(task.Exception!.GetBaseException());
                return;
            }
            if (task.IsCanceled)
            {
                // A service that cancels a request nobody cancelled is asked again, a few times for the same text; then the field waits for the next edit.
                if (wanted && ++_langCancelledRuns <= MaxCancelledRetries) ScheduleLanguageIfWanted();
                return;
            }
            bool current = wanted && LanguageActive && request.Snapshot.Matches(Editor.Document.Paragraphs);
            if (current) { _langCancelledRuns = 0; ApplyAnalysis(request, task.Result); }
            else ScheduleLanguageIfWanted();
        }

        /// <summary>A service that is asked again for the same text after cancelling it itself, at most this many times.</summary>
        const int MaxCancelledRetries = 3;

        int _langCancelledRuns;

        /// <summary>An analysis failed: the text is not asked again until it changes (the next edit tries the service again), or until <see cref="RestartLanguageAnalysis"/> or
        /// the service's <c>Changed</c>.</summary>
        void LanguageFailure(Exception error)
        {
            _langFailed = true;
            ReportLanguageFailure(error);
        }

        /// <summary>Tells the host of a failure without stopping anything: a completion or a hover that failed costs that answer only.</summary>
        void ReportLanguageFailure(Exception error)
        {
            var handler = LanguageServiceFailed;
            if (handler == null)
            {
                Trace("The language service failed: " + error);
                return;
            }
            try { handler(error); }
            catch (Exception thrown) { Trace("The LanguageServiceFailed handler threw: " + thrown); }
        }

        // ------------------------------------------------ the host's service changed, the field came and went

        void StartLanguageListening()
        {
            if (_lang == null || _langListening == _lang) return;
            StopLanguageListening();
            _langListening = _lang;
            _lang.Changed += OnLanguageServiceChanged;
        }

        void StopLanguageListening()
        {
            if (_langListening != null) _langListening.Changed -= OnLanguageServiceChanged;
            _langListening = null;
        }

        void OnLanguageServiceChanged(object? sender, EventArgs e)
        {
            if (CheckAccess()) RestartLanguageAnalysis();
            else OwnDispatcher.Post(RestartLanguageAnalysis, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        void LanguageAttached()
        {
            _langCancelledRuns = 0;       // the field is back: a service that gave up while it was away is asked again
            StartLanguageListening();
            ScheduleLanguageIfWanted();
        }

        void LanguageDetached()
        {
            CancelLanguageRequest();
            CloseCompletion();
            StopLanguageListening();
        }

        // ------------------------------------------------ chips

        void DrawLanguageChips(DrawingContext context, ParagraphLayout layout, int index, double top)
        {
            if (_tokenStyles == null || SystemColours.IsHighContrast()) return;
            var s = LiveLanguage();
            if (s == null || s.Tokens.Count == 0 || index >= s.Snapshot.Paragraphs.Length) return;
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            int from = s.Snapshot.StartOf(index), to = s.Snapshot.EndOf(index);
            foreach (var token in s.Tokens)
            {
                int a = Math.Max(token.Start, from), b = Math.Min(token.End, to);
                if (b <= a) continue;
                var style = _tokenStyles.Resolve(token.Kind, dark);
                if (style == null || (style.Fill == null && style.Outline == null)) continue;
                var pen = style.Outline == null ? null : new Pen(style.Outline, 1);
                foreach (var r in layout.SelectionRects(a - from, b - from))
                {
                    var box = new Rect(r.X + 0.5, r.Y + top + 0.5, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1));
                    context.DrawRectangle(style.Fill, pen, new RoundedRect(box, 3));
                }
            }
        }

        /// <summary>Lays a wash of the background over the glyphs of tokens whose style mutes them; drawn after the text, before the squiggles.</summary>
        void DrawLanguageMutes(DrawingContext context, ParagraphLayout layout, int index, double top)
        {
            if (_tokenStyles == null || SystemColours.IsHighContrast() || !(Background is ISolidColorBrush background)) return;
            var s = LiveLanguage();
            if (s == null || s.Tokens.Count == 0 || index >= s.Snapshot.Paragraphs.Length) return;
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            int from = s.Snapshot.StartOf(index), to = s.Snapshot.EndOf(index);
            foreach (var token in s.Tokens)
            {
                int a = Math.Max(token.Start, from), b = Math.Min(token.End, to);
                var style = b > a ? _tokenStyles.Resolve(token.Kind, dark) : null;
                if (style == null || style.Mute <= 0) continue;
                var wash = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(background.Color, style.Mute * background.Opacity);
                foreach (var r in layout.SelectionRects(a - from, b - from))
                    context.DrawRectangle(wash, null, new Rect(r.X, r.Y + top, r.Width, r.Height));
            }
        }

        /// <summary>For a test: the chips that would be drawn, as start, end (offsets of the text the service was given) and style, for the theme the field is in;
        /// empty with none, or in a high contrast theme.</summary>
        internal IReadOnlyList<(int Start, int End, TokenStyle Style)> ChipsDrawn()
        {
            var result = new List<(int, int, TokenStyle)>();
            var s = LiveLanguage();
            if (s == null || _tokenStyles == null || SystemColours.IsHighContrast()) return result;
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            foreach (var token in s.Tokens)
            {
                var style = _tokenStyles.Resolve(token.Kind, dark);
                if (style != null && style.Draws) result.Add((token.Start, token.End, style));
            }
            return result;
        }

        // ------------------------------------------------ context menu

        ContextInfo WithLanguage(ContextInfo info)
        {
            foreach (var d in DiagnosticsAt(info.Position)) info.Diagnostics.Add(d);
            return info;
        }

        // ------------------------------------------------ hover

        TextPosition _hoverAt = NoHover;
        static readonly TextPosition NoHover = new TextPosition(-1, -1);
        int _hoverRequest;

        /// <summary>The help for a place: the messages of the diagnostics there (worst first), then the service's own hover text, one per line; null when there is none, or when the
        /// text changed while the service was asked. A host that shows its own tooltip asks this; the field itself sets its tooltip from it when the pointer rests on text.</summary>
        public Task<string?> GetLanguageHoverAsync(TextPosition position) => GetLanguageHoverAsync(position, CancellationToken.None);

        async Task<string?> GetLanguageHoverAsync(TextPosition position, CancellationToken cancellation)
        {
            var service = _lang;
            var paragraphs = Editor.Document.Paragraphs;
            if (service == null || !LanguageActive || position.Paragraph < 0 || position.Paragraph >= paragraphs.Count) return null;
            var snap = LangSnapshot.Of(paragraphs);
            int epoch = _langEpoch;
            int at = snap.Global(new TextPosition(position.Paragraph, Math.Min(position.Offset, paragraphs[position.Paragraph].Length)));
            var lines = DiagnosticsAt(position).Select(d => d.Message).ToList();
            LanguageHover? hover;
            try { hover = await service.HoverAsync(snap.Text, LanguageText.PositionOf(snap.Text, at), cancellation); }
            catch (OperationCanceledException) { return null; }
            catch (Exception e)
            {
                ReportLanguageFailure(e);
                hover = null;
            }
            if (cancellation.IsCancellationRequested) return null;
            if (epoch != _langEpoch || !LanguageActive || !snap.Matches(Editor.Document.Paragraphs)) return null;
            if (hover != null && hover.Contents.Length > 0) lines.Add(hover.Contents);
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        void UpdateLanguageHover(Point at)
        {
            if (_lang == null || !LanguageActive) return;
            var loc = Locate(at);
            var place = PointerIsOverText(loc, at) ? loc.Character : NoHover;
            if (place.Equals(_hoverAt)) return;
            _hoverAt = place;
            int request = ++_hoverRequest;
            _hoverCancel?.Cancel();
            _hoverCancel = null;
            if (place.Paragraph < 0) { ToolTip.SetTip(this, null); return; }
            var cancel = _hoverCancel = new CancellationTokenSource();
            _ = ShowHover(request, place, cancel.Token);
        }

        CancellationTokenSource? _hoverCancel;

        async Task ShowHover(int request, TextPosition place, CancellationToken cancellation)
        {
            var text = await GetLanguageHoverAsync(place, cancellation);
            if (request == _hoverRequest) ToolTip.SetTip(this, text);
        }

        void ClearLanguageHover()
        {
            if (_hoverAt.Paragraph < 0) return;
            _hoverAt = NoHover;
            _hoverRequest++;
            _hoverCancel?.Cancel();
            _hoverCancel = null;
            ToolTip.SetTip(this, null);
        }
    }
}
