using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SIL.TextEditing.Avalonia
{
    // Host-supplied spell checking (ADR 0020): the host gives a checker per writing system; the field checks the paragraphs in view
    // while the application is idle and the layout draws the squiggles. The rules (words, skipped text) are in SpellScanner.
    public partial class RichTextField
    {
        ISpellCheckHost? _spellHost;
        ISpellCheckHost? _spellGuard;
        ISpellCheckHost? _spellListening;
        readonly HashSet<string> _spellFailed = new HashSet<string>();
        bool _spellPosted;
        ScrollViewer? _spellScroll;
        static readonly IReadOnlyList<SpellingMark> NoMarks = new SpellingMark[0];

        /// <summary>How far below the top of a field the checking goes when nothing says how much of it is in view (the field is in no
        /// scroll viewer and has no window yet to measure against): about a screenful and a half.</summary>
        const double UnmeasuredSpan = 2000;

        /// <summary>The host's spell checking. Null, the default, checks nothing. Setting it starts the checking over
        /// (<see cref="RestartSpelling"/>); the field listens to the host's <see cref="ISpellCheckHost.DictionariesChanged"/> while it is in
        /// the visual tree.</summary>
        public ISpellCheckHost? SpellChecking
        {
            get => _spellHost;
            set
            {
                StopListening();
                ReleaseBatch();
                _spellHost = value;
                _spellGuard = value == null ? null : new GuardedHost(this, value);
                // The word of a double-click, Ctrl+arrow, find and context request is the word the spell checker marks while the host has no word rule of its own (WordRules).
                UpdateWordRules();
                if (_onScreen) StartListening();
                RestartSpelling();
            }
        }

        /// <summary>Raised on the field's thread when a checker or the host throws while the field is checking: the writing system, and the
        /// exception. The field then stops checking that writing system (until <see cref="RestartSpelling"/> or a new host) and goes on with the
        /// others, so a failing dictionary does not end the application. Raised once per writing system until it is checked again. When
        /// nothing handles it, the failure is written to <see cref="System.Diagnostics.Trace"/> (once per writing system, as it is raised), so it is not silent.
        /// A host that repairs the dictionary calls <see cref="ISpellCheckHost.DictionariesChanged"/> (or <see cref="RestartSpelling"/>), which tries the
        /// writing system again.</summary>
        public event Action<string, Exception>? SpellCheckerFailed;

        /// <summary>For a test: where the trace line of a failure nobody handles goes; null (the default) writes it to
        /// <see cref="System.Diagnostics.Trace"/>.</summary>
        internal Action<string>? SpellTraceSink { get; set; }

        /// <summary>For a test: how many times checking has been restarted.</summary>
        internal int SpellRestarts { get; private set; }

        /// <summary>For a test: how many checker wrappers the field has made (one per writing system and checker instance until a restart).</summary>
        internal int SpellWrappersCreated { get; private set; }

        /// <summary>For a test: how idle work is asked for. It is given the action to run when the application is idle; null (the default)
        /// posts it to the field's dispatcher at background priority, after input and layout. One request is pending at a time.</summary>
        internal Action<Action>? SpellIdlePost { get; set; }

        /// <summary>For a test: the part of the field in view as top and bottom in the field's coordinates; null works it out from the
        /// scroll viewer the field is in, else from its window, else a fixed span below the top.</summary>
        internal Func<(double Top, double Bottom)>? SpellVisibleSpan { get; set; }

        /// <summary>Forgets every result and checks again as idle time allows. Call it when the styles changed or a checker's answers changed
        /// without <see cref="ISpellCheckHost.DictionariesChanged"/> being raised. The field does it itself when a word is added
        /// (<see cref="AddToDictionary"/>) or ignored, when the read-only state changes (<c>IsReadOnly</c> of the field or of its editor), when
        /// the host raises <see cref="ISpellCheckHost.DictionariesChanged"/>, and in <see cref="RefreshFromDocument"/>. A writing system that failed is
        /// tried again.
        /// <para>What a host calls when it changes text itself, not through the editor: nothing. The paragraph notices a change to its runs, a run's
        /// text, writing system, object data or property bag (replaced or changed in place), its style, direction or property bag
        /// (<see cref="Paragraph.ChangeStamp"/>), tells the field, and the field lays that paragraph out again, drops its marks and checks it again.
        /// A changed or replaced entry of <c>Document.Paragraphs</c> is not noticed: call <see cref="RefreshFromDocument"/>; a style resolver whose
        /// answers changed, <see cref="InvalidateAllLayouts"/> and, when what is checked changed, this. Edits made through the editor,
        /// <see cref="DocumentEditor.Apply"/> and <see cref="DocumentEditor.ExternalReplaceParagraph"/> need no call.</para></summary>
        public void RestartSpelling()
        {
            SpellRestarts++;
            ReleaseBatchOnRestart();   // a checker that hangs on the old batch must not hold the checking that starts now
            _spellFailed.Clear();
            (_spellGuard as GuardedHost)?.Forget();
            _spellEpoch++;
            _lastAnswer = null;
            _pendingEdit = null;
            for (int i = 0; i < _info.Count; i++)
            {
                _info[i].Spell = null;
                _layouts[i]?.SetSpellingMarks(null);
            }
            InvalidateVisual();
            ScheduleSpellingIfWanted();
        }

        /// <summary>True when every paragraph in view has been checked (or there is no spell checking). Cheap: it looks at the paragraphs in
        /// view only.</summary>
        public bool IsSpellingComplete => NextUncheckedVisible() < 0 && !_spellInFlight;

        /// <summary>For a test: how many words one idle step checks at most. A step looks at the words of one paragraph in view that is not
        /// checked, and goes on from where it stopped the next time, so a slow checker holds up input for at most this many words.</summary>
        internal int SpellStepWords { get; set; } = 25;

        /// <summary>Checks the next few words of the first paragraph in view that is not checked, in document order (FieldWorks checks a step at a
        /// time during idle time), and returns true when nothing in view is left to check. A step is bounded by words, not by paragraphs: a long
        /// paragraph takes many steps and a short one, or what an edit left to check, one. The idle work calls it; a host or test may too. A
        /// checker that throws is reported through <see cref="SpellCheckerFailed"/>, not thrown.
        /// <para>When the words of the step are for a checker that is an <see cref="IBackgroundSpellChecker"/>, their dictionary questions are sent to a
        /// worker and the step returns false, whatever is left: the marks arrive on this thread when the worker has answered (the idle work then goes on by
        /// itself), and until then a further call starts nothing new. A host or test that wants to wait for the end uses <see cref="IsSpellingComplete"/> as
        /// the dispatcher runs.</para></summary>
        public bool CheckSpellingStep()
        {
            if (_spellHost == null) return true;
            if (_spellInFlight) return false;
            int i = NextUncheckedVisible();
            if (i < 0) return true;
            CheckSomeWords(i);
            InvalidateVisual();
            return !_spellInFlight && NextUncheckedVisible() < 0;
        }

        /// <summary>What has been checked of a paragraph: the marks found so far and the stretches of text still to check, both for the paragraph as it was
        /// when <see cref="Stamp"/> was taken. An edit moves the marks outside it with the text and leaves only the words around the edit to check.</summary>
        sealed class SpellState
        {
            public readonly List<SpellingMark> Marks = new List<SpellingMark>();
            /// <summary>Stretches [Start, End) still to check, in order, apart; a word that starts inside one is checked whole.</summary>
            public readonly List<(int Start, int End)> Pending = new List<(int, int)>();
            public long Stamp;
            /// <summary>Where the last step stopped at its word budget, or -1: the next step goes on from there (the end of a word) instead of
            /// looking back at the word before it, which only text that was changed needs.</summary>
            public int ResumeAt = -1;
        }

        void CheckSomeWords(int index)
        {
            var paragraph = Editor.Document.Paragraphs[index];
            var state = LiveState(index);
            int length = paragraph.Length;
            if (state == null)
            {
                state = new SpellState { Stamp = paragraph.ChangeStamp };
                if (length > 0) state.Pending.Add((0, length));
                _info[index].Spell = state;
            }
            if (state.Pending.Count > 0)
            {
                int limit = SpellLimit(index);
                var (rangeStart, rangeEnd) = state.Pending[0];
                var scan = new SpellScanner.Window(paragraph, _spellGuard!, _styles, Editor.IsReadOnly);
                int pos = rangeStart == state.ResumeAt ? Math.Min(rangeStart, length) : scan.WordStartForRecheck(Math.Min(rangeStart, length));
                int zoneStart = pos, words = 0;
                bool done = false;
                var fresh = new List<SpellingMark>();
                List<SpellScanner.Judged>? asked = null;   // words for a checker that answers off this thread, in order
                while (true)
                {
                    if (words >= SpellStepWords) break;
                    // A word that starts one character after the stretch (past a joiner) may have been one word with what was edited, so it is looked at too.
                    if (!scan.NextWord(pos, out int a, out int b) || a > rangeEnd + 1 || a >= limit) { done = true; break; }
                    var judged = scan.Prepare(a, b);
                    if (judged != null)
                    {
                        var j = judged.Value;
                        if (j.NeedsDictionary && j.Checker is GuardedChecker background && background.Background) (asked ??= new List<SpellScanner.Judged>()).Add(j);
                        else
                        {
                            if (j.NeedsDictionary) judged = j.Checker!.Check(j.Word) ? (SpellScanner.Judged?)null : j.Misspelled();
                            if (judged != null) fresh.Add(MarkOf(judged.Value));
                        }
                    }
                    words++;
                    pos = b;
                }
                if (asked == null)
                {
                    // The words just looked at replace whatever marks lay over them.
                    ApplyStep(state, zoneStart, pos, done, rangeEnd, fresh);
                }
                else
                {
                    // The words are found and judged; the dictionary questions are out. The marks are applied when the answers come back, if nothing changed.
                    SendToWorker(new SpellBatch
                    {
                        Info = _info[index], State = state, Paragraph = paragraph, Stamp = paragraph.ChangeStamp, Epoch = _spellEpoch,
                        ZoneStart = zoneStart, Pos = pos, RangeEnd = rangeEnd, Done = done, Fresh = fresh, Asked = asked,
                    });
                }
            }
            _layouts[index]?.SetSpellingMarks(state.Marks);
        }

        /// <summary>The marks of a paragraph as last checked, in order; empty when it is not checked yet or its text has changed since.</summary>
        public IReadOnlyList<SpellingMark> SpellingMarks(int paragraph) => LiveMarks(paragraph) ?? NoMarks;

        /// <summary>The marked word at a position, with what the context menu needs (ADR 0021): the word, its writing system, why it is
        /// marked and the checker's suggestions. This is the hook the host's context menu reads when it builds its spelling items (the
        /// suggestions of <c>ContextInfo</c>); null when the position is not in a marked word, is in text a line limit hides, or there is
        /// no host. Only the word at the position is checked, so the answer is current and cheap.</summary>
        public SpellingSuggestions? SpellingSuggestionsAt(TextPosition position)
        {
            if (_spellHost == null || position.Paragraph < 0 || position.Paragraph >= Editor.Document.Paragraphs.Count) return null;
            int limit = SpellLimit(position.Paragraph);
            if (position.Offset >= limit && limit < int.MaxValue) return null;
            var paragraph = Editor.Document.Paragraphs[position.Paragraph];
            // The last answer holds while the paragraph, the checkers and the read-only state are as they were: moving the pointer about a
            // word asks nobody anything.
            var last = _lastAnswer;
            if (last != null && ReferenceEquals(last.Paragraph, paragraph) && last.Stamp == paragraph.ChangeStamp && last.Epoch == _spellEpoch
                && last.ReadOnly == Editor.IsReadOnly && last.Limit == limit && position.Offset >= last.Answer.Start.Offset && position.Offset <= last.Answer.End.Offset)
                return last.Answer.Again(position.Paragraph, paragraph);
            var answer = SpellScanner.SuggestionsAt(paragraph, position.Paragraph, position.Offset, _spellGuard!, _styles, Editor.IsReadOnly, limit, null);
            if (answer != null)
                _lastAnswer = new LastAnswer { Paragraph = paragraph, Stamp = paragraph.ChangeStamp, Epoch = _spellEpoch, ReadOnly = Editor.IsReadOnly, Limit = limit, Answer = answer };
            return answer;
        }

        sealed class LastAnswer
        {
            public Paragraph Paragraph = null!;
            public long Stamp;
            public int Epoch;
            public bool ReadOnly;
            public int Limit;
            public SpellingSuggestions Answer = null!;
        }

        LastAnswer? _lastAnswer;
        int _spellEpoch;

        /// <summary>Replaces the marked word with text in the word's writing system: one run with the properties of the word's first run, as
        /// one undo step. For a misspelling, a suggestion; for a <see cref="SpellingProblem.Mixed"/> word it makes the whole word one writing
        /// system (to keep each part's writing system use the overload that takes runs). False, and nothing changes, for an
        /// <see cref="SpellingProblem.Embedded"/> word (the text would lose its object; use the overload), when the text can no longer be
        /// edited, is hidden by a line limit, or is no longer the word the menu was made for.</summary>
        public bool ApplySpellingSuggestion(SpellingSuggestions suggestions, string replacement)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (suggestions.Problem == SpellingProblem.Embedded || !StillTheWord(suggestions)) return false;
            var paragraph = Editor.Document.Paragraphs[suggestions.Start.Paragraph];
            var first = paragraph.Slice(suggestions.Start.Offset, suggestions.End.Offset)[0];
            return ReplaceSpelling(suggestions, new List<Run> { new Run { Ws = first.Ws, Text = replacement, Props = first.Props.Clone() } });
        }

        /// <summary>Replaces the marked word with runs, as one undo step: each keeps its own writing system, properties and, for an object,
        /// its object data. This is how "insert missing spaces" is done (<see cref="SpellingSuggestions.WithMissingSpaces"/>) for a mixed or
        /// embedded word, and how a host makes any other choice for a word that is not a plain misspelling. False, and nothing changes, when
        /// there are no runs, the text can no longer be edited, is hidden by a line limit, or is no longer the word the menu was made for.</summary>
        public bool ApplySpellingSuggestion(SpellingSuggestions suggestions, IReadOnlyList<Run> replacement)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (replacement.Count == 0 || !StillTheWord(suggestions)) return false;
            return ReplaceSpelling(suggestions, replacement.Select(r => r.Clone()).ToList());
        }

        bool ReplaceSpelling(SpellingSuggestions suggestions, List<Run> runs)
        {
            if (Editor.IsReadOnly || !Editor.CanEditRange(suggestions.Start, suggestions.End)) return false;
            var fragment = new Paragraph { DefaultWs = runs[0].Ws };
            fragment.Runs.AddRange(runs);
            Editor.CloseUndoGroup();
            Editor.ReplaceRange(suggestions.Start, suggestions.End, new List<Paragraph> { fragment }, "spelling");
            Editor.CloseUndoGroup();
            return true;
        }

        /// <summary>"Add to dictionary": hands the word to its checker (<see cref="ISpellChecker.Add"/>) and checks again. False for a
        /// word that is not a plain misspelling (<see cref="SpellingSuggestions.CanAddToDictionary"/>). The host records its own undo.</summary>
        public bool AddToDictionary(SpellingSuggestions suggestions)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (!suggestions.AddToDictionary()) return false;
            RestartSpelling();
            return true;
        }

        /// <summary>"Ignore": hands the word to its checker (<see cref="ISpellIgnorer.Ignore"/>) and checks again. False for a word that is
        /// not a plain misspelling or whose checker cannot ignore (<see cref="SpellingSuggestions.CanIgnore"/>).</summary>
        public bool IgnoreWord(SpellingSuggestions suggestions)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (!suggestions.IgnoreWord()) return false;
            RestartSpelling();
            return true;
        }

        bool StillTheWord(SpellingSuggestions s)
        {
            int p = s.Start.Paragraph;
            if (p < 0 || p >= Editor.Document.Paragraphs.Count || s.End.Paragraph != p) return false;
            var text = Editor.Document.Paragraphs[p].Text;
            if (s.Start.Offset < 0 || s.End.Offset > text.Length || s.Start.Offset >= s.End.Offset) return false;
            if (s.Start.Offset >= SpellLimit(p)) return false;
            var slice = text.Substring(s.Start.Offset, s.End.Offset - s.Start.Offset);
            if (slice == s.Word) return true;
            try { return slice.Normalize(NormalizationForm.FormC) == s.Word; }
            catch (ArgumentException) { return false; }
        }

        // ------------------------------------------------ a host that fails must not end the application

        void SpellFailure(string ws, Exception error)
        {
            if (!_spellFailed.Add(ws)) return;
            var handler = SpellCheckerFailed;
            if (handler == null)
            {
                Trace("Spell checking of writing system '" + ws + "' failed and is switched off until it is restarted: " + error);
                return;
            }
            try { handler(ws, error); }
            catch (Exception thrown)
            {
                // A handler that throws must not take the idle work down with it.
                Trace("The SpellCheckerFailed handler threw for writing system '" + ws + "': " + thrown);
            }
        }

        void Trace(string message)
        {
            var sink = SpellTraceSink;
            if (sink != null) sink(message);
            else System.Diagnostics.Trace.TraceWarning("SIL.TextEditing: " + message);
        }

        /// <summary>The host as the scanner sees it: a call that throws is reported and the writing system is left alone from then on.</summary>
        sealed class GuardedHost : ISpellCheckHost
        {
            readonly RichTextField _field;
            readonly ISpellCheckHost _inner;
            // One wrapper per writing system for as long as the host keeps answering with the same checker, so a word does not allocate one.
            // The host is still asked every time, so a checker it swaps for another is noticed.
            readonly Dictionary<string, (ISpellChecker Inner, ISpellChecker Wrapper)> _wrappers = new Dictionary<string, (ISpellChecker, ISpellChecker)>();

            public GuardedHost(RichTextField field, ISpellCheckHost inner)
            {
                _field = field;
                _inner = inner;
            }

            public ISpellChecker? GetChecker(string ws)
            {
                if (_field._spellFailed.Contains(ws)) return null;
                try
                {
                    var checker = _inner.GetChecker(ws);
                    if (checker == null)
                    {
                        _wrappers.Remove(ws);
                        return null;
                    }
                    if (_wrappers.TryGetValue(ws, out var cached) && ReferenceEquals(cached.Inner, checker)) return cached.Wrapper;
                    ISpellChecker wrapper = checker is ISpellIgnorer ? new GuardedIgnoringChecker(_field, ws, checker) : new GuardedChecker(_field, ws, checker);
                    _wrappers[ws] = (checker, wrapper);
                    _field.SpellWrappersCreated++;
                    return wrapper;
                }
                catch (Exception e)
                {
                    _field.SpellFailure(ws, e);
                    return null;
                }
            }

            /// <summary>Forgets the wrappers: a restart starts from what the host says now.</summary>
            public void Forget() => _wrappers.Clear();

            // The word rule is the field's own (RichTextField.WordRules) when the host set one, else the spell host's, so the marks and the double-click agree.
            public bool IsWordForming(string ws, int codePoint)
            {
                var own = _field._wordRules;
                try { return (own ?? _inner).IsWordForming(ws, codePoint); }
                catch (Exception e)
                {
                    if (own != null) _field.WordRulesFailed(e);
                    else _field.SpellFailure(ws, e);
                    return SpellScanner.DefaultIsWordForming(codePoint);
                }
            }

            public bool JoinsWords(string ws, int codePoint)
            {
                var own = _field._wordRules;
                try { return (own ?? _inner).JoinsWords(ws, codePoint); }
                catch (Exception e)
                {
                    if (own != null) _field.WordRulesFailed(e);
                    else _field.SpellFailure(ws, e);
                    return SpellScanner.DefaultJoinsWords(codePoint);
                }
            }

            // The field listens to the host itself; nothing listens to the guard.
            public event EventHandler? DictionariesChanged { add { } remove { } }
        }

        class GuardedChecker : ISpellChecker
        {
            readonly RichTextField _field;
            readonly string _ws;
            protected readonly ISpellChecker Inner;

            public GuardedChecker(RichTextField field, string ws, ISpellChecker inner)
            {
                _field = field;
                _ws = ws;
                Inner = inner;
            }

            // A word that cannot be judged is taken as right: nothing is marked on a failure.
            public bool Check(string word)
            {
                if (_field._spellFailed.Contains(_ws)) return true;
                try { return Inner.Check(word); }
                catch (Exception e)
                {
                    _field.SpellFailure(_ws, e);
                    return true;
                }
            }

            internal string Ws => _ws;

            /// <summary>True when the host says the checker may be asked on a worker thread.</summary>
            internal bool Background => Inner is IBackgroundSpellChecker;

            /// <summary>The host's checker asked directly, for the worker: nothing of the field is touched, so an exception is the caller's to catch and
            /// report on the field's thread.</summary>
            internal bool CheckOnWorker(string word) => Inner.Check(word);

            // The suggestions for a word do not change until the field restarts (it makes new wrappers then), so asking again is free.
            readonly Dictionary<string, IReadOnlyList<string>> _suggestions = new Dictionary<string, IReadOnlyList<string>>();
            const int MaxCachedSuggestions = 256;

            public IReadOnlyList<string> Suggest(string word)
            {
                if (_suggestions.TryGetValue(word, out var known)) return known;
                try
                {
                    var list = Inner.Suggest(word);
                    if (list != null)
                    {
                        if (_suggestions.Count >= MaxCachedSuggestions) _suggestions.Clear();
                        _suggestions[word] = list;
                    }
                    return list ?? new string[0];
                }
                catch (Exception e)
                {
                    _field.SpellFailure(_ws, e);
                    return new string[0];
                }
            }

            // A user's choice is made on purpose, so an exception here is the host's to see.
            public void Add(string word) => Inner.Add(word);
        }

        sealed class GuardedIgnoringChecker : GuardedChecker, ISpellIgnorer
        {
            public GuardedIgnoringChecker(RichTextField field, string ws, ISpellChecker inner) : base(field, ws, inner) { }
            public void Ignore(string word) => ((ISpellIgnorer)Inner).Ignore(word);
        }

        // ------------------------------------------------ an edit moves the marks it leaves alone

        TextEdit? _pendingEdit;

        void OnTextEdited(TextEdit edit) => _pendingEdit = edit;

        /// <summary>Before the paragraphs [start, start + removed) are replaced: what was known of them.</summary>
        List<ParaInfo> SpellBeforeChange(int start, int removed)
        {
            var old = new List<ParaInfo>();
            if (_spellHost == null) return old;
            for (int i = 0; i < removed && start + i < _info.Count; i++) old.Add(_info[start + i]);
            return old;
        }

        /// <summary>After the paragraphs were replaced (the new <see cref="ParaInfo"/> are in place): the marks the edit left alone are kept, moved with
        /// the text, and only the words around the edit are left to check, so the other squiggles of the paragraph stay while a person types.
        /// A change whose range is not known, or whose first and last paragraph were not as the field last saw them, starts the paragraph over.</summary>
        void SpellAfterChange(int start, int added, List<ParaInfo> old)
        {
            var edit = _pendingEdit;
            _pendingEdit = null;
            if (_spellHost == null || edit == null || old.Count == 0 || added == 0) return;
            var e = edit.Value;
            if (e.Start.Paragraph != start || e.OldEnd.Paragraph - start + 1 != old.Count || e.NewEnd.Paragraph - start + 1 != added || start + added > _info.Count) return;
            var head = old[0].Spell != null && old[0].Spell!.Stamp == e.FirstOldStamp ? old[0].Spell : null;
            var tail = old[old.Count - 1].Spell != null && old[old.Count - 1].Spell!.Stamp == e.LastOldStamp ? old[old.Count - 1].Spell : null;
            int a = e.Start.Offset, b = e.OldEnd.Offset, ne = e.NewEnd.Offset, shift = ne - b;
            var paragraphs = Editor.Document.Paragraphs;
            // A paragraph with a line limit is left to be checked afresh: an edit moves where the limit cuts it, which brings text into view that
            // nothing was pending for (and pushes text out), and such paragraphs are short by design.
            bool Limited(int index) => FormatOf(index).MaxLines > 0;
            if (added == 1)
            {
                if (Limited(start)) return;
                if (!e.PropertiesKept) tail = null;   // the text after the edit came from a paragraph with other properties: what was known of it may not hold
                var p = paragraphs[start];
                _info[start].Spell = Compose(head, a, true, tail, b, shift, true, a, ne, p.Length, p.ChangeStamp, i => JoinerAt(p, i));
                return;
            }
            var first = paragraphs[start];
            if (!Limited(start)) _info[start].Spell = Compose(head, a, true, null, 0, 0, false, a, first.Length, first.Length, first.ChangeStamp, i => JoinerAt(first, i));
            if (!e.PropertiesKept || Limited(start + added - 1)) return;
            var last = paragraphs[start + added - 1];
            _info[start + added - 1].Spell = Compose(null, 0, false, tail, b, shift, true, 0, ne, last.Length, last.ChangeStamp, i => JoinerAt(last, i));
        }

        /// <summary>True when the character at the index of the paragraph joins the words on each side of it (an apostrophe), as the host says.</summary>
        bool JoinerAt(Paragraph paragraph, int index)
        {
            var text = paragraph.Text;
            if (index < 0 || index >= text.Length || _spellGuard == null || char.IsSurrogate(text[index])) return false;   // the joiners are all in the basic plane
            var run = paragraph.RunAfter(index, out _);
            return _spellGuard.JoinsWords(run?.Ws ?? paragraph.DefaultWs, text[index]);
        }

        /// <summary>The state of a paragraph made of the start of an old one (marks and stretches left to check before <paramref name="headEnd"/>) and the end of
        /// another (after <paramref name="tailStart"/>, moved by <paramref name="shift"/>), with the stretch [dirtyStart, dirtyEnd] between them to check. An
        /// old state of null is not known, so the part it would have kept is all to check.</summary>
        static SpellState Compose(SpellState? head, int headEnd, bool hasHead, SpellState? tail, int tailStart, int shift, bool hasTail, int dirtyStart, int dirtyEnd, int length, long stamp, Func<int, bool> joinerAt)
        {
            var state = new SpellState { Stamp = stamp };
            var pending = new List<(int Start, int End)>();
            if (hasHead)
            {
                if (head == null) pending.Add((0, headEnd));
                else
                {
                    // A mark that ends one character before the edit is safe unless that character joins words: then the edit may have made one word of it and
                    // what follows.
                    foreach (var m in head.Marks)
                    {
                        if (m.End < headEnd && !(m.End == headEnd - 1 && joinerAt(m.End))) state.Marks.Add(m);
                        // A mark that goes is checked again over its own stretch, whatever the edit did to the text beside it: an edit that splits a
                        // surrogate pair leaves a lone surrogate that ends the word the recheck from the edit would start at, so the mark would never be looked at again.
                        else if (m.Start < headEnd) pending.Add((m.Start, m.End));
                    }
                    foreach (var (s, en) in head.Pending) if (s < headEnd) pending.Add((s, Math.Min(en, headEnd)));
                }
            }
            pending.Add((dirtyStart, dirtyEnd));
            if (hasTail)
            {
                if (tail == null) pending.Add((tailStart + shift, length));
                else
                {
                    foreach (var m in tail.Marks)
                    {
                        if (m.Start > tailStart && !(m.Start + shift == dirtyEnd + 1 && joinerAt(m.Start + shift - 1))) state.Marks.Add(new SpellingMark(m.Start + shift, m.Length, m.Problem));
                        else if (m.End > tailStart) pending.Add((m.Start + shift, m.End + shift));   // likewise
                    }
                    foreach (var (s, en) in tail.Pending) if (en > tailStart) pending.Add((Math.Max(s, tailStart) + shift, en + shift));
                }
            }
            // Marks the text no longer holds, and stretches past its end, go; the rest are put in order and joined where they touch.
            state.Marks.RemoveAll(m => m.End > length);
            pending.Sort((x, y) => x.Start != y.Start ? x.Start.CompareTo(y.Start) : x.End.CompareTo(y.End));
            foreach (var (s0, e0) in pending)
            {
                int s = Math.Max(0, Math.Min(s0, length)), en = Math.Max(s, Math.Min(e0, length));
                if (state.Pending.Count > 0 && s <= state.Pending[state.Pending.Count - 1].End)
                    state.Pending[state.Pending.Count - 1] = (state.Pending[state.Pending.Count - 1].Start, Math.Max(state.Pending[state.Pending.Count - 1].End, en));
                else state.Pending.Add((s, en));
            }
            return state;
        }

        // ------------------------------------------------ results and idle work

        /// <summary>What is known of a paragraph's spelling, or null when nothing is or the paragraph has changed since in a way the field was not
        /// told of (<see cref="Paragraph.ChangeStamp"/>: a change made behind the editor's back): a result for other text is never used. Comparing a
        /// number is cheap, so the render path asks every time.</summary>
        SpellState? LiveState(int index)
        {
            if (index < 0 || index >= _info.Count || index >= Editor.Document.Paragraphs.Count) return null;
            var state = _info[index].Spell;
            if (state == null) return null;
            if (state.Stamp != Editor.Document.Paragraphs[index].ChangeStamp)
            {
                _info[index].Spell = null;
                return null;
            }
            return state;
        }

        /// <summary>The marks found so far in a paragraph (all of them once it is checked), or null when it has not been looked at or has changed since.</summary>
        List<SpellingMark>? LiveMarks(int index) => LiveState(index)?.Marks;

        /// <summary>True when part of the paragraph still has to be checked.</summary>
        bool NeedsSpelling(int index)
        {
            var state = LiveState(index);
            return state == null || state.Pending.Count > 0;
        }

        /// <summary>Called as a paragraph is drawn: a mark the layout still draws for a paragraph that has changed since (behind the editor's back) goes.</summary>
        void DropStaleSpelling(int index, ParagraphLayout layout)
        {
            if (_spellHost != null && _info[index].Spell != null && LiveState(index) == null) layout.SetSpellingMarks(null);
        }

        void ApplySpelling(int index, ParagraphLayout layout)
        {
            var marks = LiveMarks(index);
            if (marks != null) layout.SetSpellingMarks(marks);
        }

        /// <summary>The end of the text of a paragraph that shows: where a line limit cuts it, or no end at all.</summary>
        int SpellLimit(int index) => FormatOf(index).MaxLines > 0 ? ParagraphLayoutFor(index).VisibleEnd : int.MaxValue;

        /// <summary>Where a line limit cuts a paragraph changes with the width, so what was checked of such a paragraph is made again.</summary>
        void ForgetLimitedSpelling()
        {
            if (_spellHost == null) return;
            for (int i = 0; i < _info.Count && i < Editor.Document.Paragraphs.Count; i++)
                if (_info[i].Spell != null && FormatOf(i).MaxLines > 0) _info[i].Spell = null;
        }

        (double Top, double Bottom) VisibleSpan()
        {
            var seam = SpellVisibleSpan;
            if (seam != null) return seam();
            var scroll = _spellScroll;
            if (scroll != null)
            {
                var origin = scroll.TranslatePoint(default, this);
                double height = scroll.Viewport.Height > 0 ? scroll.Viewport.Height : scroll.Bounds.Height;
                if (origin != null && height > 0) return (origin.Value.Y, origin.Value.Y + height);
            }
            // Not in a scroll viewer (or one with no viewport): what the window shows of the field.
            var top = TopLevel.GetTopLevel(this);
            if (top != null && top.Bounds.Height > 0)
            {
                var origin = top.TranslatePoint(default, this);
                if (origin != null) return (origin.Value.Y, origin.Value.Y + top.Bounds.Height);
            }
            return (0, UnmeasuredSpan);
        }

        int NextUncheckedVisible()
        {
            if (_spellHost == null) return -1;
            var (top, bottom) = VisibleSpan();
            double y = 0;
            for (int i = 0; i < _layouts.Count && i < Editor.Document.Paragraphs.Count; i++)
            {
                double height = ParagraphLayoutFor(i).Height;
                if (y + height >= top && y <= bottom && NeedsSpelling(i)) return i;
                y += height;
                if (y > bottom) break;
            }
            return -1;
        }

        /// <summary>Asks for idle time when something in view needs checking.</summary>
        void ScheduleSpellingIfWanted()
        {
            if (_spellHost == null || _spellPosted || !_onScreen || NextUncheckedVisible() < 0) return;
            RequestSpellingIdle();
        }

        /// <summary>Asks for idle time now, without looking at the layout (a document change is not the moment to lay paragraphs out):
        /// the idle work finds out what is due. One request is pending at a time, and none while the field is not in the visual tree.</summary>
        void RequestSpellingIdle()
        {
            if (_spellHost == null || _spellPosted || !_onScreen) return;
            _spellPosted = true;
            void Step()
            {
                _spellPosted = false;
                // A batch that is out asks for the next step itself when it comes back.
                if (_spellHost != null && _onScreen && !CheckSpellingStep() && !_spellInFlight) RequestSpellingIdle();
            }
            var post = SpellIdlePost;
            if (post != null) post(Step);
            else OwnDispatcher.Post(Step, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        // ------------------------------------------------ the visual tree

        /// <summary>The field is now in the visual tree: it follows the scroll viewer it is in and the host's notices, and checks what is due.</summary>
        void WatchScrolling()
        {
            StopWatchingScrolling();
            _spellScroll = this.FindAncestorOfType<ScrollViewer>();
            if (_spellScroll != null) _spellScroll.PropertyChanged += OnScrollerChanged;
            StartListening();
            ScheduleSpellingIfWanted();
        }

        void StopWatchingScrolling()
        {
            if (_spellScroll != null) _spellScroll.PropertyChanged -= OnScrollerChanged;
            _spellScroll = null;
            StopListening();
        }

        void StartListening()
        {
            if (_spellHost == null || _spellListening == _spellHost) return;
            StopListening();
            _spellListening = _spellHost;
            _spellHost.DictionariesChanged += OnDictionariesChanged;
        }

        void StopListening()
        {
            if (_spellListening != null) _spellListening.DictionariesChanged -= OnDictionariesChanged;
            _spellListening = null;
        }

        void OnDictionariesChanged(object? sender, EventArgs e)
        {
            if (CheckAccess()) RestartSpelling();
            else OwnDispatcher.Post(RestartSpelling, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        void OnScrollerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != ScrollViewer.OffsetProperty && e.Property != ScrollViewer.ViewportProperty) return;
            if (_spellHost == null) return;
            // The content moves when the scroll viewer next arranges it, so ask once that has happened.
            OwnDispatcher.Post(ScheduleSpellingIfWanted, global::Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}
