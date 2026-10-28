using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using WritingSystemText = SIL.Motif.App.Controls.WritingSystemText;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>What a constituent of a word is, independent of the control that draws it.</summary>
internal enum Part { Word, FieldWorksForm, FieldWorksGloss, PanGlossForm, PanGlossGloss, CardForm, CardGloss, CardCategory }

/// <summary>One selectable piece of a word: its text, what it is, and where it sits in its word.</summary>
internal sealed record Constituent(Part Part, int Morph, TextBlock Visual)
{
    public string Text => Visual.Text ?? string.Empty;
}

internal sealed record SemanticConstituent(Part Part, int Morph, Control Owner, int RunIndex, string Text,
    FlowDirection Direction);

/// <summary>Records the typed requests a word makes of its list host.</summary>
internal sealed class RecordingHost : IWordPresentationHost
{
    public List<WordRequest> Requests { get; } = [];

    /// <summary>The evidence a card reads when it opens; none means the read fails.</summary>
    public WordCardDocument? Card { get; set; }

    public Task<WordCardReadResult> ReadCardAsync(WordPresentationKey key, long evidenceRevision, CancellationToken cancellationToken) =>
        Task.FromResult(Card is { } card
            ? WordCardReadResult.Read(card)
            : WordCardReadResult.Failed("No card in a parity scene.", canRetry: false));

    public ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return ValueTask.FromResult(new WordActionResult(true));
    }
}

/// <summary>
/// The one place that knows how today's text view is built. A test asks for "the constituents of word N" or "the
/// bounds of word N"; a redesign that draws a word as one control rewrites this file, not the tests.
/// </summary>
internal sealed class TextViewScene : IDisposable
{
    private const double Epsilon = 0.75;
    private readonly List<ResultsTokenViewModel> _tokens = [];
    private readonly TextStyles _styles = new();

    private TextViewScene(TextViewSample sample, MotifTestWindow window)
    {
        Sample = sample;
        Window = window;
    }

    public TextViewSample Sample { get; }

    public MotifTestWindow Window { get; }

    public RecordingHost Host { get; } = new();

    public Control Root => (Control)Window.Content!;

    /// <summary>Layout-warning messages seen since the scene opened.</summary>
    public IReadOnlyCollection<string> LayoutWarnings => _log?.Messages ?? [];

    private LayoutLog? _log;

    /// <summary>One word alone in a window, as the reader draws a word strip.</summary>
    public static TextViewScene SingleWord(TextViewSample sample, double width = 900) => Open(sample, 1, width, line: false);

    /// <summary>A reader line of <paramref name="words"/> words and its gutter, at <paramref name="width"/>.</summary>
    public static TextViewScene Line(TextViewSample sample, int words, double width) => Open(sample, words, width, line: true);

    public static TextViewScene ScrollableLine(TextViewSample sample, int words, double width) =>
        Open(sample, words, width, line: true, scroll: true);

    private static TextViewScene Open(TextViewSample sample, int words, double width, bool line, bool scroll = false)
    {
        WalkthroughFonts.Register();
        var window = new MotifTestWindow { Width = width, Height = 400 };
        var scene = new TextViewScene(sample, window);
        scene._log = new LayoutLog();
        scene.ConfigureStyles(window);
        for (var i = 0; i < words; i++) scene._tokens.Add(sample.Token(i));
        var body = line ? scene.BuildLine() : scene.BuildStrip(scene._tokens[0]);
        window.Content = scroll ? new ScrollViewer { Content = body } : body;
        window.Show();
        scene._openWidth = width;
        scene.Settle();
        return scene;
    }

    /// <summary>A word card showing its constituents as the card renders them, in its own window.</summary>
    public static TextViewScene Card(TextViewSample sample, double width = 700)
    {
        WalkthroughFonts.Register();
        var window = new MotifTestWindow { Width = width, Height = 600 };
        var scene = new TextViewScene(sample, window);
        scene._log = new LayoutLog();
        scene.ConfigureStyles(window);
        var token = sample.Token(0);
        scene._tokens.Add(token);
        var key = token.PresentationKey;
        var morphs = sample.StoredForms(0).Select(form =>
            new ParserReadingMorphViewModel(sample.Morph(form, parser: false))).ToArray();
        scene.Host.Card = new WordCardDocument(key, 0,
        [
            new WordCardHeading("Readings"),
            new WordCardMorphology("In FieldWorks", morphs),
            new WordCardAnalysis(null, token),
            new WordCardMorphology("One long form", [new ParserReadingMorphViewModel(
                new ParserReadingMorph(sample.LongForm, "long", "n", null, false, null)
                    { FormWritingSystem = sample.FormWritingSystem, GlossWritingSystem = sample.GlossWritingSystem })]),
        ]);
        window.Content = new WordCard { Host = scene.Host, Key = key, EvidenceRevision = 0 };
        window.Show();
        scene._openWidth = width;
        scene.Settle();
        return scene;
    }

