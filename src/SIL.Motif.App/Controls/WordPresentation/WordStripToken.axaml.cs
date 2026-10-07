using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Threading;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Views;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>A selectable text occurrence with its compact analyses and next actions.</summary>
public sealed partial class WordStripToken : Border
{
    public static readonly StyledProperty<ResultsTokenViewModel?> DataProperty =
        AvaloniaProperty.Register<WordStripToken, ResultsTokenViewModel?>(nameof(Data));

    public static readonly StyledProperty<WordInteractionState?> StateProperty =
        AvaloniaProperty.Register<WordStripToken, WordInteractionState?>(nameof(State),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IWordPresentationHost?> HostProperty =
        AvaloniaProperty.Register<WordStripToken, IWordPresentationHost?>(nameof(Host));

    public static readonly StyledProperty<bool> ShowsSelectionCheckBoxProperty =
        AvaloniaProperty.Register<WordStripToken, bool>(nameof(ShowsSelectionCheckBox));

    private readonly Border _strip;
    private readonly Control _wordLine;
    private readonly Control _fieldWorksLine;
    private readonly Control _panGlossLine;
    private readonly Panel _wordLinePanel;
    private readonly Panel _fieldWorksContent;
    private readonly Panel _panGlossContent;
    private readonly Panel _stripContent;
    private readonly Panel _actionRow;
    private readonly Control _selectionCheckBox;
    private readonly UnreadMark _unreadMark;
    private readonly Control _namedWarning;
    private readonly Control _disapprovedMark;
    private readonly Control _moreFieldWorks;
    private readonly Control _nothingStored;
    private readonly Control _morePanGloss;
    private readonly Control _panGlossNote;
    private readonly Control _primaryAction;
    private readonly Control _stagedChanges;
    private readonly Control _actions;
    private readonly MorphemePanel _fieldWorksMorphology;
    private readonly MorphemePanel _panGlossMorphology;
    private ResultsTokenViewModel? _observedData;
    private Point? _pressedAt;

    static WordStripToken()
    {
        DataProperty.Changed.AddClassHandler<WordStripToken>((row, _) => row.OnDataChanged());
        StateProperty.Changed.AddClassHandler<WordStripToken>((row, _) => row.OnStateChanged());
        ShowsSelectionCheckBoxProperty.Changed.AddClassHandler<WordStripToken>((row, _) => row.SyncOptionalControls());
    }

    public WordStripToken()
    {
        AvaloniaXamlLoader.Load(this);
        _strip = this;
        _wordLine = this.FindControl<Control>("WordLine")!;
        _wordLinePanel = (Panel)_wordLine;
        _fieldWorksLine = this.FindControl<Control>("FieldWorksLine")!;
        _panGlossLine = this.FindControl<Control>("PanGlossLine")!;
        _stripContent = this.FindControl<Panel>("StripContent")!;
        _fieldWorksContent = this.FindControl<Panel>("FieldWorksContent")!;
        _panGlossContent = this.FindControl<Panel>("PanGlossContent")!;
        _actionRow = this.FindControl<Panel>("ActionRow")!;
        _selectionCheckBox = this.FindControl<Control>("SelectionCheckBox")!;
        _unreadMark = this.FindControl<UnreadMark>("UnreadMark")!;
        _namedWarning = this.FindControl<Control>("NamedWarning")!;
        _disapprovedMark = this.FindControl<Control>("DisapprovedMarkChip")!;
        _moreFieldWorks = this.FindControl<Control>("MoreFieldWorks")!;
        _nothingStored = this.FindControl<Control>("NothingStored")!;
        _morePanGloss = this.FindControl<Control>("MorePanGloss")!;
        _panGlossNote = this.FindControl<Control>("PanGlossNote")!;
        _primaryAction = this.FindControl<Control>("PrimaryActionButton")!;
        _stagedChanges = this.FindControl<Control>("ResultsInTextPanelStagedChangesItems")!;
        _actions = this.FindControl<Control>("StripActions")!;
        _fieldWorksMorphology = this.FindControl<MorphemePanel>("FieldWorksMorphology")!;
        _panGlossMorphology = this.FindControl<MorphemePanel>("PanGlossMorphology")!;
        Focusable = true;
        IsTabStop = true;
        _strip.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _strip.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(MorphemePanel.OverflowRequestedEvent, OnOverflowRequested, RoutingStrategies.Bubble);
        _strip.KeyDown += OnKeyDown;
    }

    /// <summary>The occurrence and analysis facts to present.</summary>
    public ResultsTokenViewModel? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>The keyed open and bulk-action selection state of this occurrence.</summary>
    public WordInteractionState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The Analyze text host for card reads, navigation, and occurrence actions.</summary>
    public IWordPresentationHost? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    /// <summary>Whether this Analyze view currently allows bulk-action checkboxes.</summary>
    public bool ShowsSelectionCheckBox
    {
        get => GetValue(ShowsSelectionCheckBoxProperty);
        set => SetValue(ShowsSelectionCheckBoxProperty, value);
    }

    /// <summary>Focuses the occurrence target without exposing a template child.</summary>
    public void FocusWord() => _strip.Focus(NavigationMethod.Directional);

    internal RunningTextLineMetrics LineMetrics { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        LineMetrics = new RunningTextLineMetrics(
            MeasuredLineHeight(_wordLine), MeasuredLineHeight(_fieldWorksLine), MeasuredLineHeight(_panGlossLine));
        return size;
    }

    private static double MeasuredLineHeight(Control row)
    {
        var textHeight = row.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.TextLayout.Height).DefaultIfEmpty(0).Max();
        return Math.Max(row.DesiredSize.Height, textHeight);
    }

