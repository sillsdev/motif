using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using Run = Avalonia.Controls.Documents.Run;

namespace SIL.Motif.App.Compare;

/// <summary>How a compare ended.</summary>
public enum CompareEnd
{
    /// <summary>The result is to replace the person's pending edit.</summary>
    Done,

    /// <summary>Nothing changes; the edit still no longer fits.</summary>
    DecideLater,

    /// <summary>The person's edit is to be thrown away for the FieldWorks text.</summary>
    StartAgain,
}

/// <summary>How a compare ended and, after Done, the text the person chose.</summary>
public sealed record CompareOutcome(CompareEnd End, IReadOnlyList<ParagraphVersion> Result);

/// <summary>
/// The popup that compares the person's version of a field with the FieldWorks version. It opens on the combined view:
/// one copy of the text with both sides' changes marked, the person's in SIL Blue with a solid underline, a single
/// strikethrough and a "You" tag, FieldWorks' in SIL Violet with a dashed underline, a double strikethrough and an "FW"
/// tag, beside a list of changes each with its switch. The combined view is read-only. The side-by-side view, on
/// request, shows FieldWorks read-only on the left and the person's version, editable, on the right as the result.
/// </summary>
public sealed partial class CompareWindow : Window
{
    /// <summary>SIL Blue: the person's changes.</summary>
    public static readonly IBrush MineBrush = new SolidColorBrush(Color.Parse("#164F91"));

    /// <summary>SIL Violet: the FieldWorks changes.</summary>
    public static readonly IBrush FieldWorksBrush = new SolidColorBrush(Color.Parse("#54278F"));

    private static readonly IBrush NeedsChoiceBrush = new SolidColorBrush(Color.Parse("#FFF4CE"));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#E8F0FB"));

    /// <summary>What the popup says under its title.</summary>
    public const string Explanation = "Both sets of changes are shown in one copy of the text. Changes made in one place only are " +
        "already in. Choose where both changed the same words. Nothing is saved to FieldWorks until you apply your changes.";