    private void ConfigureStyles(Window window)
    {
        _styles.SetFallbackFamilies([WalkthroughFonts.DejaVuSansFamily]);
        WritingSystemDisplay Display(string id, bool rtl) => new(id, id, id, WritingSystemKind.Vernacular, 0, false,
            WalkthroughFonts.DejaVuSansFamily, string.Empty, rtl, new Dictionary<string, double> { ["Normal"] = 12 });
        _styles.ReplaceContext([Display(Sample.FormWritingSystem, Sample.RightToLeft), Display(Sample.GlossWritingSystem, false)], "en");
        WritingSystemText.SetResolver(window, _styles);
    }

    private WordStripToken NewStrip(ResultsTokenViewModel token) => new()
    {
        Data = token,
        DataContext = token,
        Host = Host,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private Control BuildStrip(ResultsTokenViewModel token)
    {
        var holder = new CountingHost { Child = NewStrip(token), HorizontalAlignment = HorizontalAlignment.Left };
        WritingSystemText.SetFlowId(holder, Sample.FormWritingSystem);
        WritingSystemText.SetFlowEnabled(holder, true);
        return holder;
    }

    private Control BuildLine()
    {
        var gutter = new StackPanel { Classes = { "gutter", "stripRows" } };
        foreach (var label in new[] { "Word", "FieldWorks", "PanGloss" })
            gutter.Children.Add(new TextBlock { Text = label, Classes = { "gutterLabel" } });
        var items = new ItemsControl
        {
            ItemsSource = _tokens,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsPanel = new FuncTemplate<Panel?>(() =>
            {
                var panel = new WordStripWrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch };
                WritingSystemText.SetFlowEnabled(panel, true);
                return panel;
            }),
            ItemTemplate = new FuncDataTemplate<ResultsTokenViewModel>((token, _) => NewStrip(token!)),
        };
        var body = new RunningTextPanel { HorizontalAlignment = HorizontalAlignment.Stretch, Children = { gutter, items } };
        WritingSystemText.SetFlowId(body, Sample.FormWritingSystem);
        WritingSystemText.SetFlowEnabled(body, true);
        return new CountingHost { Child = body, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    private double _openWidth;

    /// <summary>Changes the window's width and lets layout settle.</summary>
    public void Resize(double width)
    {
        if (Math.Abs(Window.Width - width) < 0.01) return;
        Window.Width = width;
        Settle();
    }

    /// <summary>Runs layout and posted jobs until nothing more is queued.</summary>
    public void Settle()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }
    }

    // ---- the seam: words -------------------------------------------------------------------------------------

    /// <summary>The reader's word views in the order the line holds them (reading order, not screen order).</summary>
    public IReadOnlyList<WordStripToken> Words() => [.. Window.GetVisualDescendants().OfType<WordStripToken>()];

    /// <summary>The gutter labels of a reader line (Word, FieldWorks, PanGloss).</summary>
    public IReadOnlyList<Control> GutterLabels() => [.. Window.GetVisualDescendants().OfType<StackPanel>()
        .First(panel => panel.Classes.Contains("gutter")).Children];

    /// <summary>The first word view.</summary>
    public WordStripToken Word(int index = 0) => Words()[index];

    public ResultsTokenViewModel TokenOf(int index) => _tokens[index];

    /// <summary>The word card in a card scene.</summary>
    public WordCard TheCard => Window.GetVisualDescendants().OfType<WordCard>().Single();

    /// <summary>Where a control sits in the window, as an axis-aligned box.</summary>
    public Rect Bounds(Visual visual) => LayoutAssertions.BoundsIn(visual, Window);

    public Rect WordBounds(int index) => Bounds(Word(index));

    /// <summary>Every selectable piece of word <paramref name="index"/>, in the order they are drawn into the word.</summary>
    public IReadOnlyList<Constituent> Constituents(int index) => ConstituentsOf(Word(index));

