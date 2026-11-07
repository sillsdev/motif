using System.Collections.Generic;
using Avalonia.Media;

namespace SIL.TextEditing.Avalonia
{
    // Find and replace on the field (ADR 0021, ADR 0025). The search, the selection and the replacement live in the UI-free
    // DocumentEditor (TryFind, FindAll, ReplaceMatch, ReplaceCurrent, ReplaceAll, all on Editor); this part selects and
    // highlights what the host's dialog found and asks the host to scroll it into view.
    public partial class RichTextField
    {
        FindOptions? _highlightOptions;
        bool _highlightAll;
        TextRange? _currentMatch;
        bool _highlightTimedOut;
        static readonly System.TimeSpan HighlightBudget = System.TimeSpan.FromMilliseconds(50);
        Dictionary<int, IReadOnlyList<TextRange>>? _highlightCache;

        /// <summary>The fill of every match when all matches are highlighted (<see cref="SetFindHighlight"/>).</summary>
        public IBrush FindHighlightBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xE0, 0x40));

        /// <summary>The fill of the current match: the one <see cref="Find(FindOptions, bool, bool)"/> or
        /// <see cref="SelectMatch"/> chose last. It is drawn under the selection, so it stays visible after the selection moves.</summary>
        public IBrush CurrentMatchBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x9A, 0x1F));

        /// <summary>The match the host's dialog is on, or null. It is forgotten when the document changes (a replacement, typing,
        /// an undo), because its range may no longer mean anything.</summary>
        public TextRange? CurrentMatch
        {
            get
            {
                SyncFindState();
                return _currentMatch;
            }
        }

        /// <summary>The value of <c>_docVersion</c> the current match and the highlight cache were worked out at.</summary>
        int _findVersion;

        /// <summary>Drops the current match and the highlight cache when a paragraph changed since they were worked out, even if the field has not
        /// measured since (a second host edit before the next measure is not told apart from the first by the measure guard).</summary>
        void SyncFindState()
        {
            int version = System.Threading.Volatile.Read(ref _docVersion);
            if (version == _findVersion) return;
            _findVersion = version;
            FindDocumentChanged();
        }

        /// <summary>Finds the next (or previous) match from the selection, selects it, makes it the current match and asks the host to
        /// scroll it into view (<see cref="ScrollRequested"/>). False when there is none.</summary>
        public bool Find(FindOptions options, bool forward = true, bool wrap = false) => Find(options, forward, wrap, out _);

        /// <summary>As <see cref="Find(FindOptions, bool, bool)"/>, saying whether the search wrapped round the end of the field, so the
        /// host can show FieldWorks' wrap message.</summary>
        public bool Find(FindOptions options, bool forward, bool wrap, out bool wrapped)
        {
            if (!Editor.TryFind(options, forward, wrap, out var match, out wrapped)) return false;
            SelectMatch(match);
            return true;
        }

        /// <summary>Selects exactly the range (even in read-only text), makes it the current match and asks the host to scroll it into
        /// view.</summary>
        public void SelectMatch(TextRange match)
        {
            _moving = true;
            try { Editor.SelectRange(match); }
            finally { _moving = false; }
            _goalX = -1;
            _hit = null;
            SyncFindState();
            _currentMatch = match;
            InvalidateVisual();
            RequestRangeIntoView(match);
        }

        /// <summary>Highlights every match of the options (all = true) as the host's dialog is open, or none (null) when it closes. The
        /// current match is highlighted whatever this says. The matches are looked for only in the paragraphs drawn, and again after
        /// every change to the document.</summary>
        public void SetFindHighlight(FindOptions? options, bool all = true)
        {
            _highlightOptions = options?.Clone();
            // The paint gets a short budget (50 ms at most), so typing a slow pattern into a find box stalls it briefly; an explicit Find keeps the options' own.
            if (_highlightOptions != null && _highlightOptions.RegexTimeout > HighlightBudget) _highlightOptions.RegexTimeout = HighlightBudget;
            _highlightAll = all && options != null;
            _highlightCache = null;
            _highlightTimedOut = false;
            InvalidateVisual();
        }

        /// <summary>True when the highlight of all matches was given up because a regular expression ran out of time
        /// (<see cref="FindOptions.RegexTimeout"/>): nothing but the current match is drawn until the options change, and the paint
        /// never runs the expression again.</summary>
        public bool FindHighlightTimedOut => _highlightTimedOut;

        /// <summary>Forgets the current match and every highlight.</summary>
        public void ClearFindHighlight()
        {
            _currentMatch = null;
            SetFindHighlight(null);
        }

        void FindDocumentChanged()
        {
            _currentMatch = null;
            _highlightCache = null;
        }

        void DrawFindHighlights(DrawingContext context, ParagraphLayout layout, int index, double y)
        {
            SyncFindState();
            if (!_highlightAll && _currentMatch == null) return;
            if (_highlightAll && _highlightOptions != null && !_highlightTimedOut)
            {
                _highlightCache ??= new Dictionary<int, IReadOnlyList<TextRange>>();
                if (!_highlightCache.TryGetValue(index, out var matches))
                {
                    _highlightCache[index] = matches = Editor.FindAllIn(index, _highlightOptions, out bool timedOut);
                    if (timedOut) { _highlightTimedOut = true; _highlightCache = null; return; }
                }
                foreach (var m in matches) FillRange(context, layout, m, y, FindHighlightBrush);
            }
            if (_currentMatch is TextRange current && current.Start.Paragraph == index) FillRange(context, layout, current, y, CurrentMatchBrush);
        }

        static void FillRange(DrawingContext context, ParagraphLayout layout, TextRange range, double y, IBrush brush)
        {
            foreach (var r in layout.SelectionRects(range.Start.Offset, range.End.Offset))
                context.DrawRectangle(brush, null, new global::Avalonia.Rect(r.X, r.Y + y, r.Width, r.Height));
        }
    }
}
