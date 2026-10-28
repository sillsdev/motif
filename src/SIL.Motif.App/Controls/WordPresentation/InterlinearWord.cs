using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>An experimental drawn representation of a word and its compact interlinear readings.</summary>
internal sealed class InterlinearWord : Control
{
    private const double Gap = 4;
    private readonly List<Run> _runs = [];
    private ResultsTokenViewModel? _data;
    private TextStyles? _styles;
    private ModelKey? _modelKey;
    private Size _modelSize;
    private long _dataRevision;
    private double _width;
    private int _selected = -1;
    private int _anchor;
    private int _active;
    private bool _dragging;

    internal ResultsTokenViewModel? Data
    {
        get => _data;
        set { _data = value; _selected = -1; RefreshData(); }
    }

    internal void RefreshData()
    {
        _dataRevision++;
        InvalidateMeasure();
    }

    internal RunningTextLineMetrics LineMetrics { get; private set; }
    internal IReadOnlyList<Run> Runs => _runs;
    internal int SelectedRunIndex => _selected;
    internal int HitRun(Point point) => _runs.FindIndex(run => run.Bounds.Contains(point));
    internal void SelectRange(int index, int start, int end)
    {
        _selected = index;
        _anchor = Math.Clamp(start, 0, _runs[index].Text.Length);
        _active = Math.Clamp(end, 0, _runs[index].Text.Length);
        Focus();
        InvalidateVisual();
    }

    public InterlinearWord()
    {
        Focusable = true;
        IsTabStop = true;
        FlowDirection = FlowDirection.LeftToRight;
    }