    public static IReadOnlyList<Constituent> ConstituentsOf(WordStripToken strip)
    {
        var result = new List<Constituent>();
        var seen = new Dictionary<Part, int>();
        foreach (var block in strip.GetVisualDescendants().OfType<CopyableTextBlock>())
        {
            var part = Classify(block);
            if (part is null) continue;
            var index = seen.GetValueOrDefault(part.Value);
            seen[part.Value] = index + 1;
            result.Add(new Constituent(part.Value, index, block));
        }
        return result;
    }

    /// <summary>The selectable pieces of the card's morphology, forms, glosses and categories.</summary>
    public IReadOnlyList<Constituent> CardConstituents()
    {
        var result = new List<Constituent>();
        var seen = new Dictionary<Part, int>();
        foreach (var block in TheCard.GetVisualDescendants().OfType<CopyableTextBlock>())
        {
            var part = Classify(block);
            if (part is null) continue;
            var index = seen.GetValueOrDefault(part.Value);
            seen[part.Value] = index + 1;
            result.Add(new Constituent(part.Value, index, block));
        }
        return result;
    }

    private static Part? Classify(CopyableTextBlock block)
    {
        if (block.Classes.Contains("stripWord")) return Part.Word;
        var panel = block.FindAncestorOfType<MorphemePanel>();
        if (panel is null) return null;
        var producer = panel.GetVisualAncestors().OfType<Border>().Select(row => row.Classes).Select(classes =>
            classes.Contains("stripFieldWorks") ? 1 : classes.Contains("panGlossLine") ? 2 : 0).FirstOrDefault(kind => kind != 0);
        if (block.Classes.Contains("morphemePanelForm"))
            return producer switch { 1 => Part.FieldWorksForm, 2 => Part.PanGlossForm, _ => Part.CardForm };
        if (block.Classes.Contains("morphemePanelGloss"))
            return producer switch { 1 => Part.FieldWorksGloss, 2 => Part.PanGlossGloss, _ => Part.CardGloss };
        return block.Classes.Contains("morphemePanelCategory") ? Part.CardCategory : null;
    }

    public Constituent Constituent(int word, Part part, int morph = 0) =>
        Constituents(word).Single(item => item.Part == part && item.Morph == morph);

    public IReadOnlyList<SemanticConstituent> SemanticConstituents(int index)
    {
        var strip = Word(index);
        if (strip.GetVisualDescendants().OfType<InterlinearWord>().FirstOrDefault() is not { } drawn)
            return Constituents(index).Select(c => new SemanticConstituent(c.Part, c.Morph, c.Visual, -1,
                c.Text, c.Visual.FlowDirection)).ToArray();
        var result = new List<SemanticConstituent>();
        void Add(Part part, int morph, int runIndex)
        {
            var run = drawn.Runs[runIndex];
            result.Add(new SemanticConstituent(part, morph, drawn, runIndex, run.Text, run.Direction));
        }
        Add(Part.Word, 0, 0);
        var runIndex = 1;
        for (var morph = 0; morph < TokenOf(index).PrimaryFieldWorksMorphs.Count; morph++)
        {
            Add(Part.FieldWorksForm, morph, runIndex++);
            Add(Part.FieldWorksGloss, morph, runIndex++);
        }
        if (TokenOf(index).ShowsPanGlossReading)
            for (var morph = 0; morph < TokenOf(index).PanGlossReadingMorphs.Count; morph++)
            {
                Add(Part.PanGlossForm, morph, runIndex++);
                Add(Part.PanGlossGloss, morph, runIndex++);
            }
        return result;
    }

