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
    // answer from another thread or process. It serves a field of one paragraph; a field of several has no language service at work.
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

        /// <summary>What the service found, in offsets of the paragraph, for the paragraph as it was at <see cref="Stamp"/>. After an edit, results that lie
        /// clear of the edit are carried to where they now are (<see cref="Provisional"/>) so the chips do not flicker while the text is asked again; a diagnostic
        /// carried this way has lost its fix, which was made for text that has changed.</summary>
        sealed class LangState
        {
            public Paragraph Paragraph = null!;
            public long Stamp;
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
            public Paragraph Paragraph = null!;
            public long Stamp;
            public int Epoch;
            public string Text = string.Empty;
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

        bool LanguageActive => _lang != null && Editor.Document.Paragraphs.Count == 1;

        /// <summary>The tokens of the paragraph as last answered (carried to where edits have put them), in order; empty when there is no answer for this text yet.</summary>
        public IReadOnlyList<LanguageToken> LanguageTokens
        {
            get
            {
                var s = LiveLanguage();
                var text = Editor.Document.Paragraphs.Count == 1 ? Editor.Document.Paragraphs[0].Text : string.Empty;
                return s == null ? new LanguageToken[0] : s.Tokens.Select(t => new LanguageToken(OffsetRange(text, t.Start, t.End), t.Kind, t.Target)).ToList();
            }
        }

        /// <summary>The diagnostics of the paragraph as last answered, in order. After an edit those clear of it are carried to where they now are, without their fix
        /// (<see cref="ApplyDiagnosticFix"/> needs an answer for the text as it is). Diagnostics are a view: none of them is in the document.</summary>
        public IReadOnlyList<LanguageDiagnostic> LanguageDiagnostics => LiveLanguage()?.Diagnostics.Select(d => d.Diagnostic).ToList() ?? (IReadOnlyList<LanguageDiagnostic>)new LanguageDiagnostic[0];

        static LanguageRange OffsetRange(string text, int start, int end) => new LanguageRange(LanguageText.PositionOf(text, start), LanguageText.PositionOf(text, end));

        /// <summary>The diagnostics that cover the character at a position, or lie at the caret when it is at the end of one, worst first.</summary>
        public IReadOnlyList<LanguageDiagnostic> DiagnosticsAt(TextPosition position)
        {
            var s = LiveLanguage();
            if (s == null || position.Paragraph != 0) return new LanguageDiagnostic[0];
            int at = position.Offset;
            return s.Diagnostics.Where(d => (at >= d.Start && at < d.End) || (d.Start == d.End && at == d.Start)).OrderBy(d => d.Diagnostic.Severity).Select(d => d.Diagnostic).ToList();
        }

        /// <summary>Applies the fix of a diagnostic this field showed, as one undo step. False, and nothing changes, when the field is read-only or the range may not be
        /// edited, the diagnostic has no fix, or the answer it came from is not for the text as it is (an edit since, or one still being asked). The diagnostic is
        /// one of <see cref="LanguageDiagnostics"/>, or from <c>ContextInfo.Diagnostics</c>.</summary>
        public bool ApplyDiagnosticFix(LanguageDiagnostic diagnostic)
        {
            if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
            var s = LiveLanguage();
            var fix = diagnostic.Fix;
            if (s == null || s.Provisional || fix == null || fix.Edits.Count == 0 || !s.Diagnostics.Any(d => ReferenceEquals(d.Diagnostic, diagnostic) || d.Diagnostic.Equals(diagnostic))) return false;
            var paragraph = Editor.Document.Paragraphs[0];
            string text = paragraph.Text;
            var edits = fix.Edits.Select(e => (Start: LanguageText.StartOffset(text, e.Range), End: LanguageText.EndOffset(text, e.Range), e.NewText)).OrderBy(e => e.Start).ThenBy(e => e.End).ToList();
            for (int i = 0; i < edits.Count; i++)
                if (edits[i].End < edits[i].Start || (i > 0 && edits[i].Start < edits[i - 1].End)) return false;
            return ReplaceLanguage(edits.First().Start, edits.Last().End, ApplyEdits(text, edits), "language fix");
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

        /// <summary>Replaces [start, end) of the paragraph with text in the writing system and properties of the text there, as one undo step; false when the
        /// field is read-only or the range may not be edited.</summary>
        bool ReplaceLanguage(int start, int end, string replacement, string label)
        {
            var a = new TextPosition(0, start);
            var b = new TextPosition(0, end);
            if (Editor.IsReadOnly || !Editor.CanEditRange(a, b)) return false;
            var paragraph = Editor.Document.Paragraphs[0];
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
            if (!LanguageActive || Editor.IsReadOnly || !Editor.CanEdit) return false;
            Editor.Ordered(out var a, out var b);
            string text = Editor.Document.Paragraphs[0].Text;
            return _lang!.CanInsert(text, OffsetRange(text, a.Offset, b.Offset), kind);
        }

        // ------------------------------------------------ results

        LangState? LiveLanguage()
        {
            var s = _langState;
            if (s == null || !LanguageActive) return null;
            var p = Editor.Document.Paragraphs[0];
            // Not dropped here: an edit moves the stamp before the field is told of it, and what the edit leaves alone is carried over then (LanguageAfterChange).
            return ReferenceEquals(p, s.Paragraph) && p.ChangeStamp == s.Stamp ? s : null;
        }

        static (int Start, int End) Offsets(string text, LanguageRange range)
        {
            int a = Math.Max(0, Math.Min(LanguageText.StartOffset(text, range), text.Length));
            int b = Math.Max(a, Math.Min(LanguageText.EndOffset(text, range), text.Length));
            return (a, b);
        }

        void ApplyAnalysis(LangRequest request, LanguageAnalysis analysis)
        {
            var state = new LangState { Paragraph = request.Paragraph, Stamp = request.Stamp };
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
            if (same && !old!.Provisional && ReferenceEquals(old.Paragraph, state.Paragraph) && old.Stamp == state.Stamp) return;
            _langState = state;
            RefreshLanguageLayout();
            LanguageAnalysisChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Puts the diagnostics on the paragraph's layout, and redraws. A paragraph's tokens are drawn by the field itself, from the state.</summary>
        void RefreshLanguageLayout()
        {
            if (_layouts.Count > 0) ApplyDiagnostics(0, _layouts[0]);
            InvalidateVisual();
        }

        void ApplyDiagnostics(int index, ParagraphLayout? layout)
        {
            if (layout == null || index != 0) return;
            var s = LiveLanguage();
            if (s == null || s.Diagnostics.Count == 0) { layout.SetDiagnosticMarks(null); return; }
            var marks = new List<(int, int, IBrush)>();
            foreach (var d in s.Diagnostics)
            {
                var brush = SeverityBrush(d.Diagnostic.Severity);
                if (brush != null && d.End > d.Start) marks.Add((d.Start, d.End, brush));
            }
            layout.SetDiagnosticMarks(marks);
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
            if (old != null && edit != null && Editor.Document.Paragraphs.Count == 1 && start == 0 && removed == 1 && added == 1)
            {
                var e = edit.Value;
                if (old.Stamp == e.FirstOldStamp && e.Start.Paragraph == 0 && e.OldEnd.Paragraph == 0 && e.NewEnd.Paragraph == 0)
                {
                    var p = Editor.Document.Paragraphs[0];
                    var carried = new LangState { Paragraph = p, Stamp = p.ChangeStamp, Provisional = true };
                    int a = e.Start.Offset, b = e.OldEnd.Offset, shift = e.NewEnd.Offset - b;
                    foreach (var t in old.Tokens)
                    {
                        if (t.End <= a) carried.Tokens.Add(t);
                        else if (t.Start >= b) carried.Tokens.Add(new LangToken(t.Start + shift, t.End + shift, t.Kind, t.Target));
                    }
                    foreach (var d in old.Diagnostics)
                    {
                        if (d.End <= a && d.End > d.Start) carried.Diagnostics.Add(new LangDiag { Start = d.Start, End = d.End, Diagnostic = WithoutFix(d.Diagnostic, d.Diagnostic.Range) });
                        else if (d.Start >= b) carried.Diagnostics.Add(new LangDiag { Start = d.Start + shift, End = d.End + shift, Diagnostic = WithoutFix(d.Diagnostic, OffsetRange(p.Text, d.Start + shift, d.End + shift)) });
                    }
                    _langState = carried;
                }
            }
            RefreshLanguageLayout();
            ScheduleLanguageIfWanted();
            if (_completion != null) ScheduleCompletionRefresh();
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
            var paragraph = Editor.Document.Paragraphs[0];
            var live = LiveLanguage();
            if (live != null && !live.Provisional) return;
            var asking = _langOut;
            if (asking != null && asking.Epoch == _langEpoch && ReferenceEquals(asking.Paragraph, paragraph) && asking.Stamp == paragraph.ChangeStamp) return;
            CancelLanguageRequest();
            _langEdited = false;
            var request = new LangRequest { Paragraph = paragraph, Stamp = paragraph.ChangeStamp, Epoch = _langEpoch, Text = paragraph.Text, Cancel = new CancellationTokenSource() };
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

        /// <summary>On the field's thread: the service has answered. The answer is applied only when the field has the same service, the same paragraph at the same stamp and has not started over.</summary>
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
            var paragraphs = Editor.Document.Paragraphs;
            bool current = wanted && LanguageActive && ReferenceEquals(paragraphs[0], request.Paragraph) && request.Paragraph.ChangeStamp == request.Stamp;
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
            if (index != 0 || _tokenStyles == null || SystemColours.IsHighContrast()) return;
            var s = LiveLanguage();
            if (s == null || s.Tokens.Count == 0) return;
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            foreach (var token in s.Tokens)
            {
                var style = _tokenStyles.Resolve(token.Kind, dark);
                if (style == null || (style.Fill == null && style.Outline == null)) continue;
                var pen = style.Outline == null ? null : new Pen(style.Outline, 1);
                foreach (var r in layout.SelectionRects(token.Start, token.End))
                {
                    var box = new Rect(r.X + 0.5, r.Y + top + 0.5, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1));
                    context.DrawRectangle(style.Fill, pen, new RoundedRect(box, 3));
                }
            }
        }

        /// <summary>For a test: the chips that would be drawn, as start, end and style, for the theme the field is in; empty with none, or in a high contrast theme.</summary>
        internal IReadOnlyList<(int Start, int End, TokenStyle Style)> ChipsDrawn()
        {
            var result = new List<(int, int, TokenStyle)>();
            var s = LiveLanguage();
            if (s == null || _tokenStyles == null || SystemColours.IsHighContrast()) return result;
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            foreach (var token in s.Tokens)
            {
                var style = _tokenStyles.Resolve(token.Kind, dark);
                if (style != null && (style.Fill != null || style.Outline != null)) result.Add((token.Start, token.End, style));
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

        int _hoverOffset = -1;
        int _hoverRequest;

        /// <summary>The help for a place: the messages of the diagnostics there (worst first), then the service's own hover text, one per line; null when there is none, or when the
        /// text changed while the service was asked. A host that shows its own tooltip asks this; the field itself sets its tooltip from it when the pointer rests on text.</summary>
        public Task<string?> GetLanguageHoverAsync(TextPosition position) => GetLanguageHoverAsync(position, CancellationToken.None);

        async Task<string?> GetLanguageHoverAsync(TextPosition position, CancellationToken cancellation)
        {
            var service = _lang;
            if (service == null || !LanguageActive || position.Paragraph != 0) return null;
            var paragraph = Editor.Document.Paragraphs[0];
            long stamp = paragraph.ChangeStamp;
            int epoch = _langEpoch;
            string text = paragraph.Text;
            var lines = DiagnosticsAt(position).Select(d => d.Message).ToList();
            LanguageHover? hover;
            try { hover = await service.HoverAsync(text, LanguageText.PositionOf(text, Math.Min(position.Offset, text.Length)), cancellation); }
            catch (OperationCanceledException) { return null; }
            catch (Exception e)
            {
                ReportLanguageFailure(e);
                hover = null;
            }
            if (cancellation.IsCancellationRequested) return null;
            if (epoch != _langEpoch || !LanguageActive || !ReferenceEquals(Editor.Document.Paragraphs[0], paragraph) || paragraph.ChangeStamp != stamp) return null;
            if (hover != null && hover.Contents.Length > 0) lines.Add(hover.Contents);
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        void UpdateLanguageHover(Point at)
        {
            if (_lang == null || !LanguageActive) return;
            var loc = Locate(at);
            int offset = PointerIsOverText(loc, at) ? loc.Character.Offset : -1;
            if (offset == _hoverOffset) return;
            _hoverOffset = offset;
            int request = ++_hoverRequest;
            _hoverCancel?.Cancel();
            _hoverCancel = null;
            if (offset < 0) { ToolTip.SetTip(this, null); return; }
            var cancel = _hoverCancel = new CancellationTokenSource();
            _ = ShowHover(request, offset, cancel.Token);
        }

        CancellationTokenSource? _hoverCancel;

        async Task ShowHover(int request, int offset, CancellationToken cancellation)
        {
            var text = await GetLanguageHoverAsync(new TextPosition(0, offset), cancellation);
            if (request == _hoverRequest) ToolTip.SetTip(this, text);
        }

        void ClearLanguageHover()
        {
            if (_hoverOffset < 0) return;
            _hoverOffset = -1;
            _hoverRequest++;
            _hoverCancel?.Cancel();
            _hoverCancel = null;
            ToolTip.SetTip(this, null);
        }
    }
}