    static InterlinearWord()
    {
        TextElement.ForegroundProperty.Changed.AddClassHandler<InterlinearWord>((word, _) => word.InvalidateMeasure());
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var styles = WritingSystemText.GetResolver(this);
        if (!ReferenceEquals(styles, _styles))
        {
            if (_styles is not null) _styles.ContextChanged -= OnStylesChanged;
            _styles = styles;
            if (_styles is not null) _styles.ContextChanged += OnStylesChanged;
        }
        if (_data is null || styles is null)
        {
            _runs.Clear();
            _modelKey = null;
            return default;
        }
        var foreground = TextElement.GetForeground(this) ?? Brushes.Black;
        var key = new ModelKey(styles, styles.Revision, _dataRevision, availableSize.Width, foreground);
        if (_modelKey == key) return _modelSize;
        _runs.Clear();
        var word = Shape(_data.Text, _data.TextWritingSystem, styles, foreground);
        var rtl = styles.Resolve(TextStyleRequest.Linguistic(_data.TextWritingSystem, "Normal")).FlowDirection == FlowDirection.RightToLeft;
        var widthLimit = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width) : double.PositiveInfinity;
        var field = ShapeMorphs(_data.PrimaryFieldWorksMorphs, styles, foreground);
        var pan = _data.ShowsPanGlossReading ? ShapeMorphs(_data.PanGlossReadingMorphs, styles, foreground) : [];
        var wordWidth = word.WidthIncludingTrailingWhitespace;
        var fieldWidth = ColumnsWidth(field);
        var panWidth = ColumnsWidth(pan);
        _width = Math.Min(widthLimit, Math.Max(wordWidth, Math.Max(fieldWidth, panWidth)));
        if (!double.IsFinite(_width)) _width = Math.Max(wordWidth, Math.Max(fieldWidth, panWidth));
        var y = word.Height;
        _runs.Add(new Run(_data.Text, word, new Rect(rtl ? _width - wordWidth : 0, 0, wordWidth, word.Height),
            rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight));
        var fieldHeight = Place(field, y, _width, rtl);
        y += fieldHeight;
        var panHeight = Place(pan, y, _width, rtl);
        y += panHeight;
        LineMetrics = new RunningTextLineMetrics(word.Height, fieldHeight, panHeight);
        if (_selected >= _runs.Count) _selected = -1;
        if (_selected >= 0)
        {
            _anchor = Math.Clamp(_anchor, 0, _runs[_selected].Text.Length);
            _active = Math.Clamp(_active, 0, _runs[_selected].Text.Length);
        }
        (ControlAutomationPeer.FromElement(this) as WordPeer)?.Refresh();
        _modelKey = key;
        _modelSize = new Size(_width, y);
        return _modelSize;
    }

    private static double ColumnsWidth(List<Column> columns) => columns.Sum(c => c.Width) + Math.Max(0, columns.Count - 1) * Gap;

    private double Place(List<Column> columns, double top, double width, bool rtl)
    {
        if (columns.Count == 0) return 0;
        List<List<Column>> groups = [[]];
        var used = 0d;
        foreach (var column in columns)
        {
            if (groups[^1].Count > 0 && used + Gap + column.Width > width)
            {
                groups.Add([]);
                used = 0;
            }
            if (groups[^1].Count > 0) used += Gap;
            groups[^1].Add(column);
            used += column.Width;
        }
        var y = top;
        foreach (var group in groups)
        {
            var formHeight = group.Max(c => c.Form.Height);
            var glossHeight = group.Max(c => c.Gloss.Height);
            var x = rtl ? width : 0;
            foreach (var column in group)
            {
                if (rtl) x -= column.Width;
                _runs.Add(new Run(column.FormText, column.Form, new Rect(x, y, column.Form.WidthIncludingTrailingWhitespace, formHeight), column.FormDirection));
                _runs.Add(new Run(column.GlossText, column.Gloss, new Rect(x, y + formHeight, column.Gloss.WidthIncludingTrailingWhitespace, glossHeight), column.GlossDirection));
                x += rtl ? -Gap : column.Width + Gap;
            }
            y += formHeight + glossHeight;
        }
        return y - top;
    }

    private static List<Column> ShapeMorphs(IReadOnlyList<ParserReadingMorphViewModel> morphs, TextStyles styles, IBrush foreground) =>
        morphs.Select(m => new Column(m.Form, m.GlossOrPlaceholder,
            Shape(m.Form, m.FormWritingSystem, styles, foreground),
            Shape(m.GlossOrPlaceholder, m.GlossWritingSystem, styles, foreground),
            styles.Resolve(TextStyleRequest.Linguistic(m.FormWritingSystem, "Normal")).FlowDirection,
            styles.Resolve(TextStyleRequest.Linguistic(m.GlossWritingSystem, "Normal")).FlowDirection)).ToList();

    private static TextLayout Shape(string text, string? writingSystem, TextStyles styles, IBrush foreground)
    {
        var style = styles.Resolve(TextStyleRequest.Linguistic(writingSystem, "Normal"));
        return new TextLayout(text, new Typeface(style.FontFamily ?? FontFamily.Default,
            style.Emphasis ?? FontStyle.Normal, style.Weight ?? FontWeight.Normal), style.FontSize ?? 14,
            foreground, flowDirection: style.FlowDirection, fontFeatures: style.FontFeatures);
    }

    public override void Render(DrawingContext context)
    {
        for (var i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            if (i == _selected && _anchor != _active &&
                this.TryFindResource("Intent.Selected.Fill", out var resource) && resource is IBrush selection)
            {
                var start = Math.Min(_anchor, _active);
                var end = Math.Max(_anchor, _active);
                foreach (var bounds in run.Layout.TextLines[0].GetTextBounds(start, end - start))
                    context.FillRectangle(selection, bounds.Rectangle.Translate(run.Bounds.Position));
            }
            run.Layout.Draw(context, run.Bounds.Position);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        var index = HitRun(point);
        if (index < 0) return;
        _selected = index;
        _anchor = _active = CharacterAt(_runs[index], point);
        _dragging = true;
        Focus();
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _selected < 0) return;
        _active = CharacterAt(_runs[_selected], e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        if (_anchor != _active) e.Handled = true;
    }

    [TextEditingKeyHandler]
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_selected < 0) return;
        if (e.Key == Key.Tab)
        {
            var next = _selected + ((e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1);
            if (next >= 0 && next < _runs.Count)
            {
                _selected = next;
                _anchor = _active = 0;
                InvalidateVisual();
                e.Handled = true;
            }
            return;
        }
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0 && e.Key is Key.Left or Key.Right)
        {
            var line = _runs[_selected].Layout.TextLines[0];
            var hit = new CharacterHit(_active, 0);
            var forward = (e.Key == Key.Right) == (_runs[_selected].Direction == FlowDirection.LeftToRight);
            var next = forward ? line.GetNextCaretCharacterHit(hit) : line.GetPreviousCaretCharacterHit(hit);
            _active = Math.Clamp(next.FirstCharacterIndex + next.TrailingLength, 0, _runs[_selected].Text.Length);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if ((e.KeyModifiers & primary) == 0) return;
        if (e.Key == Key.A)
        {
            _anchor = 0;
            _active = _runs[_selected].Text.Length;
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.C && _active != _anchor && TopLevel.GetTopLevel(this) is { } top)
        {
            var text = _runs[_selected].Text;
            _ = new AvaloniaClipboard(top).SetTextAsync(text[Math.Min(_anchor, _active)..Math.Max(_anchor, _active)]);
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_runs.Count == 0) return;
        if (e.NavigationMethod == NavigationMethod.Tab)
            _selected = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? _runs.Count - 1 : 0;
        else if (_selected < 0) _selected = 0;
    }

    private static int CharacterAt(Run run, Point point)
    {
        var hit = run.Layout.TextLines[0].GetCharacterHitFromDistance(point.X - run.Bounds.X);
        return Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, run.Text.Length);
    }

    private void OnStylesChanged(object? sender, EventArgs e) => InvalidateMeasure();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_styles is not null) _styles.ContextChanged -= OnStylesChanged;
        _styles = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new WordPeer(this);

    private sealed class WordPeer(InterlinearWord owner) : ControlAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
            owner._runs.Select((run, index) => (AutomationPeer)new ConstituentPeer(this, owner, run, index)).ToArray();

        internal void Refresh() => InvalidateChildren();
    }

    private sealed class ConstituentPeer(AutomationPeer parent, InterlinearWord owner, Run run, int index) : UnrealizedElementAutomationPeer
    {
        protected override string GetAcceleratorKeyCore() => string.Empty;
        protected override string GetAccessKeyCore() => string.Empty;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetAutomationIdCore() => string.Empty;
        protected override string GetClassNameCore() => "Text";
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override string GetNameCore() => run.Text;
        protected override AutomationPeer GetParentCore() => parent;
        protected override Rect GetBoundingRectangleCore()
        {
            if (TopLevel.GetTopLevel(owner) is not { } top ||
                owner.TranslatePoint(run.Bounds.TopLeft, top) is not { } start ||
                owner.TranslatePoint(run.Bounds.BottomRight, top) is not { } end) return default;
            return new Rect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        }
        protected override bool IsKeyboardFocusableCore() => true;
        protected override bool HasKeyboardFocusCore() => owner.IsFocused && owner._selected == index;
        protected override void SetFocusCore()
        {
            owner._selected = index;
            owner._anchor = owner._active = 0;
            owner.Focus();
            owner.InvalidateVisual();
        }
    }

    private readonly record struct ModelKey(TextStyles Styles, long StyleRevision, long DataRevision, double Width, IBrush Foreground);
    internal sealed record Run(string Text, TextLayout Layout, Rect Bounds, FlowDirection Direction);
    private sealed record Column(string FormText, string GlossText, TextLayout Form, TextLayout Gloss,
        FlowDirection FormDirection, FlowDirection GlossDirection)
    {
        public double Width => Math.Max(Form.WidthIncludingTrailingWhitespace, Gloss.WidthIncludingTrailingWhitespace);
    }
}