    public Rect SemanticBounds(SemanticConstituent constituent)
    {
        if (constituent.Owner is not InterlinearWord drawn) return Bounds(constituent.Owner);
        var bounds = drawn.Runs[constituent.RunIndex].Bounds;
        var first = drawn.TranslatePoint(bounds.TopLeft, Window)!.Value;
        var last = drawn.TranslatePoint(bounds.BottomRight, Window)!.Value;
        return new Rect(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
            Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
    }

    public SemanticConstituent SemanticConstituent(int word, Part part, int morph = 0) =>
        SemanticConstituents(word).Single(item => item.Part == part && item.Morph == morph);

    public bool SemanticReaches(SemanticConstituent constituent)
    {
        var point = SemanticBounds(constituent).Center;
        var hit = VisualAt(point);
        if (constituent.Owner is InterlinearWord drawn)
            return ReferenceEquals(hit, drawn) && Window.TranslatePoint(point, drawn) is { } local &&
                drawn.HitRun(local) == constituent.RunIndex;
        return hit is not null && (ReferenceEquals(hit, constituent.Owner) || constituent.Owner.IsVisualAncestorOf(hit));
    }

    public AutomationPeer SemanticPeer(SemanticConstituent constituent) => constituent.Owner is InterlinearWord drawn
        ? PeerOf(drawn).GetChildren()[constituent.RunIndex] : PeerOf(constituent.Owner);

    public void FocusSemantic(SemanticConstituent constituent)
    {
        if (constituent.Owner is InterlinearWord) SemanticPeer(constituent).SetFocus();
        else
        {
            constituent.Owner.Focusable = true;
            Assert.True(constituent.Owner.Focus());
        }
    }

    public string? CopySemanticRange(SemanticConstituent constituent, int start, int end)
    {
        if (constituent.Owner is not InterlinearWord drawn)
            return CopyRangeByKeyboard(new Constituent(constituent.Part, constituent.Morph, (TextBlock)constituent.Owner), start, end);
        Window.Clipboard!.ClearAsync().GetAwaiter().GetResult();
        drawn.SelectRange(constituent.RunIndex, start, end);
        Press(Key.C, Primary);
        return Window.Clipboard.TryGetTextAsync().GetAwaiter().GetResult();
    }

    public string? CopySemanticDrag(SemanticConstituent constituent, double fraction) =>
        constituent.Owner is InterlinearWord ? CopySemanticByDrag(constituent, fraction) :
            CopyByDrag(new Constituent(constituent.Part, constituent.Morph, (TextBlock)constituent.Owner), fraction);

    public string? CopySemanticByDrag(SemanticConstituent constituent, double fraction)
    {
        Window.Clipboard!.ClearAsync().GetAwaiter().GetResult();
        Window.MouseMove(SemanticBounds(constituent).Center);
        Settle();
        var bounds = SemanticBounds(constituent);
        var rtl = constituent.Direction == FlowDirection.RightToLeft;
        var from = new Point(rtl ? bounds.Right - 1 : bounds.Left + 1, bounds.Center.Y);
        var to = new Point(rtl ? bounds.Right - 1 - (bounds.Width - 2) * fraction : bounds.Left + 1 + (bounds.Width - 2) * fraction, bounds.Center.Y);
        Window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Window.MouseUp(to, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Press(Key.C, Primary);
        return Window.Clipboard.TryGetTextAsync().GetAwaiter().GetResult();
    }

    public string? CopySemanticAll(SemanticConstituent constituent)
    {
        Window.Clipboard!.ClearAsync().GetAwaiter().GetResult();
        if (constituent.Owner is InterlinearWord drawn)
            PeerOf(drawn).GetChildren()[constituent.RunIndex].SetFocus();
        else FocusCleared(new Constituent(constituent.Part, constituent.Morph, (TextBlock)constituent.Owner));
        Press(Key.A, Primary);
        Press(Key.C, Primary);
        return Window.Clipboard.TryGetTextAsync().GetAwaiter().GetResult();
    }

    public string? CopySemanticKeyboardPrefix(SemanticConstituent constituent)
    {
        Window.Clipboard!.ClearAsync().GetAwaiter().GetResult();
        if (constituent.Owner is InterlinearWord drawn)
            PeerOf(drawn).GetChildren()[constituent.RunIndex].SetFocus();
        else Assert.True(constituent.Owner.Focus());
        Press(constituent.Direction == FlowDirection.RightToLeft ? Key.Left : Key.Right, RawInputModifiers.Shift);
        Press(Key.C, Primary);
        return Window.Clipboard.TryGetTextAsync().GetAwaiter().GetResult();
    }

    public Point Centre(Constituent constituent)
    {
        var bounds = Bounds(constituent.Visual);
        return bounds.Center;
    }

    /// <summary>What the pointer is over at a point in the window, from the deepest visual up.</summary>
    public Visual? VisualAt(Point point) => Window.GetVisualAt(point);

    // ---- the seam: actions and focus -------------------------------------------------------------------------

    /// <summary>The buttons, check boxes and links a word offers, in the order they are drawn.</summary>
    public static IReadOnlyList<Control> ActionsOf(Visual word) => [.. word.GetVisualDescendants()
        .OfType<Control>().Where(control => control is Button or CheckBox && control.IsEffectivelyVisible)];

    /// <summary>The disposition buttons a word carries now; at rest it has none, only the focused or hovered word does.</summary>
    public static IReadOnlyList<Control> DispositionButtonsOf(Visual word) => [.. ActionsOf(word)
        .Where(control => control.GetVisualAncestors().OfType<WordDispositionButtons>().Any())];

    /// <summary>The index of the word view that holds <paramref name="element"/>, or -1 outside every word.</summary>
    public int WordIndexOf(IInputElement? element)
    {
        if (element is not Visual visual) return -1;
        var owner = visual.GetSelfAndVisualAncestors().OfType<WordStripToken>().FirstOrDefault();
        return owner is null ? -1 : Words().ToList().IndexOf(owner);
    }

    /// <summary>Every Tab stop from the first word to the end of the last word, in order, starting with the first word.</summary>
    public List<IInputElement> TabThroughEveryWord() => TabFrom(Word(0), backwards: false, inside: e => WordIndexOf(e) >= 0);

    /// <summary>
    /// Focuses <paramref name="start"/> and presses Tab (or Shift+Tab) while focus stays <paramref name="inside"/>,
    /// returning the start and every stop reached, in order.
    /// </summary>
    public List<IInputElement> TabFrom(Control start, bool backwards, Func<IInputElement, bool> inside)
    {
        if (WordIndexOf(start) < 0 && WalkWords.Count > 0 && WalkWords[^1] is var last && last >= 0 &&
            Word(last).GetVisualDescendants().OfType<InterlinearWord>().Any())
        {
            Word(last).FocusWord();
            Settle();
        }
        Assert.True(start.Focus(), "The start of the tab walk cannot take focus.");
        var stops = new List<IInputElement> { start };
        var visited = new HashSet<(IInputElement Element, int Run)> { (start, start is InterlinearWord first ? first.SelectedRunIndex : -1) };
        WalkWords = [WordIndexOf(start)];
        WalkRuns = [start is InterlinearWord initial ? initial.SelectedRunIndex : -1];
        for (var press = 0; press < 400; press++)
        {
            var focused = Tab(backwards);
            if (focused is null || !inside(focused) ||
                !visited.Add((focused, focused is InterlinearWord drawn ? drawn.SelectedRunIndex : -1))) break;
            stops.Add(focused);
            WalkWords.Add(WordIndexOf(focused));
            WalkRuns.Add(focused is InterlinearWord word ? word.SelectedRunIndex : -1);
        }
        return stops;
    }

    /// <summary>The word each stop of the last tab walk was in, read when focus was there (a button may leave later).</summary>
    public List<int> WalkWords { get; private set; } = [];
    public List<int> WalkRuns { get; private set; } = [];

    public IReadOnlyList<(Control Owner, int Run)> SemanticStops(IReadOnlyList<IInputElement> stops, int word) =>
        stops.Select((stop, i) => (stop, i)).Where(x => WalkWords[x.i] == word &&
            (x.stop is InterlinearWord || x.stop is CopyableTextBlock))
            .Select(x => ((Control)x.stop, WalkRuns[x.i])).ToArray();

    public IInputElement? Focused => Window.FocusManager!.GetFocusedElement();

    /// <summary>Presses Tab (or Shift+Tab) and returns what then has focus.</summary>
    public IInputElement? Tab(bool backwards = false)
    {
        Press(Key.Tab, backwards ? RawInputModifiers.Shift : RawInputModifiers.None);
        Settle();
        return Focused;
    }

    public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    public static RawInputModifiers Primary => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    public void Click(Point point)
    {
        Window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        Window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    // ---- the seam: copy --------------------------------------------------------------------------------------

    /// <summary>Focuses a constituent, selects all of it from the keyboard, copies, and returns the clipboard text.</summary>
    public string? CopyAllByKeyboard(Constituent constituent)
    {
        FocusCleared(constituent);
        Press(Key.A, Primary);
        Press(Key.C, Primary);
        return Window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult();
    }

    /// <summary>Selects characters [start, end) of a constituent, copies from the keyboard, and returns the clipboard.</summary>
    public string? CopyRangeByKeyboard(Constituent constituent, int start, int end)
    {
        FocusCleared(constituent);
        var block = (SelectableTextBlock)constituent.Visual;
        block.SelectionStart = start;
        block.SelectionEnd = end;
        Press(Key.C, Primary);
        return Window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult();
    }

    /// <summary>Drags the pointer across the first <paramref name="fraction"/> of a constituent in reading direction.</summary>
    public string? CopyByDrag(Constituent constituent, double fraction)
    {
        FocusCleared(constituent);
        var bounds = Bounds(constituent.Visual);
        var rtl = constituent.Visual.FlowDirection == FlowDirection.RightToLeft;
        var y = bounds.Center.Y;
        var from = new Point(rtl ? bounds.Right - 1 : bounds.Left + 1, y);
        var to = new Point(rtl ? bounds.Right - 1 - (bounds.Width - 2) * fraction : bounds.Left + 1 + (bounds.Width - 2) * fraction, y);
        Window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Window.MouseUp(to, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Press(Key.C, Primary);
        return Window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult();
    }

    private void FocusCleared(Constituent constituent)
    {
        Window.Clipboard!.ClearAsync().GetAwaiter().GetResult();
        ((SelectableTextBlock)constituent.Visual).ClearSelection();
        constituent.Visual.Focusable = true;
        Assert.True(constituent.Visual.Focus(), $"'{constituent.Text}' cannot take focus.");
    }

    // ---- the seam: accessibility -----------------------------------------------------------------------------

    /// <summary>The automation peer of any control, as a screen reader would meet it.</summary>
    public static AutomationPeer PeerOf(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    /// <summary>The control a screen reader meets as word <paramref name="index"/>: the one carrying its automation id.</summary>
    public Control AccessibleWord(int index) => Word(index).GetSelfAndVisualDescendants().OfType<Control>()
        .First(control => AutomationProperties.GetAutomationId(control) == TokenOf(index).StripAutomationId);

    /// <summary>The names of every peer at or below a word, depth first.</summary>
    public static IReadOnlyList<string> PeerNames(Control control)
    {
        var names = new List<string>();
        void Walk(AutomationPeer peer)
        {
            names.Add(peer.GetName());
            foreach (var child in peer.GetChildren()) Walk(child);
        }
        Walk(PeerOf(control));
        return names;
    }

    // ---- the seam: layout ------------------------------------------------------------------------------------

    public CountingHost? Counter => Window.Content is ScrollViewer scroll ? scroll.Content as CountingHost : Window.Content as CountingHost;

    public IEnumerable<Visual> Everything() => Window.GetVisualDescendants();

    /// <summary>Puts the scene back as it opened: no selection, no focus, no recorded requests, width restored.</summary>
    public void Reset()
    {
        Host.Requests.Clear();
        Resize(_openWidth);
        Window.Focus();
        Window.MouseMove(new Point(Window.Width - 1, Window.Height - 1));
        foreach (var block in Window.GetVisualDescendants().OfType<SelectableTextBlock>()) block.ClearSelection();
        Settle();
    }

    public void Dispose()
    {
        _log?.Dispose();
        foreach (var token in _tokens) token.Dispose();
        Window.Close();
    }

    /// <summary>Counts how often layout reaches the root of the control under test.</summary>
    internal sealed class CountingHost : Decorator
    {
        public int Measures { get; private set; }

        public int Arranges { get; private set; }

        public long MeasureTicks { get; private set; }

        public long ArrangeTicks { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            Measures++;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return base.MeasureOverride(availableSize); }
            finally { MeasureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start; }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Arranges++;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return base.ArrangeOverride(finalSize); }
            finally { ArrangeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start; }
        }
    }

    private sealed class LayoutLog : IDisposable
    {
        private readonly ILogSink? _previous = Logger.Sink;
        private readonly Sink _sink;

        public LayoutLog()
        {
            _sink = new Sink(_previous);
            Logger.Sink = _sink;
        }

        public IReadOnlyCollection<string> Messages => _sink.Messages;

        public void Dispose() => Logger.Sink = _previous;

        private sealed class Sink(ILogSink? previous) : ILogSink
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

            public IReadOnlyCollection<string> Messages => _messages.ToArray();

            public bool IsEnabled(LogEventLevel level, string area) =>
                area == LogArea.Layout && level >= LogEventLevel.Warning || previous?.IsEnabled(level, area) == true;

            public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
                Record(level, area, messageTemplate);

            public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
                params object?[] propertyValues) => Record(level, area, messageTemplate);

            private void Record(LogEventLevel level, string area, string template)
            {
                if (area == LogArea.Layout && level >= LogEventLevel.Warning) _messages.Enqueue(template);
            }
        }
    }
}
