using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Templates;
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
    private readonly Border _fieldWorksLine;
    private readonly Panel _wordLinePanel;
    private readonly Panel _stripStack;
    private readonly Panel _stripContent;
    private readonly Control _wordText;
    private readonly Control _verdictGlyph;
    private readonly Control _markFace;
    private readonly Border _actions;
    private readonly List<Control> _owned = [];
    private Control? _addFace;
    private Control? _selectionGlyph;
    private Control? _unreadMark;
    private Control? _namedWarning;
    private Control? _disapprovedMark;
    private MorphemePanel? _fieldWorksMorphology;
    private Control? _moreFieldWorks;
    private Control? _nothingStored;
    private Control? _fieldWorksColumns;
    private Border? _panGlossLine;
    private MorphemePanel? _panGlossMorphology;
    private Control? _morePanGloss;
    private Control? _panGlossNote;
    private Control? _panGlossColumns;
    private Control? _actionRow;
    private Control? _stagedChanges;
    private WordStripOverlay? _ownOverlay;
    private ResultsTokenViewModel? _observedData;
    private Point? _pressedAt;

    static WordStripToken()
    {
        DataProperty.Changed.AddClassHandler<WordStripToken>((row, _) => row.OnDataChanged());
        StateProperty.Changed.AddClassHandler<WordStripToken>((row, _) => row.OnStateChanged());
        ShowsSelectionCheckBoxProperty.Changed.AddClassHandler<WordStripToken>((row, _) => row.SyncOptionalControls());
    }

    /// <summary>How many word strips this process has built, so a test can bound the work of a scroll or jump.</summary>
    internal static int BuiltCount => _builtCount;

    private static int _builtCount;

    public WordStripToken()
    {
        Interlocked.Increment(ref _builtCount);
        AvaloniaXamlLoader.Load(this);
        _strip = this;
        _wordLine = this.FindControl<Control>("WordLine")!;
        _wordLinePanel = (Panel)_wordLine;
        _fieldWorksLine = this.FindControl<Border>("FieldWorksLine")!;
        _stripStack = this.FindControl<Panel>("StripStack")!;
        _stripContent = this.FindControl<Panel>("StripContent")!;
        _wordText = this.FindControl<Control>("WordText")!;
        _verdictGlyph = this.FindControl<Control>("VerdictGlyph")!;
        _markFace = this.FindControl<Control>("MarkFace")!;
        _actions = this.FindControl<Border>("StripActions")!;
        Focusable = true;
        IsTabStop = true;
        _strip.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _strip.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(MorphemePanel.OverflowRequestedEvent, OnOverflowRequested, RoutingStrategies.Bubble);
        _strip.KeyDown += OnKeyDown;
        PointerEntered += (_, _) => Overlay.Claim(this);
        PointerExited += (_, _) => ReleaseOverlayIfIdle();
        GotFocus += (_, _) => Overlay.Claim(this);
        LostFocus += (_, _) => Dispatcher.UIThread.Post(ReleaseOverlayIfIdle);
    }

    private WordStripOverlay Overlay => WordStripOverlay.GetInstance(this) ?? (_ownOverlay ??= new WordStripOverlay());

    /// <summary>Builds a rarely shown part of a strip from the shared part templates.</summary>
    internal static T BuildPart<T>(string name, object? data) where T : Control
    {
        var key = "WordStripPart." + name;
        var template = Application.Current?.FindResource(key) as IDataTemplate ??
            throw new InvalidOperationException($"The word strip part {key} is missing.");
        return (T)template.Build(data)!;
    }

    internal void ReleaseOverlayIfIdle()
    {
        var overlay = Overlay;
        if (ReferenceEquals(overlay.Owner, this) && !IsPointerOver && !IsKeyboardFocusWithin && !overlay.HasOpenMenu)
            overlay.Release(this);
    }

    /// <summary>Puts the resting faces or the shared overlay in this word, whichever it now shows.</summary>
    internal void SyncOverlay() => SyncOptionalControls();

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

    private static double MeasuredLineHeight(Control? row) => row is { IsVisible: true, Parent: not null } ? row.DesiredSize.Height : 0;

    private void OnDataChanged()
    {
        if (_observedData is not null) _observedData.PropertyChanged -= OnDataPropertyChanged;
        if (ReferenceEquals(Overlay.Owner, this)) Overlay.Release(this);
        if (Data is not { } data)
        {
            _observedData = null;
            _strip.Tag = null;
            ApplyOverflowRequests();
            SyncOptionalControls();
            return;
        }
        _observedData = data;
        _observedData.PropertyChanged += OnDataPropertyChanged;
        if (State?.Key != data.PresentationKey)
            SetCurrentValue(StateProperty, data.PresentationState);
        AutomationProperties.SetName(_strip, data.HoverSummary);
        _strip.Tag = data;
        AutomationProperties.SetAutomationId(_strip, data.StripAutomationId);
        SyncOptionalControls();
        ApplyOverflowRequests();
    }

    private void ApplyOverflowRequests()
    {
        var data = Data;
        if (_fieldWorksMorphology is not null)
            _fieldWorksMorphology.OverflowRequest = data is null ? null : new MorphemeOverflowRequest(
                data.PresentationKey.Value, $"{data.PresentationKey.Value}:fieldworks", data.EvidenceRevision,
                MorphologyProducer.FieldWorks);
        if (_panGlossMorphology is not null)
            _panGlossMorphology.OverflowRequest = data is null ? null : new MorphemeOverflowRequest(
                data.PresentationKey.Value, $"{data.PresentationKey.Value}:pangloss", data.EvidenceRevision,
                MorphologyProducer.PanGloss);
    }

    private void OnDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsTokenViewModel.HoverSummary))
        {
            if (Dispatcher.UIThread.CheckAccess()) SetCurrentAutomationName();
            else Dispatcher.UIThread.Post(SetCurrentAutomationName);
            return;
        }
        if (e.PropertyName is { Length: > 0 } name && name is not (
                nameof(ResultsTokenViewModel.ShowUnread) or nameof(ResultsTokenViewModel.HasNamedWarning) or
                nameof(ResultsTokenViewModel.HasDisapprovedReading) or nameof(ResultsTokenViewModel.HasFieldWorksAnalyses) or
                nameof(ResultsTokenViewModel.HasMoreFieldWorksAnalyses) or nameof(ResultsTokenViewModel.HasNothingStored) or
                nameof(ResultsTokenViewModel.ShowsPanGlossReading) or nameof(ResultsTokenViewModel.HasMorePanGlossReadings) or
                nameof(ResultsTokenViewModel.ShowsPanGlossNote) or nameof(ResultsTokenViewModel.HasPrimaryAction) or
                nameof(ResultsTokenViewModel.HasStagedChanges) or nameof(ResultsTokenViewModel.ShowsActions) or
                nameof(ResultsTokenViewModel.Disposition))) return;
        if (Dispatcher.UIThread.CheckAccess()) SyncOptionalControls();
        else Dispatcher.UIThread.Post(SyncOptionalControls);
    }

    private void SetCurrentAutomationName()
    {
        if (_observedData is { } data) AutomationProperties.SetName(_strip, data.HoverSummary);
    }

    private T Part<T>(ref T? field, string name) where T : Control
    {
        if (field is null)
        {
            field = BuildPart<T>(name, Data);
            _owned.Add(field);
        }
        return field;
    }

    private void SyncOptionalControls()
    {
        var data = Data;
        var overlay = Overlay;
        var active = ReferenceEquals(overlay.Owner, this) && data is not null;
        var line = new List<Control>(8);
        if (ShowsSelectionCheckBox)
            line.Add(active ? overlay.SelectionBox : Part(ref _selectionGlyph, "SelectionGlyph"));
        if (data?.ShowUnread == true) line.Add(Part(ref _unreadMark, "UnreadMark"));
        line.Add(_wordText);
        line.Add(_verdictGlyph);
        if (data?.HasNamedWarning == true) line.Add(Part(ref _namedWarning, "NamedWarning"));
        if (active) line.Add(overlay.Buttons);
        else
        {
            line.Add(_markFace);
            if (data?.Disposition.HasReadings == true) line.Add(Part(ref _addFace, "AddFace"));
        }
        if (data?.HasDisapprovedReading == true) line.Add(Part(ref _disapprovedMark, "DisapprovedChip"));
        Reconcile(_wordLinePanel, line);

        var fieldWorks = new List<Control>(3);
        if (data?.HasFieldWorksAnalyses == true)
        {
            var created = _fieldWorksMorphology is null;
            fieldWorks.Add(Part(ref _fieldWorksMorphology, "FieldWorksMorphology"));
            if (created) ApplyOverflowRequests();
        }
        if (data?.HasMoreFieldWorksAnalyses == true) fieldWorks.Add(Part(ref _moreFieldWorks, "MoreFieldWorks"));
        if (data?.HasNothingStored == true) fieldWorks.Add(Part(ref _nothingStored, "NothingStored"));
        FillRow(_fieldWorksLine, fieldWorks, ref _fieldWorksColumns);

        var rows = new List<Control>(3) { _wordLine, _fieldWorksLine };
        if (data is { Verdict: not OccurrenceVerdict.Matches })
        {
            var panGlossLine = Part(ref _panGlossLine, "PanGlossLine");
            var panGloss = new List<Control>(3);
            if (data.ShowsPanGlossReading)
            {
                var created = _panGlossMorphology is null;
                panGloss.Add(Part(ref _panGlossMorphology, "PanGlossMorphology"));
                if (created) ApplyOverflowRequests();
            }
            if (data.HasMorePanGlossReadings) panGloss.Add(Part(ref _morePanGloss, "MorePanGloss"));
            if (data.ShowsPanGlossNote) panGloss.Add(Part(ref _panGlossNote, "PanGlossNote"));
            FillRow(panGlossLine, panGloss, ref _panGlossColumns);
            rows.Add(panGlossLine);
        }
        Reconcile(_stripStack, rows);

        var columns = new List<Control>(3) { _stripStack };
        if (data is { ShowsActions: true })
        {
            SetChild(_actions, data.HasPrimaryAction ? Part(ref _actionRow, "ActionRow") : null);
            columns.Add(_actions);
        }
        if (data?.HasStagedChanges == true) columns.Add(Part(ref _stagedChanges, "Staged"));
        Reconcile(_stripContent, columns);
        Park();
    }

    private void FillRow(Border row, List<Control> items, ref Control? columnsField)
    {
        if (items.Count == 0)
        {
            row.Child = null;
            return;
        }
        if (items.Count == 1)
        {
            SetChild(row, items[0]);
            return;
        }
        var columns = (Grid)Part(ref columnsField, "Row");
        SetChild(row, columns);
        Reconcile(columns, items);
    }

    private void Reconcile(Panel panel, List<Control> desired)
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var child = desired[index];
            if (panel is Grid) Grid.SetColumn(child, ColumnOf(panel, child, index));
            if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], child)) continue;
            Detach(child);
            panel.Children.Insert(Math.Min(index, panel.Children.Count), child);
        }
        for (var index = panel.Children.Count - 1; index >= desired.Count; index--) panel.Children.RemoveAt(index);
        for (var index = panel.Children.Count - 1; index >= 0; index--)
            if (!desired.Contains(panel.Children[index])) panel.Children.RemoveAt(index);
    }

    private int ColumnOf(Panel panel, Control child, int index)
    {
        if (!ReferenceEquals(panel, _stripContent)) return index;
        return ReferenceEquals(child, _stripStack) ? 0 : ReferenceEquals(child, _actions) ? 1 : 2;
    }

    private void Detach(Control child)
    {
        if (ReferenceEquals(child.Parent, this)) LogicalChildren.Remove(child);
        switch (child.Parent)
        {
            case Panel panel: panel.Children.Remove(child); break;
            case Decorator decorator when ReferenceEquals(decorator.Child, child): decorator.Child = null; break;
            case ContentControl content when ReferenceEquals(content.Content, child): content.Content = null; break;
        }
    }

    private void SetChild(Border parent, Control? child)
    {
        if (ReferenceEquals(parent.Child, child)) return;
        parent.Child = null;
        if (child is null) return;
        Detach(child);
        parent.Child = child;
    }

    private void Park()
    {
        foreach (var part in _owned)
        {
            if (part.GetVisualParent() is null)
            {
                if (!LogicalChildren.Contains(part)) LogicalChildren.Add(part);
            }
            else if (ReferenceEquals(part.Parent, this)) LogicalChildren.Remove(part);
        }
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
        if (_selectionGlyph is not null && e.Source is Visual hit &&
            hit.GetSelfAndVisualAncestors().Any(visual => ReferenceEquals(visual, _selectionGlyph)) &&
            e.GetCurrentPoint(_strip).Properties.IsLeftButtonPressed && Data is { } selected)
        {
            selected.IsSelectedForActions = !selected.IsSelectedForActions;
            _pressedAt = null;
            e.Handled = true;
            return;
        }
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
        "TextReader:FirstItem", "TextReader:LastItem", "TextReader:Approve", "TextReader:Disapprove", "TextReader:Unknown",
        "TextReader:RemoveAnalysis", "TextReader:IncorrectSpelling", "TextReader:OpenAddList",
        "TextReader:OpenDispositionMenu")]
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Data is not { IsWord: true } token || KeyboardShortcutRegistry.IsTextInput(e.Source)) return;
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.CloseWordCard, KeyboardShortcutBehavior.OpenWordCard]) ??
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
                var previous = entry.Behavior == KeyboardShortcutBehavior.PreviousWord;
                if (_wordText.FlowDirection == Avalonia.Media.FlowDirection.RightToLeft) previous = !previous;
                await NavigateAsync(token, previous ? WordAction.NavigatePrevious : WordAction.NavigateNext);
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
            case KeyboardShortcutBehavior.RemoveAnalysis:
                if (token.Disposition.StoredRows.FirstOrDefault()?.Tiles
                        .FirstOrDefault(tile => tile.Kind == WordDispositionKind.Absent)?.Action is not { } removal)
                    break;
                e.Handled = true;
                if (token.StageMarkingChoiceForTokenCommand?.CanExecute(removal) == true)
                    await token.StageMarkingChoiceForTokenCommand.ExecuteAsync(removal);
                break;
            case KeyboardShortcutBehavior.IncorrectSpelling:
                e.Handled = true;
                var spelling = token.IncorrectSpellingAction;
                if (token.AddChangeForTokenCommand?.CanExecute(spelling) == true)
                    await token.AddChangeForTokenCommand.ExecuteAsync(spelling);
                break;
            case KeyboardShortcutBehavior.OpenAddList:
            case KeyboardShortcutBehavior.OpenDispositionMenu:
                var name = entry.Behavior == KeyboardShortcutBehavior.OpenAddList
                    ? WordDispositionButtons.AddButtonName : WordDispositionButtons.MarkButtonName;
                Overlay.Claim(this);
                if (Overlay.Buttons.Children.OfType<Button>().FirstOrDefault(button =>
                        button.IsEffectivelyVisible && AutomationProperties.GetName(button) == name) is not { } opener)
                    break;
                e.Handled = true;
                opener.Flyout?.ShowAt(opener);
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
