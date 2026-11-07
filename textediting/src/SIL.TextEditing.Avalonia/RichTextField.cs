using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>
    /// A rich text editing surface: one <see cref="SIL.TextEditing.Document"/>, paragraphs laid out with
    /// TextFormatter, and its own caret, selection, hit testing and IME client. Every run carries its
    /// writing system. No Avalonia type leaks into the model project.
    /// </summary>
    public partial class RichTextField : Control
    {
        readonly List<ParagraphLayout?> _layouts = new List<ParagraphLayout?>();
        // One entry per paragraph, kept in step with _layouts: what a paragraph's layout needs from the paragraphs before it.
        readonly List<ParaInfo> _info = new List<ParaInfo>();

        const int NoNumber = int.MinValue;

        sealed class ParaInfo
        {
            /// <summary>The resolved paragraph format; it depends only on the paragraph itself.</summary>
            public ParaFormat? Format;
            /// <summary>The paragraph's place in its numbered run, or <see cref="NoNumber"/> until worked out.</summary>
            public int Number = NoNumber;
            /// <summary>The bullet or number text, or null until worked out.</summary>
            public string? Label;
            /// <summary>What has been checked of the paragraph's spelling (null until looked at), for the paragraph as it was (see <see cref="SpellState"/>).</summary>
            public SpellState? Spell;
            /// <summary>The <see cref="Paragraph.ChangeStamp"/> the format, label and layout above were made for, or 0 until the first is made. The field
            /// notices a paragraph that changed behind the editor's back by this (issue 101).</summary>
            public long Stamp;
            /// <summary>The paragraph this entry watches for changes (see <c>Watch</c>).</summary>
            public Paragraph? Watched;
        }
        readonly InputMethodClient _ime;
        IStyleResolver? _styles;
        double _layoutWidth = -1;
        global::Avalonia.Threading.DispatcherTimer? _pauseTimer;
        string? _preedit;
        string? _heldHigh;
        int _preeditCursor;
        double _goalX = -1;
        CharacterHit? _hit;
        TextPosition _hitPosition;
        bool _dragging;
        bool _moving;
        DragUnit _dragUnit;
        TextPosition _unitStart, _unitEnd;

        enum DragUnit { Character, Word, Paragraph }

        static RichTextField()
        {
            FocusableProperty.OverrideDefaultValue<RichTextField>(true);
            ClipToBoundsProperty.OverrideDefaultValue<RichTextField>(true);
        }

        public RichTextField(Document document)
        {
            Editor = new DocumentEditor(document) { AccessCheck = RequireUIThread };
            for (int i = 0; i < document.Paragraphs.Count; i++) { _layouts.Add(null); _info.Add(new ParaInfo()); }
            WatchAll();
            Editor.DocumentChanged += OnDocumentChanged;
            Editor.IsReadOnlyChanged += OnEditorReadOnlyChanged;
            Editor.BlocksFlattened += f => BlocksFlattened?.Invoke(this, f);
            Editor.TextEdited += OnTextEdited;
            Editor.DocumentChanged += (a, b, c) => RaiseAutomationValueChanged();
            Editor.ActiveWsChanged += (o, n) => { if (IsFocused) ActiveWsChanged?.Invoke(o, n); };   // a view without focus does not set the keyboard (#59)
            Editor.CaretMoved += OnCaretMoved;
            Editor.CaretLimit = p => p >= 0 && p < _layouts.Count && p < Editor.Document.Paragraphs.Count && FormatOf(p).MaxLines > 0 ? VisibleLength(p) : int.MaxValue;
            _ime = new InputMethodClient(this);
            TextInputMethodClientRequested += (o, e) => e.Client = _ime;
            GotFocus += (o, e) => { InvalidateVisual(); ActiveWsChanged?.Invoke(Editor.ActiveWs, Editor.ActiveWs); };
            LostFocus += (o, e) => { Editor.CloseUndoGroup(); CloseCompletion(); InvalidateVisual(); };
            Editor.Committed += d => RestartPauseTimer();
            ActualThemeVariantChanged += (o, e) =>
            {
                // The default text colour is one brush the layouts share, changed in place: a theme change is a redraw, not a layout of every paragraph.
                RefreshSystemColours();
            };
            HookContextRequests();
        }

        /// <summary>The dispatcher the field belongs to. Avalonia 12 lets a control live on a dispatcher other than the main UI
        /// thread's; Avalonia 11 has only the one.</summary>
        global::Avalonia.Threading.Dispatcher OwnDispatcher =>
#if NET10_0_OR_GREATER
            Dispatcher;
#else
            global::Avalonia.Threading.Dispatcher.UIThread;
#endif

        void RequireUIThread()
        {
            if (!CheckAccess())
                throw new InvalidOperationException("A RichTextField's document must be edited on the thread of the dispatcher the field belongs to: the field changes Avalonia state when the document changes. Marshal the edit with the field's Dispatcher (the UI thread's, Dispatcher.UIThread, for a field on the main window).");
        }

        /// <summary>The editing session: caret, selection, active writing system and edit events. Like the control, it must be
        /// used on the UI thread: a call that changes the document from another thread throws an
        /// <see cref="InvalidOperationException"/> before anything changes (<see cref="DocumentEditor.AccessCheck"/>).</summary>
        public DocumentEditor Editor { get; }
        public WritingSystemRegistry WritingSystems { get; set; } = new WritingSystemRegistry();
        /// <summary>Resolves the object data of an object run to a picture, or null.</summary>
        public Func<string, IImage?> ObjectImage { get; set; } = d => null;
        /// <summary>Lay objects out as an EM SPACE and paint the picture over it: the workaround for Avalonia issue 21792
        /// (inline objects in right-to-left text are ordered wrongly in 11.3 and 12.1).</summary>
        public bool ObjectAsGlyph { get; set; }
        /// <summary>The Unicode form the host stores (FormD for FieldWorks); null keeps text as it arrives. Forwards to
        /// <see cref="SIL.TextEditing.Document.Normalization"/>.</summary>
        public System.Text.NormalizationForm? Normalization
        {
            get => Editor.Document.Normalization;
            set => Editor.Document.Normalization = value;
        }
        /// <summary>The host's styles (ADR 0004): asked for the formatting of a paragraph or character style name in a writing
        /// system. Null draws only the properties in the runs' own bags.</summary>
        public IStyleResolver? StyleResolver
        {
            get => _styles;
            set
            {
                _styles = value;
                InvalidateAllLayouts();
            }
        }
        IBrush? _foreground, _background;

        // The default text colour, which the text is laid out with until the host sets one. It is one brush the field owns and changes in place
        // when the theme or the system's colours change, so that a change costs a redraw and not a layout of every paragraph.
        SolidColorBrush? _themeForeground;
        bool _themeForegroundStale = true;

        /// <summary>The brush the text is laid out with: the host's colour, else the field's own default.</summary>
        IBrush LayoutForeground => _foreground ?? DefaultForeground();

        SolidColorBrush DefaultForeground()
        {
            _themeForeground ??= new SolidColorBrush();
            if (_themeForegroundStale)
            {
                var resolved = ResolveCaretBrush();
                _themeForeground.Color = resolved is ISolidColorBrush solid ? solid.Color : Colors.Black;
                _themeForeground.Opacity = resolved.Opacity;
                _themeForegroundStale = false;
            }
            return _themeForeground;
        }

        /// <summary>The colour of text and of a border with no colour of its own. Until the host sets one it is the theme's text colour (the
        /// same resource as the caret), or in a high contrast theme the system's window text colour, and it follows a change of either without
        /// the text being laid out again. A colour the host sets is kept as it is, under any theme: a host that sets only this on a dark theme
        /// draws it on the theme's dark page, so it sets <see cref="Background"/> as well (or not either). <see cref="ResetForeground"/> goes
        /// back to following the theme. Read, it is the colour the text is drawn in now (a copy: the field's own default brush changes with the theme and is not handed out).</summary>
        public IBrush Foreground
        {
            get => _foreground ?? new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(DefaultForeground().Color, DefaultForeground().Opacity);
            set
            {
                _foreground = value;
                InvalidateAllLayouts();
            }
        }

        /// <summary>The page colour. Until the host sets one it is the theme's window colour (white when there is no theme), or in a high contrast
        /// theme the system's window colour, so light text taken from a dark theme is not drawn on white. A colour the host sets is kept as it is
        /// (see <see cref="Foreground"/>); <see cref="ResetBackground"/> goes back to following the theme.</summary>
        public IBrush Background
        {
            get => _background ?? ResolveBackgroundBrush();
            set
            {
                _background = value;
                InvalidateVisual();
            }
        }

        /// <summary>Goes back to following the theme for the text colour, after the host has set <see cref="Foreground"/>.</summary>
        public void ResetForeground()
        {
            _foreground = null;
            _themeForegroundStale = true;
            InvalidateAllLayouts();
        }

        /// <summary>Goes back to following the theme for the page colour, after the host has set <see cref="Background"/>.</summary>
        public void ResetBackground()
        {
            _background = null;
            InvalidateVisual();
        }

        /// <summary>Takes the system's colours and high contrast setting again. The field does this by itself when the platform says they changed.</summary>
        internal void RefreshSystemColours()
        {
            SystemColours.Refresh();
            _themeForegroundStale = true;
            DefaultForeground();
            InvalidateVisual();
        }
        public bool ShowCaretAlways { get; set; }
        /// <summary>A field-level read-only flag (forwards to <see cref="DocumentEditor.IsReadOnly"/>): selection and copy
        /// work, nothing changes the text. Runs can also be read-only one by one (ADR 0019).</summary>
        public bool IsReadOnly
        {
            get => Editor.IsReadOnly;
            set
            {
                // Raises IsReadOnlyChanged whoever sets it, which restarts spell checking (ADR 0020), ends a composition (an input method cannot go
                // on composing into read-only text) and tells automation: OnEditorReadOnlyChanged.
                Editor.IsReadOnly = value;
            }
        }
        /// <summary>Single-paragraph mode (ADR 0012; forwards to <see cref="DocumentEditor.SingleParagraph"/>): Enter does not
        /// split the paragraph but is passed to the host through <see cref="EnterRequested"/>.</summary>
        public bool SingleParagraph
        {
            get => Editor.SingleParagraph;
            set => Editor.SingleParagraph = value;
        }
        /// <summary>The block kinds this field may hold (ADR 0033; forwards to <see cref="DocumentEditor.BlockFeatures"/>): tables, lists and headings, all on by
        /// default. The field draws no block UI of its own; the host's commands ask <see cref="IsParagraphStyleAllowed"/> and hide or refuse what is off,
        /// and a paste turns a disallowed block into plain paragraphs and raises <see cref="BlocksFlattened"/>.</summary>
        public BlockFeatures BlockFeatures
        {
            get => Editor.BlockFeatures;
            set => Editor.BlockFeatures = value;
        }
        /// <summary>How a paragraph is recognised as a table row, a list item or a heading (forwards to <see cref="DocumentEditor.BlockRecognizer"/>); a host with its own style names sets it.</summary>
        public IBlockRecognizer BlockRecognizer
        {
            get => Editor.BlockRecognizer;
            set => Editor.BlockRecognizer = value;
        }
        /// <summary>True when the host may give paragraphs this style (<see cref="DocumentEditor.IsParagraphStyleAllowed"/>): false for a table, list or heading style whose feature is off.</summary>
        public bool IsParagraphStyleAllowed(string styleName) => Editor.IsParagraphStyleAllowed(styleName);
        /// <summary>Raised after a paste (a Markdown paste included) that turned blocks the field does not allow into plain paragraphs, so the host can tell the user.
        /// Not raised when nothing was flattened.</summary>
        public event EventHandler<BlockFlattening>? BlocksFlattened;
        /// <summary>The wrap width used when the available width is unbounded.</summary>
        public double PreferredWidth { get; set; } = 400;
        /// <summary>The number of paragraph layouts built so far, for measuring relayout cost.</summary>
        public long LayoutCount { get; private set; }
        public string? Preedit => _preedit;
        public TextInputMethodClient ImeClient => _ime;

        /// <summary>Tells the host which writing system's keyboard to switch to (ADR 0008; the field never touches keyboards).
        /// Raised synchronously, with the old and the new writing system, once per change and never when nothing changed: when the
        /// caret moves into text of another writing system, when a selection's first writing system changes (FieldWorks uses the
        /// first writing system a selection covers, not the end the caret is at), when an edit, an undo or <see cref="DocumentEditor.SetActiveWs"/>
        /// (the writing-system toolbar) changes it. It is also raised when the field gains focus, with the old and new both the
        /// writing system at the caret, because the keyboard then in effect belongs to whatever had focus before and the
        /// writing system here may not have changed. While the field lacks focus it raises nothing, as FieldWorks' views do not
        /// set the keyboard without focus: a change made then (a host setting the caret in a background field, say) is reported
        /// once, as the focus-gain event, with the writing system the field has by then. See docs/reference/editing.md, "Keyboards", for what
        /// a host does on focus in and out.
        ///
        /// <para>On Windows, FieldWorks sets the keyboard on focus gain only when focus comes from a window of the same process, so
        /// a keyboard the user picked elsewhere is kept when focus returns without a click (Alt+Tab, the tray); a click makes a
        /// selection, and the selection sets the keyboard. It restores the default keyboard when a view loses focus to a
        /// control elsewhere in the same application that is not a view. The field cannot tell where focus came from (Avalonia
        /// reports how focus arrived, not from which window or process), so it raises this event on every focus gain and the
        /// host decides whether to switch; to restore the default keyboard the host uses <c>LostFocus</c>, as the field raises
        /// nothing when it loses focus.</para></summary>
        public event Action<string, string>? ActiveWsChanged;
        /// <summary>Raised in single-paragraph mode when Enter is pressed (Shift+Enter is not passed on: it inserts a hard line break): the host decides what it means, as a
        /// dialog's default button does for a FieldWorks view that does not accept Return. The key is left unhandled, so it
        /// also keeps bubbling.</summary>
        public event Action<KeyModifiers>? EnterRequested;
        /// <summary>Tab, Shift+Tab, Ctrl+Tab or an arrow key that cannot move further (Tab and its variants first move to the next, previous, last or first editable place) asks to leave the field (ADR 0021); the
        /// host moves focus.</summary>
        public event EventHandler<LeaveRequestedEventArgs>? LeaveRequested;
        /// <summary>The editor asks the host to undo (Ctrl+Z); the host owns the undo stack.</summary>
        public event Action? UndoRequested;
        public event Action? RedoRequested;
        /// <summary>The writing system of the selection: "ws", or "mixed:a,b".</summary>
        public event Action<string>? SelectionWsState;

        void OnDocumentChanged(int start, int removed, int added)
        {
            var spelling = SpellBeforeChange(start, removed);
            for (int i = 0; i < removed && start < _layouts.Count; i++)
            {
                _layouts[start]?.Dispose();
                _layouts.RemoveAt(start);
                Unwatch(_info[start]);
                _info.RemoveAt(start);
            }
            for (int i = 0; i < added; i++) { _layouts.Insert(start, null); _info.Insert(start, new ParaInfo()); }
            for (int i = 0; i < added && start + i < Editor.Document.Paragraphs.Count; i++) Watch(_info[start + i], Editor.Document.Paragraphs[start + i]);
            LanguageAfterChange(start, removed, added);
            SpellAfterChange(start, added, spelling);
            ForgetNumbersFrom(start);
            RequestSpellingIdle();
            FindDocumentChanged();
            InvalidateMeasure();
            InvalidateVisual();
        }

        // ------------------------------------------------ layout

        /// <summary>The format of a paragraph, resolved once until the paragraph or the styles change.</summary>
        ParaFormat FormatOf(int index) => Current(index).Format ??= ParagraphLayout.ResolveFormat(Editor.Document.Paragraphs[index], WritingSystems, _styles);

        /// <summary>What is known of a paragraph's layout, as long as the paragraph is as it was: a paragraph that changed behind the editor's back
        /// (<see cref="Paragraph.ChangeStamp"/> is not the one its format, label and layout were made for) loses them, and the numbers of the paragraphs
        /// after it are worked out again. Comparing a number is cheap, so every use asks (issue 101).</summary>
        ParaInfo Current(int index)
        {
            if (_stampsStale) SweepStamps();
            var info = _info[index];
            var paragraphs = Editor.Document.Paragraphs;
            if (index >= paragraphs.Count) return info;
            long stamp = paragraphs[index].ChangeStamp;
            if (info.Stamp == stamp) return info;
            bool changed = info.Stamp != 0;
            info.Stamp = stamp;
            if (changed)
            {
                info.Format = null;
                info.Label = null;
                _layouts[index]?.Dispose();
                _layouts[index] = null;
                ForgetNumbersFrom(index);
            }
            return info;
        }

        void ForgetNumbersFrom(int start)
        {
            for (int i = Math.Max(start, 0); i < _info.Count; i++) { _info[i].Number = NoNumber; _info[i].Label = null; }
        }

        /// <summary>True when paragraph <paramref name="k"/> begins a new run of numbers: its scheme differs from the paragraph
        /// above, or it gives a start-at value the one above did not (see <see cref="BulletNumbers.Sequence"/>).</summary>
        bool StartsNumbering(int k)
        {
            var cur = FormatOf(k);
            var prev = FormatOf(k - 1);
            return cur.BulNumScheme != prev.BulNumScheme || (cur.BulNumStartAt != null && cur.BulNumStartAt != prev.BulNumStartAt);
        }

        /// <summary>The number of a paragraph in its run, worked out by walking back only to the last known number or the start of
        /// the run and remembered, so a list costs one step per paragraph however often it is drawn.</summary>
        int NumberOf(int index)
        {
            int k = index;
            while (k > 0 && _info[k].Number == NoNumber && !StartsNumbering(k)) k--;
            int n = _info[k].Number != NoNumber ? _info[k].Number : (FormatOf(k).BulNumStartAt ?? 1);
            _info[k].Number = n;
            for (int j = k + 1; j <= index; j++) _info[j].Number = ++n;
            return _info[index].Number;
        }

        /// <summary>The bullet or number text of a paragraph, or empty for none.</summary>
        string LabelOf(int index)
        {
            var info = _info[index];
            if (info.Label != null) return info.Label;
            var format = FormatOf(index);
            return info.Label = format.BulNumScheme == 0 ? string.Empty : BulletNumbers.Label(format, BulletNumbers.IsNumber(format.BulNumScheme) ? NumberOf(index) : 0);
        }

        public ParagraphLayout ParagraphLayoutFor(int index)
        {
            Current(index);
            var existing = _layouts[index];
            string label = LabelOf(index);
            double above = index > 0 ? FormatOf(index - 1).SpaceAfter : 0;
            if (existing != null)
            {
                if (existing.LabelText == label && existing.PreviousSpaceAfter == above) return existing;
                existing.Dispose();   // the paragraph above changed its number or its space after
            }
            int preeditAt = -1;
            string? preedit = null;
            Run? template = null;
            if (_preedit != null && index == Editor.Caret.Paragraph)
            {
                preeditAt = Editor.Caret.Offset;
                preedit = _preedit;
                template = Editor.TypingRun();
            }
            var layout = ParagraphLayout.Build(Editor.Document.Paragraphs[index], _layoutWidth > 0 ? _layoutWidth : PreferredWidth, WritingSystems, ObjectImage, LayoutForeground, ObjectAsGlyph, preeditAt, preedit, template, _styles, label, above);
            _layouts[index] = layout;
            LayoutCount++;
            ApplySpelling(index, layout);
            ApplyDiagnostics(index, layout);
            return layout;
        }

        public double ParagraphTop(int index)
        {
            double y = 0;
            for (int k = 0; k < index; k++) y += ParagraphLayoutFor(k).Height;
            return y;
        }

        public void InvalidateAllLayouts()
        {
            for (int i = 0; i < _layouts.Count; i++)
            {
                _layouts[i]?.Dispose();
                _layouts[i] = null;
                _info[i] = new ParaInfo { Watched = _info[i].Watched };
            }
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>Call after the host changed the list of paragraphs of the Document directly (not through the editor): added, removed or replaced
        /// one. It drops every layout and resizes the layout list to the paragraph count. A change to a paragraph itself, its runs, a run's text or
        /// properties (set in place too), its style or direction, needs no call: the paragraph tells the field (<see cref="Paragraph.ChangeStamp"/>).</summary>
        public void RefreshFromDocument()
        {
            foreach (var l in _layouts) l?.Dispose();
            UnwatchAll();
            _layouts.Clear();
            _info.Clear();
            FindDocumentChanged();   // the highlight and the current match were worked out from the old text
            for (int i = 0; i < Editor.Document.Paragraphs.Count; i++) { _layouts.Add(null); _info.Add(new ParaInfo()); }
            WatchAll();
            RestartSpelling();       // the paragraphs may be other text: nothing checked before is trusted, and a failed writing system is tried again
            _langState = null;
            CloseCompletion();
            RestartLanguageAnalysis();
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _hostEditNoticed = false;   // a change to a paragraph from here on asks for another measure (see OnParagraphTouched)
            double w = double.IsInfinity(availableSize.Width) ? PreferredWidth : availableSize.Width;
            if (Math.Abs(w - _layoutWidth) > 0.5)
            {
                _layoutWidth = w;
                ForgetLimitedSpelling();   // where a line limit cuts the text moves with the width
                for (int i = 0; i < _layouts.Count; i++)
                {
                    _layouts[i]?.Dispose();
                    _layouts[i] = null;
                }
            }
            double h = 0;
            for (int i = 0; i < _layouts.Count; i++) h += ParagraphLayoutFor(i).Height;
            return new Size(w, Math.Max(h + 2, 24));
        }

        /// <summary>Sets the caret affinity (which visual stop a bidi boundary offset means) for the current caret
        /// offset. Arrow-key movement sets it itself; a host restoring a caret may need it.</summary>
        public void SetCaretHit(CharacterHit hit)
        {
            _hit = hit;
            _hitPosition = Editor.Caret;
        }

        /// <summary>The caret rectangle in the coordinates of this control.</summary>
        public Rect CaretRect()
        {
            var c = Editor.Caret;
            var pl = ParagraphLayoutFor(c.Paragraph);
            var r = pl.CaretRect(c.Offset, false, CurrentHit());
            if (_preedit != null && pl.PreeditAt >= 0) r = pl.CaretRect(pl.PreeditAt + _preeditCursor, true);   // inside the preedit, in effective offsets
            return new Rect(r.X, r.Y + ParagraphTop(c.Paragraph), ParagraphLayout.CaretWidth, r.Height);
        }

        // ------------------------------------------------ rendering

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Background, null, new Rect(Bounds.Size));
            if (_layouts.Count == 0) return;
            Editor.Ordered(out var a, out var b);
            var selectionBrush = Editor.HasSelection ? ResolveSelectionBrush() : null;
            var selectedText = Editor.HasSelection ? SelectedTextColour() : null;
            double y = 0;
            var clip = new Rect(Bounds.Size);
            for (int i = 0; i < _layouts.Count; i++)
            {
                var pl = ParagraphLayoutFor(i);
                DropStaleSpelling(i, pl);
                if (y + pl.Height >= 0 && y <= clip.Bottom + 1)
                {
                    List<Rect>? selection = null;
                    pl.DrawBox(context, y);
                    pl.DrawBackgrounds(context, y);
                    DrawFindHighlights(context, pl, i, y);
                    DrawLanguageChips(context, pl, i, y);
                    if (Editor.HasSelection && i >= a.Paragraph && i <= b.Paragraph)
                    {
                        int s = i == a.Paragraph ? a.Offset : 0;
                        int e = i == b.Paragraph ? b.Offset : pl.Paragraph.Length;
                        selection = pl.SelectionRects(s, e);
                        foreach (var r in selection)
                            context.DrawRectangle(selectionBrush, null, new Rect(r.X, r.Y + y, r.Width, r.Height));
                    }
                    foreach (var li in pl.Lines) if (!li.IsEmpty) li.Draw(context, y);
                    pl.DrawDecorations(context, y);
                    if (selectedText != null && selection != null && pl.InColour(selectedText.Value) is ParagraphLayout recoloured)
                    {
                        // In a high contrast theme the selected text has a colour of its own: the selection's fill is laid again over the text drawn in its
                        // own colours, and the same text in the selection's colour goes on that.
                        foreach (var r in selection)
                        {
                            var box = new Rect(r.X, r.Y + y, r.Width, r.Height);
                            context.DrawRectangle(selectionBrush, null, box);
                            using (context.PushClip(box))
                            {
                                foreach (var li in recoloured.Lines) if (!li.IsEmpty) li.Draw(context, y);
                                recoloured.DrawDecorations(context, y);
                            }
                        }
                    }
                    pl.DrawSquiggles(context, y);
                    pl.DrawLabel(context, y);
                    pl.DrawEllipsis(context, y);
                    if (ObjectAsGlyph)
                    {
                        foreach (var o in pl.ObjectRects())
                        {
                            var img = ObjectImage(o.Value);
                            var rr = new Rect(o.Key.X, y + o.Key.Y + (o.Key.Height - 24) / 2, 32, 24);
                            if (img != null) context.DrawImage(img, rr);
                        }
                    }
                }
                y += pl.Height;
            }
            if (IsFocused || ShowCaretAlways) context.DrawRectangle(ResolveCaretBrush(), null, CaretRect());
            ScheduleSpellingIfWanted();
            ScheduleLanguageIfWanted();
        }

        // ------------------------------------------------ caret movement

        // ADR 0023: a pause of TypingPause ends the typing step even when nothing else follows.
        void RestartPauseTimer()
        {
            // Edits are refused off the UI thread (Editor.AccessCheck), so this is only reached from another thread when a host
            // has removed that check; a DispatcherTimer has thread affinity, so the restart is handed to the field's own dispatcher then. A field
            // that is not on screen has no timer (it closes the step when it leaves the screen).
            if (!CheckAccess())
            {
                OwnDispatcher.Post(RestartPauseTimer);
                return;
            }
            if (!_onScreen)
            {
                _pauseTimer?.Stop();
                _pauseHandle?.Dispose();
                _pauseHandle = null;
                return;
            }
            _pauseTimer?.Stop();
            _pauseHandle?.Dispose();
            _pauseHandle = null;
            if (!Editor.HasOpenUndoGroup) return;
            var scheduler = PauseScheduler;
            if (scheduler != null)
            {
                _pauseHandle = scheduler(Editor.TypingPause, () => Editor.CloseUndoGroup());
                return;
            }
            if (_pauseTimer == null)
            {
#if NET10_0_OR_GREATER
                _pauseTimer = new global::Avalonia.Threading.DispatcherTimer(global::Avalonia.Threading.DispatcherPriority.Background, OwnDispatcher);
#else
                _pauseTimer = new global::Avalonia.Threading.DispatcherTimer();
#endif
                _pauseTimer.Tick += (o, e) => { _pauseTimer.Stop(); Editor.CloseUndoGroup(); };
            }
            _pauseTimer.Interval = Editor.TypingPause;
            _pauseTimer.Start();
        }

        IDisposable? _pauseHandle;

        /// <summary>Starts the one-shot timer that ends a typing step when the pause passes (ADR 0023): it is given the pause
        /// (<see cref="DocumentEditor.TypingPause"/>) and the action to run when it elapses, and returns something that cancels
        /// it; the field cancels and asks again after every edit. Null, the default, uses a dispatcher timer, which the field
        /// does not create while a scheduler is set. Internal: a test with its own clock sets it to drive the pause without
        /// waiting, and the action it is given must run on the field's dispatcher thread, which the field does not check.</summary>
        internal Func<TimeSpan, Action, IDisposable>? PauseScheduler { get; set; }

        bool _onScreen;
        IPlatformSettings? _platformSettings;

        internal void OnPlatformColoursChanged(object? sender, PlatformColorValues values) =>
            OwnDispatcher.Post(RefreshSystemColours);


        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            _onScreen = true;
            _platformSettings = global::Avalonia.Application.Current?.PlatformSettings;
            if (_platformSettings != null) _platformSettings.ColorValuesChanged += OnPlatformColoursChanged;
            RefreshSystemColours();   // the theme and the system may have changed while the field was away
            base.OnAttachedToVisualTree(e);
            WatchScrolling();
            LanguageAttached();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            _onScreen = false;
            CancelBatch();   // a worker that is asking the host's checker stops: the field is going away
            LanguageDetached();
            if (_platformSettings != null) _platformSettings.ColorValuesChanged -= OnPlatformColoursChanged;
            _platformSettings = null;
            StopWatchingScrolling();
            _pauseTimer?.Stop();
            Editor.CloseUndoGroup();
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>The text a caret can reach in a paragraph with a line limit: the part the limit leaves showing. Only such a paragraph
        /// needs a layout to know it; the editor is told there is no limit for any other, so searching for an editable place does not lay
        /// out paragraphs that are not on screen.</summary>
        int VisibleLength(int paragraph) => ParagraphLayoutFor(paragraph).VisibleEnd;

        /// <summary>A position moved back to the end of the visible text when it is in text a line limit hides (FieldWorks puts an
        /// insertion point that would land in truncated text at the last visible position).</summary>
        TextPosition Visible(TextPosition p)
        {
            if (p.Paragraph < 0 || p.Paragraph >= _layouts.Count) return p;
            var pl = ParagraphLayoutFor(p.Paragraph);
            return (pl.Truncated || pl.HiddenTrailingBreak) && p.Offset > pl.VisibleEnd ? new TextPosition(p.Paragraph, pl.VisibleEnd) : p;
        }

        void OnCaretMoved()
        {
            // A caret set from outside (the host, or typing at the end of the visible text) can land in hidden text: bring it back.
            if (!_moving && Editor.Document.Paragraphs.Count == _layouts.Count)
            {
                var caret = Editor.Caret;
                var visible = Visible(caret);
                if (!visible.Equals(caret))
                {
                    Editor.SetCaret(visible, Editor.HasSelection);
                    return;
                }
            }
            if (!_moving) { _goalX = -1; _hit = null; }
            CompletionCaretMoved();
            InvalidateVisual();
            _ime.NotifyCaret();
            if (Editor.HasSelection)
            {
                var set = Editor.SelectionWsSet();
                SelectionWsState?.Invoke(set.Count > 1 ? "mixed:" + string.Join(",", set) : set[0]);
            }
            else SelectionWsState?.Invoke(Editor.ActiveWs);
            // Typing and the host's own caret changes in a field the user is working in ask the host to show the caret (a move the
            // field makes itself asks from Move).
            if (!_moving && !_dragging && IsFocused && Editor.Document.Paragraphs.Count == _layouts.Count) RequestCaretIntoView();
        }

        /// <summary>The caret hit (affinity) that applies to the current caret, or null when it has none.</summary>
        CharacterHit? CurrentHit() => _hitPosition.Equals(Editor.Caret) ? _hit : null;

        void Move(TextPosition p, bool extend, bool keepGoal = false, CharacterHit? hit = null)
        {
            p = Visible(p);
            if (!keepGoal) _goalX = -1;
            _hit = hit;
            _hitPosition = p;
            _moving = true;
            try { Editor.SetCaret(p, extend, p.CompareTo(Editor.Caret)); }
            finally { _moving = false; }
            if (!_dragging) RequestCaretIntoView();
        }

        /// <summary>Left or Right arrow, logical as in FieldWorks: Left goes backwards in a left-to-right paragraph and
        /// forwards in a right-to-left one, Right the reverse, one cluster at a time, even where that moves the caret the
        /// other way on screen inside mixed text. At a paragraph edge it crosses into the neighbour. The caret is drawn at
        /// the visual position of its logical offset: after a forward step on the trailing edge of the cluster passed,
        /// after a backward step on the leading edge of the cluster now after the caret.</summary>
        public bool MoveVisualLeftRight(bool left, bool extend)
        {
            var c = Editor.Caret;
            var para = Editor.Document.Paragraphs[c.Paragraph];
            bool forward = para.Rtl ? left : !left;   // the direction of the paragraph at the selection's focus (the caret)
            if (!extend && Editor.HasSelection)
            {
                Editor.Ordered(out var a, out var b);
                Move(forward ? b : a, false);
                return true;
            }
            if (forward)
            {
                if (c.Offset < VisibleLength(c.Paragraph))
                {
                    int n = GraphemeClusters.Next(para.Text, c.Offset);
                    Move(new TextPosition(c.Paragraph, n), extend, false, ParagraphLayoutFor(c.Paragraph).EndHit(n));
                }
                else if (c.Paragraph + 1 < Editor.Document.Paragraphs.Count) Move(new TextPosition(c.Paragraph + 1, 0), extend);
            }
            else if (c.Offset > 0)
            {
                int n = GraphemeClusters.Previous(para.Text, c.Offset);
                Move(new TextPosition(c.Paragraph, n), extend, false, new CharacterHit(ParagraphLayoutFor(c.Paragraph).ToEffective(n)));
            }
            else if (c.Paragraph > 0)
            {
                int pi = c.Paragraph - 1, len = VisibleLength(pi);
                Move(new TextPosition(pi, len), extend, false, ParagraphLayoutFor(pi).EndHit(len));
            }
            return true;
        }

        /// <summary>Ctrl+Left or Ctrl+Right: to the previous or next word start (see <see cref="WordBreaks"/>). The word
        /// order is logical, so in a right-to-left paragraph Ctrl+Left goes to the next word. At a paragraph edge it
        /// crosses into the neighbouring paragraph.</summary>
        public void MoveByWord(bool left, bool extend)
        {
            var c = Editor.Caret;
            var para = Editor.Document.Paragraphs[c.Paragraph];
            bool backward = para.Rtl ? !left : left;
            if (backward)
            {
                if (c.Offset > 0) Move(new TextPosition(c.Paragraph, WordBreaks.PreviousWordStart(para, c.Offset, Editor.WordRules)), extend);
                else if (c.Paragraph > 0) Move(new TextPosition(c.Paragraph - 1, VisibleLength(c.Paragraph - 1)), extend);
            }
            else
            {
                if (c.Offset < VisibleLength(c.Paragraph)) Move(new TextPosition(c.Paragraph, WordBreaks.NextWordStart(para, c.Offset, Editor.WordRules)), extend);
                else if (c.Paragraph + 1 < Editor.Document.Paragraphs.Count) Move(new TextPosition(c.Paragraph + 1, 0), extend);
            }
        }

        // ADR 0021: a Left or Right that moves nothing, with no selection to collapse, asks to leave the field.
        void MoveOrLeave(bool left, bool ctrl, bool shift)
        {
            var before = Editor.Caret;
            bool hadSelection = Editor.HasSelection;
            bool forward = Editor.Document.Paragraphs[before.Paragraph].Rtl ? left : !left;
            if (ctrl) MoveByWord(left, shift); else MoveVisualLeftRight(left, shift);
            if (!shift && !hadSelection && Editor.Caret.Equals(before))
                RaiseLeave(forward ? LeaveDirection.Next : LeaveDirection.Previous, left ? LeaveKey.Left : LeaveKey.Right, false, double.NaN);
        }

        bool RaiseLeave(LeaveDirection direction, LeaveKey key, bool toEnd, double goalX)
        {
            var args = new LeaveRequestedEventArgs(direction, key, toEnd, goalX);
            LeaveRequested?.Invoke(this, args);
            return args.Handled;
        }

        void MoveVertical(bool down, bool extend)
        {
            var c = Editor.Caret;
            var pl = ParagraphLayoutFor(c.Paragraph);
            var hit = CurrentHit();
            var r = pl.CaretRect(c.Offset, false, hit);
            double gx = _goalX >= 0 ? _goalX : r.X;
            int li = pl.LineIndexOf(c.Offset, hit);
            double top = ParagraphTop(c.Paragraph);
            double y;
            if (down)
            {
                if (pl.Lines.Count > 0 && li + 1 < pl.Lines.Count) y = top + pl.Lines[li + 1].Y + 1;
                else if (c.Paragraph + 1 < _layouts.Count) y = top + pl.Height + 1;
                else
                {
                    if (!extend && RaiseLeave(LeaveDirection.Next, LeaveKey.Down, false, gx)) return;
                    Move(new TextPosition(c.Paragraph, pl.VisibleEnd), extend, true);
                    return;
                }
            }
            else
            {
                if (li > 0) y = top + pl.Lines[li - 1].Y + 1;
                else if (c.Paragraph > 0) y = top - 1;
                else
                {
                    if (!extend && RaiseLeave(LeaveDirection.Previous, LeaveKey.Up, false, gx)) return;
                    Move(new TextPosition(c.Paragraph, 0), extend, true);
                    return;
                }
            }
            var target = Locate(new Point(gx, y));
            _goalX = gx;
            Move(target.Position, extend, true, target.Hit);
        }

        struct Located
        {
            public TextPosition Position;
            public CharacterHit Hit;
            public TextPosition Character;
        }

        Located Locate(Point point)
        {
            double y = 0;
            for (int i = 0; i < _layouts.Count; i++)
            {
                var pl = ParagraphLayoutFor(i);
                if (point.Y < y + pl.Height || i == _layouts.Count - 1)
                {
                    var h = pl.HitTestDetailed(new Point(point.X, point.Y - y));
                    return new Located { Position = new TextPosition(i, h.Offset), Hit = h.CaretHit, Character = new TextPosition(i, h.Character) };
                }
                y += pl.Height;
            }
            return default;
        }

        /// <summary>The document position at a point in the coordinates of this control.</summary>
        public TextPosition PointToPosition(Point point) => Locate(point).Position;

        // ------------------------------------------------ input

        /// <summary>Handles an editing key; returns false for keys it leaves to the host (Tab and the like).</summary>
        public bool HandleKey(Key key, KeyModifiers modifiers)
        {
            if (HandleLanguageKey(key, modifiers)) return true;
            bool shift = (modifiers & KeyModifiers.Shift) != 0, ctrl = (modifiers & KeyModifiers.Control) != 0;
            var lastPara = _layouts.Count - 1;
            switch (key)
            {
                case Key.Left: MoveOrLeave(true, ctrl, shift); return true;
                case Key.Right: MoveOrLeave(false, ctrl, shift); return true;
                case Key.Tab when (modifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) == 0:
                {
                    // FieldWorks moves to the next (previous) editable place first and leaves only when there is none;
                    // Ctrl+Tab goes to the last place and Ctrl+Shift+Tab to the first, and leaves only from there.
                    var which = ctrl ? (shift ? EditablePlace.First : EditablePlace.Last) : (shift ? EditablePlace.Previous : EditablePlace.Next);
                    if (Editor.TryGetEditablePlace(which, out var place)) { Move(place, false); return true; }
                    return RaiseLeave(shift ? LeaveDirection.Previous : LeaveDirection.Next, LeaveKey.Tab, ctrl, double.NaN);
                }
                case Key.Up: MoveVertical(false, shift); return true;
                case Key.F10 when shift && (modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0: RaiseKeyboardContextRequest(); return true;
                case Key.PageUp when (modifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) == 0: if (ctrl) MovePageEdge(false, shift); else MovePage(false, shift); return true;
                case Key.PageDown when (modifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) == 0: if (ctrl) MovePageEdge(true, shift); else MovePage(true, shift); return true;
                case Key.Down: MoveVertical(true, shift); return true;
                case Key.Home:
                {
                    var c = Editor.Caret;
                    if (ctrl) { Move(new TextPosition(0, 0), shift); return true; }
                    var pl = ParagraphLayoutFor(c.Paragraph);
                    int o = pl.Lines.Count > 0 ? pl.ToModel(pl.Lines[pl.LineIndexOf(c.Offset, CurrentHit())].Start) : 0;
                    Move(new TextPosition(c.Paragraph, o), shift);
                    return true;
                }
                case Key.End:
                {
                    var c = Editor.Caret;
                    if (ctrl) { Move(new TextPosition(lastPara, Editor.Document.Paragraphs[lastPara].Length), shift); return true; }
                    var pl = ParagraphLayoutFor(c.Paragraph);
                    if (pl.Lines.Count == 0) { Move(new TextPosition(c.Paragraph, pl.Paragraph.Length), shift); return true; }
                    // At a soft wrap the end of the line and the start of the next are one offset: keep the caret on this line.
                    int o = pl.LineEnd(pl.LineIndexOf(c.Offset, CurrentHit()), out var hit);
                    Move(new TextPosition(c.Paragraph, o), shift, false, hit);
                    return true;
                }
                case Key.Back: if (!JoinsIntoHiddenEnd()) Editor.Backspace(); return true;
                case Key.Delete: if (!AtHiddenEdge() || NextIsBreak()) Editor.DeleteForward(); return true;
                case Key.Enter when shift && (modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0:
                    // A hard line break inside the paragraph in either mode (our default in single-paragraph mode); refused when its
                    // new line would be hidden by the line limit.
                    if (!AtHiddenEdge() && !BreakWouldBeHidden()) Editor.InsertLineBreak();
                    return true;
                case Key.Enter:
                    if (SingleParagraph) { EnterRequested?.Invoke(modifiers); return false; }
                    if (!AtHiddenEdge()) Editor.Enter();
                    return true;
                case Key.A when ctrl:
                    Editor.Anchor = new TextPosition(0, 0);
                    Move(new TextPosition(lastPara, Editor.Document.Paragraphs[lastPara].Length), true);
                    return true;
                case Key.Z when ctrl: Editor.CloseUndoGroup(); UndoRequested?.Invoke(); return true;   // the host's task must end before it undoes
                case Key.Y when ctrl: Editor.CloseUndoGroup(); RedoRequested?.Invoke(); return true;
                case Key.C when ctrl: _ = CopyAsync(false); return true;
                case Key.X when ctrl: _ = CopyAsync(true); return true;
                case Key.V when ctrl: _ = PasteAsync(); return true;
            }
            return false;
        }

        /// <summary>True when an insertion point sits at the end of the visible text of a paragraph whose line limit hides the rest.
        /// Editing there would change text the user cannot see, so typing, Delete, Enter, paste and composition are refused (FieldWorks
        /// does not let an edit start in truncated text). A selection is a deliberate act on visible text and is not refused.</summary>
        bool AtHiddenEdge()
        {
            if (Editor.HasSelection) return false;
            var c = Editor.Caret;
            var pl = ParagraphLayoutFor(c.Paragraph);
            return pl.Truncated && c.Offset >= pl.VisibleEnd;
        }


        /// <summary>True when a hard line break would leave text nobody can see. It lays out the paragraph the replace would leave
        /// (the first one's head, the break and the last one's tail, in the format of the paragraph the replace keeps, see
        /// <see cref="DocumentEditor.KeptParagraph"/>) and refuses when the line limit would hide text or the new line itself.</summary>
        bool BreakWouldBeHidden()
        {
            // The survivor's line limit decides; an unlimited paragraph can hide nothing, so skip the scratch editor and layout.
            var a = Editor.Anchor;
            var b = Editor.Caret;
            if (a.Paragraph > b.Paragraph || (a.Paragraph == b.Paragraph && a.Offset > b.Offset)) (a, b) = (b, a);
            int survivor = Editor.KeptParagraph(a, b);
            if (ParagraphLayout.ResolveFormat(Editor.Document.Paragraphs[survivor], WritingSystems, _styles).MaxLines <= 0) return false;
            var result = Editor.PreviewLineBreak(out int kept);
            if (result == null) return false;
            double above = kept > 0 ? FormatOf(kept - 1).SpaceAfter : 0;
            using var layout = ParagraphLayout.Build(result, _layoutWidth > 0 ? _layoutWidth : PreferredWidth, WritingSystems, ObjectImage, LayoutForeground, ObjectAsGlyph, -1, null, null, _styles, LabelOf(kept), above);
            return layout.Truncated || layout.HiddenTrailingBreak;
        }

        /// <summary>True when the character after the caret is a hard line break. Delete may remove it at the visible end of a
        /// line-limited paragraph: it only removes an invisible separator and so shows the text behind it.</summary>
        bool NextIsBreak()
        {
            if (Editor.HasSelection) return false;
            var c = Editor.Caret;
            var text = Editor.Document.Paragraphs[c.Paragraph].Text;
            return c.Offset < text.Length && text[c.Offset] == DocumentEditor.LineSeparator;
        }

        /// <summary>True when Backspace at the start of a paragraph would join it onto a line-limited paragraph whose end is hidden:
        /// the joined text would vanish from view, so the join is refused.</summary>
        bool JoinsIntoHiddenEnd()
        {
            if (Editor.HasSelection) return false;
            var c = Editor.Caret;
            if (c.Offset != 0 || c.Paragraph == 0) return false;
            var above = ParagraphLayoutFor(c.Paragraph - 1);
            return above.Truncated || above.HiddenTrailingBreak;
        }

        /// <summary>Inserts a held lone high surrogate as it is (it never got its low half).</summary>
        void FlushHeld()
        {
            if (_heldHigh == null) return;
            var h = _heldHigh;
            _heldHigh = null;
            if (!AtHiddenEdge()) Editor.TypeText(h);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            FlushHeld();
            if (HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
            base.OnKeyDown(e);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            var text = e.Text!;
            // A platform may deliver one emoji as two events, one per UTF-16 half. A lone high surrogate is held until the
            // next text arrives so the pair is normalised and inserted whole; any other input flushes it unchanged first.
            if (_heldHigh != null)
            {
                if (char.IsLowSurrogate(text[0])) text = _heldHigh + text;
                else FlushHeld();
                _heldHigh = null;
            }
            if (TextNormalizer.EndsWithLoneHighSurrogate(text))
            {
                _heldHigh = text.Substring(text.Length - 1);
                text = text.Substring(0, text.Length - 1);
                e.Handled = true;
                if (text.Length == 0) return;
            }
            if (_preedit != null)
            {
                _preedit = null;
                _layouts[Editor.Caret.Paragraph]?.Dispose();
                _layouts[Editor.Caret.Paragraph] = null;
            }
            if (!AtHiddenEdge())
            {
                Editor.TypeText(text);
                CompletionAfterTyping(text);
            }
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            FlushHeld();
            var props = e.GetCurrentPoint(this).Properties;
            if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed) return;
            Focus();
            var loc = Locate(e.GetPosition(this));
            if (!props.IsLeftButtonPressed)
            {
                // Right click (context menu): keep a selection that was clicked inside, otherwise move the caret there.
                // Inside means the pointer is over a selected character, not merely beside the selection's end.
                Editor.Ordered(out var sa, out var sb);
                bool inside = Editor.HasSelection && loc.Character.CompareTo(sa) >= 0 && loc.Character.CompareTo(sb) < 0;
                if (!inside) Move(loc.Position, false, false, loc.Hit);
                e.Handled = true;
                return;
            }
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            _dragUnit = DragUnit.Character;
            if (shift) Move(loc.Position, true, false, loc.Hit);
            else if (e.ClickCount == 2) { _dragUnit = DragUnit.Word; SelectUnit(loc, true); }
            else if (e.ClickCount >= 3) { _dragUnit = DragUnit.Paragraph; SelectUnit(loc, true); }
            else Move(loc.Position, false, false, loc.Hit);
            _dragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        /// <summary>The word or paragraph under the pointer as a pair of positions.</summary>
        void UnitAt(Located loc, out TextPosition start, out TextPosition end)
        {
            var para = Editor.Document.Paragraphs[loc.Character.Paragraph];
            if (_dragUnit == DragUnit.Paragraph) { start = new TextPosition(loc.Character.Paragraph, 0); end = new TextPosition(loc.Character.Paragraph, para.Length); return; }
            if (loc.Character.Offset >= para.Length && para.Length > 0)
            {
                // The pointer is on the empty line after a trailing break: no character is under it, so a double click selects nothing.
                start = end = new TextPosition(loc.Character.Paragraph, para.Length);
                return;
            }
            WordBreaks.WordAt(para, loc.Character.Offset, Editor.WordRules, _styles, out int s, out int en);
            start = new TextPosition(loc.Character.Paragraph, s);
            end = new TextPosition(loc.Character.Paragraph, en);
        }

        /// <summary>Selects the word or paragraph under the pointer; while dragging, keeps the first unit selected and
        /// extends to the unit under the pointer.</summary>
        void SelectUnit(Located loc, bool first)
        {
            UnitAt(loc, out var start, out var end);
            if (first) { _unitStart = start; _unitEnd = end; }
            bool before = start.CompareTo(_unitStart) < 0;
            var caret = before ? start : end;
            Editor.Anchor = before ? _unitEnd : _unitStart;
            var hit = caret.Offset > 0 ? ParagraphLayoutFor(caret.Paragraph).EndHit(caret.Offset) : (CharacterHit?)null;
            Move(caret, true, false, before ? null : hit);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            ClearLanguageHover();
            base.OnPointerExited(e);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_dragging)
            {
                UpdateLanguageHover(e.GetPosition(this));
                return;
            }
            var loc = Locate(DragPoint(e.GetPosition(this)));
            if (_dragUnit == DragUnit.Character) Move(loc.Position, true, false, loc.Hit);
            else SelectUnit(loc, false);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            base.OnPointerReleased(e);   // Avalonia raises its context request for a right click from here
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _dragging = false;
        }

        // ------------------------------------------------ clipboard

        /// <summary>The forms a copy writes and a paste reads, registered by the host (ADR 0029). Plain text is built in. A new field has
        /// none registered, so copy writes plain text only until the host adds its forms: TsString through the LCModel adapter and FieldWorks
        /// Markdown through the Markdown library. <see cref="ClipboardForms.PasteOrder"/> is the order paste prefers them.</summary>
        public ClipboardForms ClipboardForms
        {
            get => _clipboardForms;
            set => _clipboardForms = value ?? throw new ArgumentNullException(nameof(value));
        }

        ClipboardForms _clipboardForms = new ClipboardForms();

        /// <summary>Raised after a copy when the backend could not store some forms (the default backend cannot store a host object): the format names. The other forms and
        /// the plain text were stored. A host that writes object payloads sets its own <see cref="ClipboardBackend"/>.</summary>
        public event EventHandler<IReadOnlyList<string>>? ClipboardFormsNotStored;

        IClipboardBackend? _clipboardBackend;

        /// <summary>Where copy, cut and paste keep the clipboard (ADR 0029). The default is Avalonia's clipboard of the window the field is in; a host with its own
        /// (FieldWorks' WinForms host, which puts a serialised object under "TsString") sets one. Payloads of the forms pass through it untouched.</summary>
        public IClipboardBackend ClipboardBackend
        {
            get => _clipboardBackend ??= new AvaloniaClipboardBackend(() => TopLevel.GetTopLevel(this));
            set => _clipboardBackend = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Whether the target has a writing system, asked when text is pasted (ADR 0029). With none set, the field asks its
        /// <see cref="WritingSystems"/> registry, and a registry with no writing systems in it (no project) accepts every one. Pasted text
        /// in a writing system this refuses takes the writing system of the selection it replaces, as FieldWorks does.</summary>
        public Func<string, bool>? KnowsWritingSystem { get; set; }

        /// <summary>Raised after a paste that mapped writing systems the target lacks to the selection's writing system, so the host can
        /// tell the user. Not raised when every writing system was known.</summary>
        public event EventHandler<WritingSystemMapping>? WritingSystemsMapped;

        bool IsWritingSystemKnown(string id) => KnowsWritingSystem != null ? KnowsWritingSystem(id) : WritingSystems.Count == 0 || WritingSystems.Contains(id);

        static string PlainText(IReadOnlyList<Paragraph> fragment) =>
            TextNormalizer.Normalize(string.Join("\n", fragment.Select(p => p.Text)), System.Text.NormalizationForm.FormC);

        /// <summary>Copies the selection, or cuts it: every registered form goes on the clipboard, with the plain text in NFC (ADR 0029).
        /// A form that throws or has nothing to say is left out and the others still go.</summary>
        public async Task CopyAsync(bool cut)
        {
            if (!Editor.HasSelection) return;
            if (cut && !Editor.CanEdit) return;   // Cut is disabled where Delete is: it must not leave the text on the clipboard either
            var fragment = Editor.GetFragment();
            var written = new List<KeyValuePair<string, object>>();
            foreach (var form in ClipboardForms)
            {
                try
                {
                    var payload = form.Write(fragment);
                    if (payload != null && !(payload is string empty && empty.Length == 0)) written.Add(new KeyValuePair<string, object>(form.FormatName, payload));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("Clipboard form " + form.Id + " could not write the selection: " + ex.Message);
                }
            }
            var refused = await ClipboardBackend.SetAsync(PlainText(fragment), written);
            if (refused.Count > 0) ClipboardFormsNotStored?.Invoke(this, refused);
            if (cut) Editor.DeleteSelection("cut");
        }

        /// <summary>"Copy as": puts one form on the clipboard, as text, for a program that reads text (so it returns false for a form whose payload is not a string): the plain text in NFC (the id
        /// <see cref="ClipboardForms.PlainTextId"/>) or a registered form's payload, such as FieldWorks Markdown. Pasting it back into a
        /// field reads it as text. False when nothing was copied: no selection, an unknown form, or a form with nothing to say.</summary>
        public async Task<bool> CopyAsAsync(string formId)
        {
            if (!Editor.HasSelection) return false;
            var fragment = Editor.GetFragment();
            string? text;
            if (formId == ClipboardForms.PlainTextId) text = PlainText(fragment);
            else
            {
                var form = ClipboardForms.Find(formId);
                if (form == null) return false;
                try { text = form.Write(fragment) as string; }   // as text: a form whose payload is not a string cannot be copied as
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("Clipboard form " + form.Id + " could not write the selection: " + ex.Message);
                    return false;
                }
            }
            if (string.IsNullOrEmpty(text)) return false;
            await ClipboardBackend.SetAsync(text!, new List<KeyValuePair<string, object>>());
            return true;
        }

        /// <summary>Pastes the first form of <see cref="ClipboardForms.PasteOrder"/> that the clipboard holds and that reads. Text in a
        /// writing system the target lacks takes the selection's writing system and raises <see cref="WritingSystemsMapped"/> when the paste changed something.</summary>
        public async Task PasteAsync()
        {
            if (AtHiddenEdge() || !Editor.CanEdit) return;   // a read-only field or text that may not be edited reads nothing and tells nobody
            foreach (var id in ClipboardForms.PasteOrder.ToList())
            {
                if (id == ClipboardForms.PlainTextId)
                {
                    var text = await ClipboardBackend.GetTextAsync();
                    if (string.IsNullOrEmpty(text)) continue;
                    Editor.PasteText(text ?? string.Empty);
                    return;
                }
                var form = ClipboardForms.Find(id);
                if (form == null) continue;
                object? payload;
                try { payload = await ClipboardBackend.GetAsync(form.FormatName, form.PayloadKind); }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("The clipboard could not be read as " + form.FormatName + ": " + ex.Message);
                    continue;
                }
                if (payload == null || (payload is string none && none.Length == 0)) continue;
                IReadOnlyList<Paragraph>? read;
                try { read = form.Read(payload); }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("Clipboard form " + form.Id + " could not read the clipboard: " + ex.Message);
                    read = null;
                }
                if (read == null || read.Count == 0) continue;
                var fragment = read.ToList();
                var mapping = ClipboardWritingSystems.MapMissing(fragment, IsWritingSystemKnown, Editor.SelectionWs);
                bool changed = false;
                void Saw(EditDelta d) => changed = true;
                Editor.Committed += Saw;
                try { Editor.Paste(fragment); }
                finally { Editor.Committed -= Saw; }
                if (changed && mapping.Any) WritingSystemsMapped?.Invoke(this, mapping);
                return;
            }
        }

        // ------------------------------------------------ IME

        /// <summary>Shows an uncommitted composition string inline at the caret; it never enters the model.</summary>
        public void SetPreedit(string? text, int? cursor)
        {
            if (text != null && !Editor.CanEdit) text = null;   // an input method cannot compose into text that may not be edited
            if (!string.IsNullOrEmpty(text) && AtHiddenEdge()) text = null;   // no composition at the end of hidden text
            _preedit = string.IsNullOrEmpty(text) ? null : text;
            if (_preedit != null) CloseCompletion();   // an input method is composing: no popup over it
            _preeditCursor = cursor ?? (_preedit?.Length ?? 0);
            int pi = Editor.Caret.Paragraph;
            _layouts[pi]?.Dispose();
            _layouts[pi] = null;
            InvalidateMeasure();
            InvalidateVisual();
            _ime.NotifyCaret();
        }

        sealed class InputMethodClient : TextInputMethodClient
        {
            readonly RichTextField _field;

            public InputMethodClient(RichTextField field) { _field = field; }

            public override Visual TextViewVisual => _field;
            public override bool SupportsPreedit => true;
            public override bool SupportsSurroundingText => true;
            public override string SurroundingText => _field.Editor.Document.Paragraphs[_field.Editor.Caret.Paragraph].Text;
            public override Rect CursorRectangle => _field.CaretRect();

            public override TextSelection Selection
            {
                get
                {
                    var c = _field.Editor.Caret;
                    var a = _field.Editor.Anchor;
                    return new TextSelection(a.Paragraph == c.Paragraph ? a.Offset : c.Offset, c.Offset);
                }
                set
                {
                    _field.Editor.Anchor = new TextPosition(_field.Editor.Caret.Paragraph, value.Start);
                    _field.Editor.SetCaret(new TextPosition(_field.Editor.Caret.Paragraph, value.End), true);
                }
            }

            public override void SetPreeditText(string? preeditText) => _field.SetPreedit(preeditText, null);
            public override void SetPreeditText(string? preeditText, int? cursorPos) => _field.SetPreedit(preeditText, cursorPos);

            public void NotifyCaret()
            {
                RaiseCursorRectangleChanged();
                RaiseSelectionChanged();
                RaiseSurroundingTextChanged();
            }
        }

        // ------------------------------------------------ automation

        protected override AutomationPeer OnCreateAutomationPeer() => _peer = new FieldPeer(this);

        /// <summary>The text a screen reader reads: paragraphs joined by CR LF, each embedded object as U+FFFC.</summary>
        internal string AutomationValue => string.Join("\r\n", Editor.Document.Paragraphs.Select(p => p.Text));

        FieldPeer? _peer;

        sealed class FieldPeer : ControlAutomationPeer, global::Avalonia.Automation.Provider.IValueProvider, global::Avalonia.Automation.Provider.IExpandCollapseProvider
        {
            readonly RichTextField _field;
            string _lastValue;

            public FieldPeer(RichTextField field) : base(field) { _field = field; _lastValue = field.AutomationValue; }

            // True when the whole field is read-only (a field-level flag, ADR 0019); read-only runs inside an editable field do not count.
            public bool IsReadOnly => _field.IsReadOnly;
            public string? Value => _field.AutomationValue;
            public void SetValue(string? value) { }

            public void RaiseReadOnlyChanged(bool old, bool now) =>
                RaisePropertyChangedEvent(global::Avalonia.Automation.ValuePatternIdentifiers.IsReadOnlyProperty, old, now);

            public void RaiseValueChanged()
            {
                var now = _field.AutomationValue;
                if (now == _lastValue) return;
                var old = _lastValue;
                _lastValue = now;
                RaisePropertyChangedEvent(global::Avalonia.Automation.ValuePatternIdentifiers.ValueProperty, old, now);
            }

            /// <summary>The worst diagnostic's message, and whether the completion popup is open (ADR 0030): the squiggle and the popup have no other non-visual form.</summary>
            protected override string? GetItemStatusCore() => _field.AutomationStatus;

            public ExpandCollapseState ExpandCollapseState => _field.IsCompletionOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
            public bool ShowsMenu => false;
            public void Expand() { if (!_field.IsCompletionOpen) _ = _field.ShowCompletionAsync(); }
            public void Collapse() => _field.CloseCompletion();

            public void RaiseExpandCollapseChanged(bool open) =>
                RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, open ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded, open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
            protected override bool IsContentElementCore() => true;
            protected override bool IsControlElementCore() => true;
            protected override bool IsKeyboardFocusableCore() => true;
            // Avalonia raises the focus-changed event itself for a focusable control; there is no public API to raise it.
            protected override bool HasKeyboardFocusCore() => _field.IsFocused;
        }

        void RaiseAutomationValueChanged() => _peer?.RaiseValueChanged();

        // ------------------------------------------------ system colours

        /// <summary>The selection fill: in a high contrast theme the system highlight colour, else the theme or system highlight resource, else light sky blue.</summary>
        public IBrush ResolveSelectionBrush() => SystemBrush(SystemColour.Highlight) ??
            ThemeBrush(Brushes.LightSkyBlue, "TextControlSelectionHighlightColor", "SystemControlHighlightAccentBrush", "SystemColorHighlightColor");

        /// <summary>The colour of selected text in a high contrast theme (the system's highlight text colour), or null when the theme decides: the text keeps its own.</summary>
        Color? SelectedTextColour() => SystemColours.IsHighContrast() ? SystemColours.Get(SystemColour.HighlightText) : null;

        /// <summary>The caret fill and default text colour: in a high contrast theme the system window text colour, else the theme or system text-colour resource, else black.</summary>
        public IBrush ResolveCaretBrush() => SystemBrush(SystemColour.WindowText) ??
            ThemeBrush(Brushes.Black, "TextControlForeground", "SystemControlForegroundBaseHighBrush", "SystemColorWindowTextColor");

        /// <summary>The page fill: in a high contrast theme the system window colour, else the theme or system window-colour resource, else white.</summary>
        public IBrush ResolveBackgroundBrush() => SystemBrush(SystemColour.Window) ??
            ThemeBrush(Brushes.White, "SystemControlBackgroundAltHighBrush", "SystemAltHighColor", "SystemColorWindowColor");

        /// <summary>The system's colour of a kind as a brush while the system is in a high contrast theme, else null (Avalonia's themes have no high contrast
        /// variant: they would give black on white, where FieldWorks draws in the system's window text and window colours).</summary>
        static IBrush? SystemBrush(SystemColour kind) =>
            SystemColours.IsHighContrast() && SystemColours.Get(kind) is Color color ? new SolidColorBrush(color) : null;

        /// <summary>The first of the named theme or system resources that is a colour or brush; else the fallback look.</summary>
        IBrush ThemeBrush(IBrush fallback, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!this.TryFindResource(key, ActualThemeVariant, out var value)) continue;
                if (value is IBrush brush) return brush;
                if (value is Color color) return new SolidColorBrush(color);
            }
            return fallback;
        }
    }
}