    private readonly TextComparison _comparison;
    private readonly WritingSystemRegistry? _writingSystems;
    private readonly IStyleResolver? _styles;
    private readonly string? _savedAt;
    private readonly Func<IReadOnlyList<ParagraphVersion>, string?>? _checkResult;
    // Focusable so Tab reaches the summary first, as the person's starting point.
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Focusable = true };
    private readonly CheckBox _showResult = new() { Content = ChangeWords.ShowResult };
    private readonly Button _openSideBySide = new() { Content = ChangeWords.OpenSideBySide };
    private readonly StackPanel _text = new() { Spacing = 10, Margin = new Thickness(4) };
    private readonly ScrollViewer _textScroll;
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly TextBlock _waiting = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _later = new() { Content = ChangeWords.DecideLater };
    private readonly Button _done = new() { Content = ChangeWords.Done, IsDefault = true };
    private readonly Grid _combined;
    private readonly List<Button> _marks = [];
    private readonly Dictionary<int, Border> _rows = [];
    private TextChange? _selected;

    /// <summary>
    /// Opens over <paramref name="comparison"/>. <paramref name="title"/> names the field and its record;
    /// <paramref name="writingSystems"/> and <paramref name="styles"/> lay the text out as the field does, and
    /// <paramref name="savedAt"/> says when FieldWorks saved its version, when known. <paramref name="checkResult"/> says why the
    /// field cannot hold a result, or null when it can; Done then stays in the window and shows that message.
    /// </summary>
    public CompareWindow(string title, TextComparison comparison, WritingSystemRegistry? writingSystems = null, IStyleResolver? styles = null,
        string? savedAt = null, Func<IReadOnlyList<ParagraphVersion>, string?>? checkResult = null)
    {
        _checkResult = checkResult;
        _comparison = comparison ?? throw new ArgumentNullException(nameof(comparison));
        _writingSystems = writingSystems;
        _styles = styles;
        _savedAt = savedAt;
        Title = "Compare two versions: " + title;
        Width = 1100;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _showResult.IsCheckedChanged += (_, _) => Render();
        _openSideBySide.Click += (_, _) => OpenSideBySide();
        _later.Click += (_, _) => DecideLater();
        _done.Click += (_, _) => Finish();
        var more = new Button { Content = "More" };
        AutomationProperties.SetName(more, "More choices");
        var mineForAll = new MenuItem { Header = ChangeWords.UseMineForAll };
        mineForAll.Click += (_, _) => _comparison.ChooseAll(Choice.Mine);
        var fieldWorksForAll = new MenuItem { Header = ChangeWords.UseFieldWorksForAll };
        fieldWorksForAll.Click += (_, _) => _comparison.ChooseAll(Choice.FieldWorks);
        var startAgain = new MenuItem { Header = ChangeWords.StartAgain };
        startAgain.Click += (_, _) => StartAgain();
        more.Flyout = new MenuFlyout { ItemsSource = new[] { mineForAll, fieldWorksForAll, startAgain } };

        var header = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = Title, FontWeight = FontWeight.Bold, FontSize = 16, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = Explanation, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(_summary);
        header.Children.Add(Legend());
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        tools.Children.Add(_showResult);
        tools.Children.Add(_openSideBySide);
        tools.Children.Add(more);
        header.Children.Add(tools);

        _textScroll = new ScrollViewer { Content = _text, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var listScroll = new ScrollViewer { Content = _list, Width = 340, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(listScroll, "Changes");
        // A grid rather than a dock panel so the children sit in Tab order: header, the marked text, then the list.
        _combined = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumnSpan(header, 2);
        _combined.Children.Add(header);
        var textBorder = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _textScroll };
        Grid.SetRow(textBorder, 1);
        _combined.Children.Add(textBorder);
        Grid.SetRow(listScroll, 1);
        Grid.SetColumn(listScroll, 1);
        _combined.Children.Add(listScroll);

        Content = Frame(_combined);
        _comparison.Changed += (_, _) => Refresh();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => FocusFirst();
        Render();
    }

    /// <summary>The comparison and its choices.</summary>
    public TextComparison Comparison => _comparison;

    /// <summary>How the compare ended, or null while it is open.</summary>
    public CompareOutcome? Outcome { get; private set; }

    /// <summary>The summary line: how many changes each side made and how many need a choice.</summary>
    public string SummaryText => _summary.Text ?? "";

    /// <summary>What the footer says while Done waits, or empty.</summary>
    public string WaitingText => _waiting.Text ?? "";

    /// <summary>Whether Done can be pressed: every change both made has a choice, or the side-by-side view is open.</summary>
    public bool IsDoneEnabled => _done.IsEnabled;

    /// <summary>Whether the clean result is shown instead of the marked text.</summary>
    public bool ShowResult
    {
        get => _showResult.IsChecked == true;
        set => _showResult.IsChecked = value;
    }

    /// <summary>The focusable marks in the combined text, one per change, in reading order.</summary>
    public IReadOnlyList<Button> Marks => _marks;

    /// <summary>The change selected in the text and the list, or null.</summary>
    public TextChange? Selected => _selected;

    /// <summary>
    /// What a screen reader reads for the text: the clean result first, then the summary, so inline edits never
    /// interrupt the reading.
    /// </summary>
    public string ReadableText => string.Join("\n", _comparison.Result().Select(paragraph => ChangeWords.Text(Tokens.Of(paragraph.Runs)))) +
                                  "\n" + SummaryText;

    /// <summary>The mark of <paramref name="change"/> in the combined text, or null when it is not shown.</summary>
    public Button? MarkOf(TextChange change) => _marks.FirstOrDefault(mark => mark.Tag == change);

    /// <summary>Selects <paramref name="change"/> in the text and the list and gives its mark the focus.</summary>
    public void Select(TextChange? change)
    {
        _selected = change;
        foreach (var (id, row) in _rows) row.Background = change?.Id == id ? SelectedBrush : Brushes.Transparent;
        foreach (var mark in _marks) mark.BorderBrush = mark.Tag == change ? Brushes.Black : MarkBorder((TextChange)mark.Tag!);
        if (change is not null && _rows.TryGetValue(change.Id, out var selectedRow)) selectedRow.BringIntoView();
        if (change is not null && MarkOf(change) is { } focused) focused.Focus(NavigationMethod.Directional);
    }

    /// <summary>Moves to the next change that needs a choice after the selected one, or the previous with <paramref name="back"/>.</summary>
    public bool NextUndecided(bool back = false)
    {
        var undecided = _comparison.Undecided;
        if (undecided.Count == 0) return false;
        var from = _selected?.Id ?? (back ? int.MaxValue : -1);
        var next = back ? undecided.LastOrDefault(change => change.Id < from) ?? undecided[^1]
            : undecided.FirstOrDefault(change => change.Id > from) ?? undecided[0];
        Select(next);
        return true;
    }

    /// <summary>
    /// Ends with the result of the choices, or of the right side in the side-by-side view; false while a choice waits or while
    /// the field cannot hold the result, which the footer then explains.
    /// </summary>
    public bool Finish()
    {
        if (IsSideBySide) TakeRightSide();
        if (!_comparison.IsDecided) return false;
        var result = _comparison.Result();
        if (_checkResult?.Invoke(result) is { } why)
        {
            _waiting.Text = why;
            return false;
        }
        Outcome = new CompareOutcome(CompareEnd.Done, result);
        Close(Outcome);
        return true;
    }

    /// <summary>Ends with nothing changed.</summary>
    public void DecideLater()
    {
        Outcome = new CompareOutcome(CompareEnd.DecideLater, []);
        Close(Outcome);
    }

    /// <summary>Ends asking for the person's edit to be thrown away for the FieldWorks text.</summary>
    public void StartAgain()
    {
        Outcome = new CompareOutcome(CompareEnd.StartAgain, []);
        Close(Outcome);
    }

    // The body is the frame's first child and the footer its last, so Tab ends on Decide later and Done.
    private Control Frame(Control body)
    {
        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footer.Children.Add(_waiting);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_later);
        buttons.Children.Add(_done);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        var frame = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("*,Auto") };
        frame.Children.Add(body);
        Grid.SetRow(footer, 1);
        frame.Children.Add(footer);
        return frame;
    }

    private static Control Legend()
    {
        var key = new WrapPanel { Orientation = Orientation.Horizontal };
        key.Children.Add(AuthorTag(ChangeWords.MineTag, MineBrush));
        key.Children.Add(new TextBlock { Text = "Yours: blue, solid underline for added words, single strikethrough for removed words", Margin = new Thickness(4, 0, 16, 0) });
        key.Children.Add(AuthorTag(ChangeWords.FieldWorksTag, FieldWorksBrush));
        key.Children.Add(new TextBlock { Text = "FieldWorks: violet, dashed underline, double strikethrough", Margin = new Thickness(4, 0, 16, 0) });
        key.Children.Add(new Border
        {
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Padding = new Thickness(3, 0),
            Child = new TextBlock { Text = "Formatting only: an outline plus words" },
        });
        return key;
    }

    /// <summary>A small "You" or "FW" tag in its author's colour.</summary>
    internal static Border AuthorTag(string text, IBrush brush) => new()
    {
        Background = brush,
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(3, 0),
        Margin = new Thickness(2, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeight.Bold },
    };

    private void Refresh()
    {
        if (IsSideBySide) UpdateFooter();
        else Render();
    }

    private void UpdateFooter()
    {
        _summary.Text = _comparison.Summary;
        AutomationProperties.SetName(_summary, _comparison.Summary);
        _waiting.Text = IsSideBySide ? "" : _comparison.Waiting ?? "";
        _done.IsEnabled = IsSideBySide || _comparison.IsDecided;
        AutomationProperties.SetName(_textScroll, ReadableText);
    }

    private void Render()
    {
        var focusedId = (FocusManager?.GetFocusedElement() as Control)?.Tag is TextChange focused ? focused.Id : (int?)null;
        _text.Children.Clear();
        _marks.Clear();
        foreach (var paragraph in _comparison.Sorted.Paragraphs) _text.Children.Add(ShowParagraph(paragraph));
        RenderList();
        UpdateFooter();
        if (_selected is not null) Select(_comparison.Sorted.Changes[_selected.Id]);
        if (focusedId is { } id && _marks.FirstOrDefault(mark => ((TextChange)mark.Tag!).Id == id) is { } again) again.Focus();
    }

    private Control ShowParagraph(SortedParagraph paragraph)
    {
        if (_comparison.RewrittenAt(paragraph.Index) is { } rewritten)
        {
            var edited = new StackPanel { Spacing = 4 };
            var note = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            note.Children.Add(AuthorTag(ChangeWords.MineTag, MineBrush));
            note.Children.Add(new TextBlock { Text = "Edited in the side-by-side view", VerticalAlignment = VerticalAlignment.Center });
            var reset = new Button { Content = "Use the compared text" };
            reset.Click += (_, _) => _comparison.Rewrite(paragraph.Index, null);
            note.Children.Add(reset);
            edited.Children.Add(note);
            foreach (var version in rewritten) edited.Children.Add(Plain(version));
            return edited;
        }
        if (ShowResult)
        {
            var result = _comparison.RewrittenAt(paragraph.Index) ?? (paragraph.Whole is { } whole &&
                (_comparison.ChoiceFor(whole) == Choice.FieldWorks ? paragraph.FieldWorks : paragraph.Mine) is null ? [] : [_comparison.Paragraph(paragraph)]);
            var clean = new StackPanel();
            foreach (var version in result) clean.Children.Add(Plain(version));
            return clean;
        }
        if (paragraph.Whole is { } change) return WholeMark(paragraph, change);
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FlowDirection = Direction(paragraph.Mine ?? paragraph.FieldWorks!) };
        foreach (var piece in paragraph.Pieces)
        {
            switch (piece)
            {
                case UnchangedPiece unchanged:
                    foreach (var run in unchanged.Tokens.SelectMany(token => token.Pieces)) block.Inlines!.Add(Styled(run));
                    break;
                case ChangePiece { Change: var inside }:
                    block.Inlines!.Add(new InlineUIContainer(Mark(inside)) { BaselineAlignment = BaselineAlignment.TextBottom });
                    break;
            }
        }
        if (paragraph.Format is not { } format) return block;
        var withFormat = new StackPanel { Spacing = 2 };
        withFormat.Children.Add(Mark(format));
        withFormat.Children.Add(block);
        return withFormat;
    }

    private Control WholeMark(SortedParagraph paragraph, TextChange change)
    {
        var mark = Mark(change);
        mark.HorizontalAlignment = HorizontalAlignment.Stretch;
        mark.HorizontalContentAlignment = HorizontalAlignment.Left;
        return mark;
    }

    private Control Plain(ParagraphVersion version)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FlowDirection = Direction(version) };
        foreach (var run in version.Runs) block.Inlines!.Add(Styled(run));
        return block;
    }

    private FlowDirection Direction(ParagraphVersion version) =>
        version.Props.Ints.TryGetValue(128, out var rtl) && rtl.Value != 0 ||
        version.Runs.Count > 0 && _writingSystems?.Contains(version.Runs[0].Ws) == true && _writingSystems.Get(version.Runs[0].Ws).Rtl
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private static IBrush MarkBorder(TextChange change) => change.Author == ChangeAuthor.Both ? Brushes.DarkGoldenrod
        : change.Kind is ChangeKind.Formatting or ChangeKind.WritingSystem or ChangeKind.ParagraphFormatting
            ? (change.Author == ChangeAuthor.Mine ? MineBrush : FieldWorksBrush) : Brushes.Transparent;

    // One focusable stop per change: its words in the author's style and tag, or the marker.
    private Button Mark(TextChange change)
    {
        var choice = _comparison.ChoiceFor(change);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var words = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (change.Author == ChangeAuthor.Both)
        {
            words.Inlines!.Add(new Run(ChangeWords.BothChanged) { FontWeight = FontWeight.SemiBold });
            if (choice is { } chosen)
            {
                words.Inlines.Add(new Run(": "));
                foreach (var run in TextComparison.Words(change, chosen).SelectMany(token => token.Pieces)) words.Inlines.Add(Styled(run));
            }
            content.Children.Add(words);
            content.Children.Add(AuthorTag(ChangeWords.MineTag, MineBrush));
            content.Children.Add(AuthorTag(ChangeWords.FieldWorksTag, FieldWorksBrush));
        }
        else
        {
            var mine = change.Author == ChangeAuthor.Mine;
            var inUse = choice == change.Initial;
            var brush = mine ? MineBrush : FieldWorksBrush;
            var after = mine ? change.Mine : change.FieldWorks;
            switch (change.Kind)
            {
                case ChangeKind.ParagraphFormatting:
                    words.Inlines!.Add(new Run(change.Description) { FontStyle = FontStyle.Italic });
                    break;
                case ChangeKind.Formatting or ChangeKind.WritingSystem:
                    foreach (var run in (inUse ? after : change.Starting).SelectMany(token => token.Pieces)) words.Inlines!.Add(Styled(run));
                    break;
                default:
                    var before = change.Kind == ChangeKind.ParagraphAdded ? [] : change.Kind == ChangeKind.ParagraphRemoved
                        ? (mine ? change.FieldWorks : change.Mine) : change.Starting;
                    if (change.Kind == ChangeKind.ParagraphRemoved) after = [];
                    if (!inUse)
                    {
                        foreach (var run in before.SelectMany(token => token.Pieces)) words.Inlines!.Add(Styled(run));
                        break;
                    }
                    foreach (var run in before.SelectMany(token => token.Pieces)) words.Inlines!.Add(Removed(Styled(run), mine, brush));
                    foreach (var run in after.SelectMany(token => token.Pieces)) words.Inlines!.Add(Added(Styled(run), mine, brush));
                    break;
            }
            content.Children.Add(words);
            content.Children.Add(AuthorTag(mine ? ChangeWords.MineTag : ChangeWords.FieldWorksTag, inUse ? brush : Brushes.Gray));
            if (!inUse) content.Children.Add(new TextBlock { Text = "not used", FontStyle = FontStyle.Italic, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center });
        }
        var mark = new Button
        {
            Tag = change,
            Content = content,
            Padding = new Thickness(1),
            Margin = new Thickness(0),
            MinHeight = 0,
            Background = change.Author == ChangeAuthor.Both && choice is null ? NeedsChoiceBrush : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = MarkBorder(change),
        };
        AutomationProperties.SetName(mark, ChangeWords.Announce(change, choice));
        if (change.Author == ChangeAuthor.Both) mark.Flyout = Popover(change);
        else mark.Click += (_, _) => Select(change);
        mark.GotFocus += (_, _) =>
        {
            if (_selected != change) SelectQuietly(change);
        };
        _marks.Add(mark);
        return mark;
    }

    private void SelectQuietly(TextChange change)
    {
        _selected = change;
        foreach (var (id, row) in _rows) row.Background = change.Id == id ? SelectedBrush : Brushes.Transparent;
    }

    private Flyout Popover(TextChange change)
    {
        var body = new StackPanel { Spacing = 6, MaxWidth = 420 };
        body.Children.Add(new TextBlock { Text = $"{ChangeWords.BothChanged}, {change.Location}", FontWeight = FontWeight.SemiBold });
        body.Children.Add(Version(ChangeWords.MineTag, MineBrush, change.Mine));
        body.Children.Add(Version(ChangeWords.FieldWorksTag, FieldWorksBrush, change.FieldWorks));
        var flyout = new Flyout { Content = body };
        var buttons = new WrapPanel();
        foreach (var (label, choice) in Choices(change))
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), IsEnabled = choice != Choice.Both || change.CanUseBoth };
            button.Click += (_, _) =>
            {
                _comparison.Choose(change, choice);
                flyout.Hide();
            };
            buttons.Children.Add(button);
        }
        var sideBySide = new Button { Content = ChangeWords.EditSideBySide, Margin = new Thickness(0, 0, 6, 6) };
        sideBySide.Click += (_, _) =>
        {
            flyout.Hide();
            OpenSideBySide();
        };
        buttons.Children.Add(sideBySide);
        body.Children.Add(buttons);
        if (change.WhyNotBoth is { } why)
            body.Children.Add(new TextBlock { Text = $"{ChangeWords.UseBoth} is not possible: {why}.", TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic });
        return flyout;
    }

    private Control Version(string tag, IBrush brush, IReadOnlyList<Token> tokens)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(AuthorTag(tag, brush));
        var words = new TextBlock { TextWrapping = TextWrapping.Wrap };
        foreach (var run in tokens.SelectMany(token => token.Pieces)) words.Inlines!.Add(Styled(run));
        if (tokens.Count == 0) words.Inlines!.Add(new Run("(nothing)") { FontStyle = FontStyle.Italic });
        line.Children.Add(words);
        return line;
    }

    private static IEnumerable<(string Label, Choice Choice)> Choices(TextChange change)
    {
        yield return (ChangeWords.UseMine, Choice.Mine);
        yield return (ChangeWords.UseFieldWorks, Choice.FieldWorks);
        if (change.Author == ChangeAuthor.Both && !change.IsWholeParagraph && change.Kind != ChangeKind.ParagraphFormatting)
            yield return (ChangeWords.UseBoth, Choice.Both);
    }

    private void RenderList()
    {
        _list.Children.Clear();
        _rows.Clear();
        foreach (var change in _comparison.Sorted.Changes)
        {
            var choice = _comparison.ChoiceFor(change);
            var who = change.Author switch
            {
                ChangeAuthor.Mine => "Yours",
                ChangeAuthor.FieldWorks => "FieldWorks",
                _ => ChangeWords.BothChanged,
            };
            var row = new StackPanel { Spacing = 4 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (change.Author != ChangeAuthor.FieldWorks) heading.Children.Add(AuthorTag(ChangeWords.MineTag, MineBrush));
            if (change.Author != ChangeAuthor.Mine) heading.Children.Add(AuthorTag(ChangeWords.FieldWorksTag, FieldWorksBrush));
            heading.Children.Add(new TextBlock { Text = $"{Capital(change.Location)} · {who}", FontWeight = FontWeight.SemiBold });
            row.Children.Add(heading);
            row.Children.Add(new TextBlock { Text = change.Description, TextWrapping = TextWrapping.Wrap });
            var switches = new WrapPanel();
            foreach (var (label, option) in Choices(change))
            {
                var toggle = new ToggleButton
                {
                    Content = label,
                    IsChecked = choice == option,
                    IsEnabled = option != Choice.Both || change.CanUseBoth,
                    Margin = new Thickness(0, 0, 4, 4),
                    Tag = change,
                };
                AutomationProperties.SetName(toggle, $"{label}, {change.Location}");
                toggle.Click += (_, _) => _comparison.Choose(change, option);
                switches.Children.Add(toggle);
            }
            // A disabled button shows no tooltip, so why Use both cannot apply is said in the row itself.
            if (change.Author == ChangeAuthor.Both && change.WhyNotBoth is { } why)
                row.Children.Add(new TextBlock { Text = $"{ChangeWords.UseBoth} is not possible: {why}.", TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic });
            if (change.Author == ChangeAuthor.Both)
            {
                var sideBySide = new Button { Content = ChangeWords.OpenSideBySide, Margin = new Thickness(0, 0, 4, 4), Tag = change };
                sideBySide.Click += (_, _) => OpenSideBySide();
                switches.Children.Add(sideBySide);
            }
            row.Children.Add(switches);
            var border = new Border { Child = row, Padding = new Thickness(6), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1), Tag = change };
            border.PointerPressed += (_, _) => Select(change);
            AutomationProperties.SetName(border, ChangeWords.Announce(change, choice));
            _rows[change.Id] = border;
            _list.Children.Add(border);
        }
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private Run Styled(TextRun run)
    {
        var styled = new Run(run.Text.Normalize(System.Text.NormalizationForm.FormC));
        var resolved = run.CharacterStyle is { } style ? _styles?.Resolve(style, run.Ws) : null;
        if (On(run.Props, TextPropertyNumbers.Italic) || resolved?.TryGetInt(TextPropertyNumbers.Italic, out var italic) == true && italic != 0)
            styled.FontStyle = FontStyle.Italic;
        if (On(run.Props, TextPropertyNumbers.Bold) || resolved?.TryGetInt(TextPropertyNumbers.Bold, out var bold) == true && bold != 0)
            styled.FontWeight = FontWeight.Bold;
        if (_writingSystems?.Contains(run.Ws) == true && _writingSystems.Get(run.Ws).Font is { Length: > 0 } font) styled.FontFamily = new FontFamily(font);
        return styled;
    }

    private static bool On(TextProps? props, int number) => props?.Ints.TryGetValue(number, out var value) == true && value.Value != 0;

    private static Run Added(Run run, bool mine, IBrush brush)
    {
        run.Foreground = brush;
        run.TextDecorations = [new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            Stroke = brush,
            StrokeThickness = 1.5,
            StrokeDashArray = mine ? null : new AvaloniaList<double> { 3, 2 },
        }];
        return run;
    }

    private static Run Removed(Run run, bool mine, IBrush brush)
    {
        run.Foreground = brush;
        var decorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough, Stroke = brush, StrokeThickness = 1 } };
        if (!mine)
        {
            decorations[0].StrokeOffset = -1.5;
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough, Stroke = brush, StrokeThickness = 1, StrokeOffset = 1.5 });
        }
        run.TextDecorations = decorations;
        return run;
    }

    private void FocusFirst()
    {
        var first = _comparison.Undecided.FirstOrDefault() ?? _comparison.Sorted.Changes.FirstOrDefault();
        if (first is not null) Select(first);
    }

    private TextChange? Current()
    {
        for (var at = FocusManager?.GetFocusedElement() as Visual; at is not null; at = at.GetVisualParent())
            if (at is Control { Tag: TextChange change }) return change;
        return _selected;
    }

    [KeyboardShortcutHandler("CompareVersions:UseMine", "CompareVersions:UseFieldWorks", "CompareVersions:UseBoth",
        "CompareVersions:EditSideBySide", "CompareVersions:NextUndecided", "CompareVersions:PreviousUndecided",
        "CompareVersions:OpenSideBySide", "CompareVersions:DecideLater", "CompareSideBySide:UseThis",
        "CompareSideBySide:SwitchSide", "CompareSideBySide:Back")]
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var scope = IsSideBySide ? KeyboardShortcutScope.CompareSideBySide : KeyboardShortcutScope.CompareVersions;
        if (KeyboardShortcutRegistry.Find(scope, e.Key, e.KeyModifiers) is not { } entry) return;
        var change = Current();
        if (!KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: change is not null))
            return;
        switch (entry.Behavior)
        {
            case KeyboardShortcutBehavior.UseMine:
                e.Handled = _comparison.Choose(change!, Choice.Mine);
                break;
            case KeyboardShortcutBehavior.UseFieldWorks:
                e.Handled = _comparison.Choose(change!, Choice.FieldWorks);
                break;
            case KeyboardShortcutBehavior.UseBoth:
                e.Handled = _comparison.Choose(change!, Choice.Both);
                break;
            case KeyboardShortcutBehavior.EditSideBySide when change!.Author == ChangeAuthor.Both:
                OpenSideBySide();
                e.Handled = true;
                break;
            case KeyboardShortcutBehavior.NextUndecided:
                e.Handled = NextUndecided();
                break;
            case KeyboardShortcutBehavior.PreviousUndecided:
                e.Handled = NextUndecided(back: true);
                break;
            case KeyboardShortcutBehavior.OpenSideBySide:
                OpenSideBySide();
                e.Handled = true;
                break;
            case KeyboardShortcutBehavior.DecideLater:
                DecideLater();
                e.Handled = true;
                break;
            case KeyboardShortcutBehavior.UseThis when CurrentRow() is { } row:
                UseThis(row);
                e.Handled = true;
                break;
            case KeyboardShortcutBehavior.SwitchSide:
                SwitchSide();
                e.Handled = true;
                break;
            case KeyboardShortcutBehavior.Back:
                BackToCombinedView();
                e.Handled = true;
                break;
        }
    }
}
