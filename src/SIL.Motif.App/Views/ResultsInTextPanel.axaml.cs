using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Analyze texts view, bound to its own view model.</summary>
public sealed partial class ResultsInTextPanel : UserControl
{
    private readonly List<Control> _visibilityAncestors = [];
    private readonly ItemsControl _lineItems;
    private readonly ScrollViewer _textScrollViewer;

    public ResultsInTextPanel(ResultsInTextViewModel inText)
    {
        ArgumentNullException.ThrowIfNull(inText);
        InText = inText;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        _lineItems = this.FindControl<ItemsControl>("TextLineItems")!;
        _textScrollViewer = this.FindControl<ScrollViewer>("TextScrollViewer")!;
        var readerToolbar = this.FindControl<Grid>("ReaderToolbar")!;
        var filterChips = this.FindControl<WrapPanel>("ResultsInTextFilterChips")!;
        readerToolbar.LayoutUpdated += (_, _) => SetFilterChipRow(readerToolbar, filterChips);
        SetFilterChipRow(readerToolbar, filterChips);
        InText.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ResultsInTextViewModel.SelectedToken) && InText.SelectedToken is { } token)
                Dispatcher.UIThread.Post(() => FocusCard(token), DispatcherPriority.Loaded);
            else if (e.PropertyName == nameof(ResultsInTextViewModel.SelectedText))
            {
                var selectedText = InText.SelectedText;
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(selectedText, InText.SelectedText)) _textScrollViewer.Offset = Vector.Zero;
                }, DispatcherPriority.Loaded);
            }
        };
    }

    public ResultsInTextViewModel InText { get; }

    private static void SetFilterChipRow(Grid toolbar, WrapPanel filterChips)
    {
        var inlineWidth = toolbar.ColumnDefinitions[2].ActualWidth;
        var requiredWidth = filterChips.Children.OfType<Control>().Where(chip => chip.IsVisible)
            .Sum(chip => chip.DesiredSize.Width);
        var row = inlineWidth > 0 && requiredWidth <= inlineWidth ? 0 : 1;
        var column = row == 1 ? 0 : 2;
        var columnSpan = row == 1 ? 5 : 1;
        if (Grid.GetRow(filterChips) == row && Grid.GetColumn(filterChips) == column &&
            Grid.GetColumnSpan(filterChips) == columnSpan) return;
        Grid.SetRow(filterChips, row);
        Grid.SetColumn(filterChips, column);
        Grid.SetColumnSpan(filterChips, columnSpan);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var ancestor in this.GetVisualAncestors().OfType<Control>())
        {
            _visibilityAncestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        InText.CloseTokenCard();
        foreach (var ancestor in _visibilityAncestors)
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        _visibilityAncestors.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnGoToTextsClick(object? sender, RoutedEventArgs e) => InText.OpenTexts?.Invoke();

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty && !IsEffectivelyVisible) InText.CloseTokenCard();
    }

    private async void OnTokenPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: ResultsTokenViewModel token } form &&
            form.FindAncestorOfType<Border>() is { } strip)
        {
            strip.Focus();
            await InText.OpenTokenCardAsync(token);
        }
    }

    [KeyboardShortcutHandler(
        "TextReader:CloseWordCard", "TextReader:PreviousWord", "TextReader:NextWord", "TextReader:PreviousLine", "TextReader:NextLine",
        "TextReader:OpenWordCard", "TextReader:PreviousScreen", "TextReader:NextScreen", "TextReader:FirstItem",
        "TextReader:LastItem", "TextReader:Approve", "TextReader:Disapprove", "TextReader:Unknown")]
    private async void OnTokenKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { Tag: ResultsTokenViewModel token } strip) return;
        if (KeyboardShortcutRegistry.IsTextInput(e.Source)) return;
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.CloseWordCard]) ??
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers);
        if (entry is null || !KeyboardShortcutRegistry.Allows(entry, isTextInput: false, hasFocusedItem: true)) return;
        switch (entry.Behavior)
        {
            case KeyboardShortcutBehavior.PreviousWord:
            case KeyboardShortcutBehavior.NextWord:
                e.Handled = FocusNeighbour(strip, entry.Behavior == KeyboardShortcutBehavior.PreviousWord ? -1 : 1);
                break;
            case KeyboardShortcutBehavior.PreviousLine:
            case KeyboardShortcutBehavior.NextLine:
                e.Handled = FocusLine(token, entry.Behavior == KeyboardShortcutBehavior.PreviousLine ? -1 : 1);
                break;
            case KeyboardShortcutBehavior.OpenWordCard:
                e.Handled = await OpenTokenCardOnKeyboardAsync(e.Key, token, InText.OpenTokenCardAsync);
                break;
            case KeyboardShortcutBehavior.PreviousScreen:
            case KeyboardShortcutBehavior.NextScreen:
                e.Handled = FocusLine(token, (entry.Behavior == KeyboardShortcutBehavior.PreviousScreen ? -1 : 1) * ScreenLineCount(token), clamp: true);
                break;
            case KeyboardShortcutBehavior.FirstItem:
            case KeyboardShortcutBehavior.LastItem:
                e.Handled = FocusBoundary(entry.Behavior == KeyboardShortcutBehavior.FirstItem ? 0 : InText.VisibleLines.Count - 1);
                break;
            case KeyboardShortcutBehavior.Approve:
            case KeyboardShortcutBehavior.Disapprove:
            case KeyboardShortcutBehavior.Unknown:
                e.Handled = true;
                await StageOpinionAsync(strip, token, entry.Behavior);
                break;
        }
    }

    private bool FocusNeighbour(Control strip, int direction)
    {
        if (strip.Tag is not ResultsTokenViewModel current) return false;
        var tokens = InText.VisibleLines.SelectMany(line => line.Tokens).Where(token => token.IsWord).ToList();
        var index = tokens.IndexOf(current);
        var next = index + direction;
        if (index < 0 || next < 0 || next >= tokens.Count) return false;
        BringTokenLineIntoView(tokens[next]);
        return StripFor(tokens[next])?.Focus(NavigationMethod.Directional) == true;
    }

    [KeyboardShortcutHandler(
        "TextReader:CloseWordCard", "TextReader:PreviousWord", "TextReader:NextWord", "TextReader:PreviousLine",
        "TextReader:NextLine", "TextReader:PreviousScreen", "TextReader:NextScreen", "TextReader:FirstItem",
        "TextReader:LastItem", "TextReader:Approve", "TextReader:Disapprove", "TextReader:Unknown")]
    private async void OnTokenCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control card) return;
        if (KeyboardShortcutRegistry.IsTextInput(e.Source)) return;
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.CloseWordCard]) ??
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers);
        if (entry is null || !KeyboardShortcutRegistry.Allows(entry, isTextInput: false, hasFocusedItem: true)) return;
        if (entry.Behavior == KeyboardShortcutBehavior.CloseWordCard)
        {
            CloseCardOntoItsWord();
            e.Handled = true;
            return;
        }
        if (e.Source is Control source && !ReferenceEquals(source, card)) return;
        var token = card.DataContext as ResultsTokenViewModel ?? InText.SelectedToken;
        switch (entry.Behavior)
        {
            case KeyboardShortcutBehavior.PreviousWord:
            case KeyboardShortcutBehavior.NextWord:
                e.Handled = true;
                await InText.MoveTokenCardAsync(entry.Behavior == KeyboardShortcutBehavior.PreviousWord ? -1 : 1).ConfigureAwait(true);
                break;
            case KeyboardShortcutBehavior.PreviousLine:
            case KeyboardShortcutBehavior.NextLine:
                e.Handled = token is not null && FocusLine(token, entry.Behavior == KeyboardShortcutBehavior.PreviousLine ? -1 : 1);
                break;
            case KeyboardShortcutBehavior.PreviousScreen:
            case KeyboardShortcutBehavior.NextScreen:
                e.Handled = token is not null && FocusLine(token,
                    (entry.Behavior == KeyboardShortcutBehavior.PreviousScreen ? -1 : 1) * ScreenLineCount(token), clamp: true);
                break;
            case KeyboardShortcutBehavior.FirstItem:
            case KeyboardShortcutBehavior.LastItem:
                e.Handled = FocusBoundary(entry.Behavior == KeyboardShortcutBehavior.FirstItem ? 0 : InText.VisibleLines.Count - 1);
                break;
            case KeyboardShortcutBehavior.Approve:
            case KeyboardShortcutBehavior.Disapprove:
            case KeyboardShortcutBehavior.Unknown:
                if (token is not null)
                {
                    e.Handled = true;
                    await StageOpinionAsync(card, token, entry.Behavior);
                }
                break;
        }
    }

    private bool FocusLine(ResultsTokenViewModel current, int offset, bool clamp = false)
    {
        var currentLine = InText.VisibleLines.FirstOrDefault(line => line.Tokens.Contains(current));
        var index = currentLine is null ? -1 : InText.VisibleLines.IndexOf(currentLine);
        var target = index + offset;
        if (index < 0) return false;
        if (clamp) target = Math.Clamp(target, 0, InText.VisibleLines.Count - 1);
        return target >= 0 && target < InText.VisibleLines.Count && FocusBoundary(target);
    }

    private bool FocusBoundary(int index)
    {
        if (InText.VisibleLines.Count == 0) return false;
        var targetIndex = Math.Clamp(index, 0, InText.VisibleLines.Count - 1);
        var line = InText.VisibleLines[targetIndex];
        var token = line.Tokens.FirstOrDefault(item => item.IsWord);
        if (token is null) return false;
        BringTokenLineIntoView(token);
        var strip = StripFor(token);
        if (strip is null) return false;
        return strip.Focus(NavigationMethod.Directional);
    }

    private int ScreenLineCount(ResultsTokenViewModel token)
    {
        var viewport = _lineItems.FindAncestorOfType<ScrollViewer>()?.Viewport.Height ?? _lineItems.Bounds.Height;
        var line = InText.VisibleLines.FirstOrDefault(candidate => candidate.Tokens.Contains(token));
        var lineIndex = line is null ? -1 : InText.VisibleLines.IndexOf(line);
        var realized = lineIndex < 0 ? 0 : _lineItems.ContainerFromIndex(lineIndex)?.Bounds.Height ?? 0;
        return Math.Max(1, (int)Math.Floor(viewport / Math.Max(1, realized)));
    }

    private async Task StageOpinionAsync(Control source, ResultsTokenViewModel token, KeyboardShortcutBehavior behavior)
    {
        if (token.Marking.FieldWorksAnalyses.Count != 1)
        {
            KeyboardShortcutStatus.Announce(source, token.Marking.FieldWorksAnalyses.Count == 0
                ? $"{token.Form} has no stored analysis to change."
                : $"{token.Form} has {token.Marking.FieldWorksAnalyses.Count} analyses. Choose one in Analyze texts.");
            return;
        }
        var kind = behavior switch
        {
            KeyboardShortcutBehavior.Approve => ChangeKinds.Approve,
            KeyboardShortcutBehavior.Disapprove => ChangeKinds.Reject,
            _ => ChangeKinds.Candidate,
        };
        var analysis = token.Marking.FieldWorksAnalyses[0];
        if (string.IsNullOrWhiteSpace(analysis.StoredAnalysisId))
        {
            KeyboardShortcutStatus.Announce(source, $"Motif cannot identify the stored analysis for {token.Form}.");
            return;
        }
        var choice = AnalysisMarkingState.OpinionChoicesFor(analysis)
            .FirstOrDefault(item => item.ChangeKind == kind);
        if (choice is null)
        {
            var requestedOpinion = behavior switch
            {
                KeyboardShortcutBehavior.Approve => ReadingGrade.Approved,
                KeyboardShortcutBehavior.Disapprove => ReadingGrade.Disapproved,
                _ => ReadingGrade.Candidate,
            };
            var currentOpinion = token.Marking.FieldWorksAnalyses[0].Opinion;
            var label = requestedOpinion switch
            {
                ReadingGrade.Approved => "Approved",
                ReadingGrade.Disapproved => "Disapproved",
                _ => "Unknown",
            };
            KeyboardShortcutStatus.Announce(source, currentOpinion == requestedOpinion
                ? $"{token.Form} is already {label}."
                : $"Motif cannot stage {label} for {token.Form} from this view.");
            return;
        }
        if (token.StageMarkingChoiceForTokenCommand?.CanExecute(choice) != true)
        {
            KeyboardShortcutStatus.Announce(source, $"Motif cannot stage the change for {token.Form} right now.");
            return;
        }
        await token.StageMarkingChoiceForTokenCommand.ExecuteAsync(choice).ConfigureAwait(true);
        KeyboardShortcutStatus.Announce(source, null);
    }

    private void OnCloseCardClick(object? sender, RoutedEventArgs e) => CloseCardOntoItsWord();

    private void CloseCardOntoItsWord()
    {
        if (InText.SelectedToken is not { } token) return;
        InText.CloseTokenCard();
        Dispatcher.UIThread.Post(() =>
        {
            BringTokenLineIntoView(token);
            StripFor(token)?.Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Loaded);
    }

    // The card is built under its line after the selection changes, so focus waits for that layout pass.
    private void FocusCard(ResultsTokenViewModel token)
    {
        if (!ReferenceEquals(InText.SelectedToken, token)) return;
        BringTokenLineIntoView(token);
        var card = this.GetVisualDescendants().OfType<Border>().FirstOrDefault(border =>
            border.Classes.Contains("wordCard") && ReferenceEquals(border.DataContext, token));
        if (card is null) return;
        card.Focus(NavigationMethod.Directional);
        card.BringIntoView();
    }

    private void BringTokenLineIntoView(ResultsTokenViewModel token)
    {
        var line = InText.VisibleLines.FirstOrDefault(line => line.Tokens.Contains(token));
        if (line is null) return;
        _lineItems.ScrollIntoView(InText.VisibleLines.IndexOf(line));
        _lineItems.UpdateLayout();
        foreach (var words in this.GetVisualDescendants().OfType<ProgressiveItemsControl>())
            if (ReferenceEquals(words.FullItemsSource, line.Tokens)) words.ShowItem(token);
        _lineItems.UpdateLayout();
    }

    private Border? StripFor(ResultsTokenViewModel token) => this.GetVisualDescendants().OfType<Border>()
        .FirstOrDefault(border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, token));

    /// <summary>Opens a word card for Enter or Space and leaves other keys available to the control.</summary>
    /// <param name="key">The key pressed on the word.</param>
    /// <param name="token">The word occurrence attached to the focused control.</param>
    /// <param name="openTokenCard">The action that selects and opens the occurrence's card.</param>
    /// <returns>Whether the key opened the card.</returns>
    internal static async Task<bool> OpenTokenCardOnKeyboardAsync(Key key, ResultsTokenViewModel? token,
        Func<ResultsTokenViewModel, Task> openTokenCard)
    {
        ArgumentNullException.ThrowIfNull(openTokenCard);
        if (key is not (Key.Enter or Key.Space) || token is null) return false;
        await openTokenCard(token).ConfigureAwait(true);
        return true;
    }
}