    private void OnDataChanged()
    {
        if (_observedData is not null) _observedData.PropertyChanged -= OnDataPropertyChanged;
        if (Data is not { } data)
        {
            _observedData = null;
            _fieldWorksMorphology.OverflowRequest = null;
            _panGlossMorphology.OverflowRequest = null;
            SyncOptionalControls();
            return;
        }
        _observedData = data;
        _observedData.PropertyChanged += OnDataPropertyChanged;
        if (State?.Key != data.PresentationKey)
            SetCurrentValue(StateProperty, data.PresentationState);
        _fieldWorksMorphology.OverflowRequest = new MorphemeOverflowRequest(data.PresentationKey.Value,
            $"{data.PresentationKey.Value}:fieldworks", data.EvidenceRevision, MorphologyProducer.FieldWorks);
        _panGlossMorphology.OverflowRequest = new MorphemeOverflowRequest(data.PresentationKey.Value,
            $"{data.PresentationKey.Value}:pangloss", data.EvidenceRevision, MorphologyProducer.PanGloss);
        AutomationProperties.SetName(_strip, data.HoverSummary);
        _strip.Tag = data;
        AutomationProperties.SetAutomationId(_strip, data.StripAutomationId);
        SyncOptionalControls();
    }

    private void OnDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { Length: > 0 } name && name is not (
                nameof(ResultsTokenViewModel.ShowUnread) or nameof(ResultsTokenViewModel.HasNamedWarning) or
                nameof(ResultsTokenViewModel.HasDisapprovedReading) or nameof(ResultsTokenViewModel.HasFieldWorksAnalyses) or
                nameof(ResultsTokenViewModel.HasMoreFieldWorksAnalyses) or nameof(ResultsTokenViewModel.HasNothingStored) or
                nameof(ResultsTokenViewModel.ShowsPanGlossReading) or nameof(ResultsTokenViewModel.HasMorePanGlossReadings) or
                nameof(ResultsTokenViewModel.ShowsPanGlossNote) or nameof(ResultsTokenViewModel.HasPrimaryAction) or
                nameof(ResultsTokenViewModel.HasStagedChanges) or nameof(ResultsTokenViewModel.ShowsActions))) return;
        if (Dispatcher.UIThread.CheckAccess()) SyncOptionalControls();
        else Dispatcher.UIThread.Post(SyncOptionalControls);
    }

    private void SyncOptionalControls()
    {
        var data = Data;
        Place(_wordLinePanel, _selectionCheckBox, ShowsSelectionCheckBox, 0);
        var hasUnread = data?.ShowUnread == true;
        Place(_wordLinePanel, _unreadMark, hasUnread, ShowsSelectionCheckBox ? 1 : 0);
        var hasWarning = data?.HasNamedWarning == true;
        Place(_wordLinePanel, _namedWarning, hasWarning,
            (ShowsSelectionCheckBox ? 1 : 0) + (hasUnread ? 1 : 0) + 1);
        Place(_wordLinePanel, _disapprovedMark, data?.HasDisapprovedReading == true,
            (ShowsSelectionCheckBox ? 1 : 0) + (hasUnread ? 1 : 0) + 1 + (hasWarning ? 1 : 0) + 1);
        var hasFieldWorks = data?.HasFieldWorksAnalyses == true;
        var hasMoreFieldWorks = data?.HasMoreFieldWorksAnalyses == true;
        Place(_fieldWorksContent, _fieldWorksMorphology, hasFieldWorks, 0);
        var fieldWorksIndex = hasFieldWorks ? 1 : 0;
        Place(_fieldWorksContent, _moreFieldWorks, hasMoreFieldWorks, fieldWorksIndex);
        fieldWorksIndex += hasMoreFieldWorks ? 1 : 0;
        Place(_fieldWorksContent, _nothingStored, data?.HasNothingStored == true, fieldWorksIndex);
        var hasPanGloss = data?.ShowsPanGlossReading == true;
        var hasMorePanGloss = data?.HasMorePanGlossReadings == true;
        Place(_panGlossContent, _panGlossMorphology, hasPanGloss, 0);
        var panGlossIndex = hasPanGloss ? 1 : 0;
        Place(_panGlossContent, _morePanGloss, hasMorePanGloss, panGlossIndex);
        panGlossIndex += hasMorePanGloss ? 1 : 0;
        Place(_panGlossContent, _panGlossNote, data?.ShowsPanGlossNote == true, panGlossIndex);
        Place(_stripContent, _actions, data is { ShowsActions: true }, 1);
        Place(_stripContent, _stagedChanges, data?.HasStagedChanges == true, 2);
        Place(_actionRow, _primaryAction, data?.HasPrimaryAction == true, 0);
    }

    private static void Place(Panel parent, Control child, bool include, int index)
    {
        if (!include)
        {
            if (ReferenceEquals(child.Parent, parent)) parent.Children.Remove(child);
            return;
        }
        if (parent is Grid) Grid.SetColumn(child, index);
        if (ReferenceEquals(child.Parent, parent)) return;
        if (child.Parent is Panel previous) previous.Children.Remove(child);
        parent.Children.Insert(Math.Min(index, parent.Children.Count), child);
    }

    private void OnStateChanged()
    {
        if (Data is not { } data || State is not { } state) return;
        if (state.Key != data.PresentationKey)
            throw new InvalidOperationException("Word interaction state belongs to another occurrence.");
        if (data.PresentationState != state) data.PresentationState = state;
    }

    private void OnOverflowRequested(object? sender, MorphemeOverflowRequestedEventArgs e)
    {
        if (Data is not { } data || e.Request.WordIdentity != data.PresentationKey.Value ||
            e.Request.EvidenceRevision != data.EvidenceRevision) return;
        _ = SendAsync(new WordRequest(data.PresentationKey, data.EvidenceRevision, WordAction.OpenMorphology,
            Producer: e.Request.Producer));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_strip);
        var nested = e.Source is Control source && source.GetSelfAndVisualAncestors()
            .TakeWhile(control => !ReferenceEquals(control, _strip))
            .Any(control => control is Button or ToggleButton or CheckBox or TextBox or ComboBox);
        _pressedAt = point.Properties.IsLeftButtonPressed && e.ClickCount == 1 && !nested
            ? point.Position : null;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedAt is not { } start || Data is not { IsWord: true } data) return;
        _pressedAt = null;
        var moved = e.GetPosition(_strip) - start;
        if (Math.Abs(moved.X) > 4 || Math.Abs(moved.Y) > 4 ||
            e.Source is SelectableTextBlock { SelectedText.Length: > 0 }) return;
        FocusWord();
        _ = SendAsync(new WordRequest(data.PresentationKey, data.EvidenceRevision, WordAction.OpenCard));
    }

    [KeyboardShortcutHandler(
        "TextReader:CloseWordCard", "TextReader:PreviousWord", "TextReader:NextWord", "TextReader:PreviousLine",
        "TextReader:NextLine", "TextReader:OpenWordCard", "TextReader:PreviousScreen", "TextReader:NextScreen",
        "TextReader:FirstItem", "TextReader:LastItem", "TextReader:Approve", "TextReader:Disapprove", "TextReader:Unknown")]
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Data is not { IsWord: true } token || KeyboardShortcutRegistry.IsTextInput(e.Source)) return;
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.CloseWordCard]) ??
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers);
        if (entry is null || !KeyboardShortcutRegistry.Allows(entry, isTextInput: false, hasFocusedItem: true)) return;
        switch (entry.Behavior)
        {
            case KeyboardShortcutBehavior.CloseWordCard:
                e.Handled = true;
                await SendAsync(new WordRequest(token.PresentationKey, token.EvidenceRevision, WordAction.CloseCard));
                break;
            case KeyboardShortcutBehavior.PreviousWord:
            case KeyboardShortcutBehavior.NextWord:
                e.Handled = true;
                await NavigateAsync(token, entry.Behavior == KeyboardShortcutBehavior.PreviousWord
                    ? WordAction.NavigatePrevious : WordAction.NavigateNext);
                break;
            case KeyboardShortcutBehavior.PreviousLine:
            case KeyboardShortcutBehavior.NextLine:
                e.Handled = true;
                await NavigateAsync(token, entry.Behavior == KeyboardShortcutBehavior.PreviousLine
                    ? WordAction.NavigatePreviousLine : WordAction.NavigateNextLine);
                break;
            case KeyboardShortcutBehavior.OpenWordCard:
                e.Handled = await OpenCardOnKeyboardAsync(e.Key, token, async word =>
                    await SendAsync(new WordRequest(word.PresentationKey, word.EvidenceRevision, WordAction.OpenCard)));
                break;
            case KeyboardShortcutBehavior.PreviousScreen:
            case KeyboardShortcutBehavior.NextScreen:
                e.Handled = true;
                await NavigateAsync(token, entry.Behavior == KeyboardShortcutBehavior.PreviousScreen
                    ? WordAction.NavigatePreviousPage : WordAction.NavigateNextPage);
                break;
            case KeyboardShortcutBehavior.FirstItem:
            case KeyboardShortcutBehavior.LastItem:
                e.Handled = true;
                await NavigateAsync(token, entry.Behavior == KeyboardShortcutBehavior.FirstItem
                    ? WordAction.NavigateFirst : WordAction.NavigateLast);
                break;
            case KeyboardShortcutBehavior.Approve:
            case KeyboardShortcutBehavior.Disapprove:
            case KeyboardShortcutBehavior.Unknown:
                e.Handled = true;
                var opinion = entry.Behavior switch
                {
                    KeyboardShortcutBehavior.Approve => OpinionMarkKind.Approved,
                    KeyboardShortcutBehavior.Disapprove => OpinionMarkKind.Disapproved,
                    _ => OpinionMarkKind.Unknown,
                };
                await SendAsync(new WordRequest(token.PresentationKey, token.EvidenceRevision,
                    WordAction.SetOpinion, opinion));
                break;
        }
    }

    internal static async Task<bool> OpenCardOnKeyboardAsync(
        Key key,
        ResultsTokenViewModel? token,
        Func<ResultsTokenViewModel, Task> openTokenCard)
    {
        ArgumentNullException.ThrowIfNull(openTokenCard);
        if (key is not (Key.Enter or Key.Space) || token is null) return false;
        await openTokenCard(token).ConfigureAwait(true);
        return true;
    }

    private Task NavigateAsync(ResultsTokenViewModel token, WordAction action) =>
        SendAsync(new WordRequest(token.PresentationKey, token.EvidenceRevision, action));

    private async Task SendAsync(WordRequest request)
    {
        if (Host is not { } host) return;
        var result = await host.HandleAsync(request, CancellationToken.None);
        if (result.StatusMessage is { Length: > 0 } message)
            KeyboardShortcutStatus.Announce(this, message);
        else if (request.Action == WordAction.SetOpinion)
            KeyboardShortcutStatus.Announce(this, null);
    }
}
