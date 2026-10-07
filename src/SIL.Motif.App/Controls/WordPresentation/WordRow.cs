using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>A shallow word row whose layout and actions are supplied by its owning list.</summary>
public sealed class WordRow : UserControl
{
    public static readonly StyledProperty<WordPresentation?> DataProperty =
        AvaloniaProperty.Register<WordRow, WordPresentation?>(nameof(Data));

    public static readonly StyledProperty<WordInteractionState?> StateProperty =
        AvaloniaProperty.Register<WordRow, WordInteractionState?>(nameof(State), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IWordPresentationHost?> HostProperty =
        AvaloniaProperty.Register<WordRow, IWordPresentationHost?>(nameof(Host));

    private readonly Border _frame;
    private readonly Grid _shell;
    private CancellationTokenSource? _readCancellation;
    private Point? _pressedAt;
    private CheckBox? _tick;
    private WordCard? _placedCard;
    private int _columnCount = 1;
    private bool _changingData;

    static WordRow()
    {
        DataProperty.Changed.AddClassHandler<WordRow>((row, _) => row.OnDataChanged());
        StateProperty.Changed.AddClassHandler<WordRow>((row, _) => row.OnStateChanged());
        HostProperty.Changed.AddClassHandler<WordRow>((row, _) => row.RefreshCardHost());
        WordListLayoutScope.LayoutProperty.Changed.AddClassHandler<WordRow>((row, _) => row.RebuildCells());
    }

    public WordRow()
    {
        Focusable = true;
        IsTabStop = true;
        _shell = new Grid { Classes = { "wordPresentationShell" } };
        _frame = new Border { Classes = { "wordPresentationFrame" }, Child = _shell };
        Content = _frame;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        KeyDown += OnKeyDown;
    }

    /// <summary>The semantic word facts and stable host identity shown by this row.</summary>
    public WordPresentation? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value?.Validate());
    }

    /// <summary>The open, checked and selected state retained by the list for this word.</summary>
    public WordInteractionState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The list-owned reader, command adapter and navigation host.</summary>
    public IWordPresentationHost? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    /// <summary>Focuses the semantic word target without exposing a template child.</summary>
    public void FocusWord() => Focus(NavigationMethod.Tab);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelRead();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (State?.IsOpen == true) StartCardRead();
    }

    /// <summary>How many times this row has built its cells; a recycled row builds them once per word.</summary>
    internal int CellBuildCount { get; private set; }

    private void OnDataChanged()
    {
        CancelRead();
        _changingData = true;
        try
        {
            if (Data is { } data && State?.Key != data.Key)
                SetCurrentValue(StateProperty, new WordInteractionState(data.Key));
        }
        finally
        {
            _changingData = false;
        }
        RebuildCells();
        if (State?.IsOpen == true) StartCardRead();
    }

    private void OnStateChanged()
    {
        if (_changingData) return;
        if (Data is { } data && State is { } state && state.Key != data.Key)
            throw new InvalidOperationException("Word interaction state belongs to another word identity.");
        if (State?.IsOpen == true) StartCardRead();
        else CancelRead();
        SyncState();
    }

    // Opening, closing and ticking a row change only its frame, its tick and its card, never its cells.
    private void SyncState()
    {
        _frame.Classes.Set("open", State?.IsOpen == true);
        if (_tick is { } tick && tick.IsChecked != State?.IsChecked) tick.IsChecked = State?.IsChecked;
        var card = State?.IsOpen == true ? _card : null;
        if (ReferenceEquals(card, _placedCard)) return;
        if (_placedCard is { } placed) _shell.Children.Remove(placed);
        _placedCard = card;
        if (card is null) return;
        card.Classes.Add("wordPresentationCardHost");
        Grid.SetRow(card, 2);
        Grid.SetColumnSpan(card, _columnCount);
        _shell.Children.Add(card);
    }

    private void RebuildCells()
    {
        CellBuildCount++;
        _tick = null;
        _placedCard = null;
        _shell.Children.Clear();
        _shell.ColumnDefinitions.Clear();
        _shell.RowDefinitions.Clear();
        _shell.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _shell.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _shell.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var data = Data;
        if (data is null)
        {
            SyncState();
            return;
        }
        var layout = WordListLayoutScope.GetLayout(this);
        var columns = layout?.Schema.OrderedColumns(layout.ShowsMeaning) ?? DefaultColumns();
        _columnCount = Math.Max(1, columns.Count);
        foreach (var column in columns)
        {
            var width = layout is null
                ? column == WordListColumn.Word ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
                : new GridLength(layout.WidthOf(column));
            _shell.ColumnDefinitions.Add(new ColumnDefinition(width));
        }
        for (var index = 0; index < columns.Count; index++)
        {
            var cell = Cell(data, columns[index]);
            if (cell is null) continue;
            Grid.SetColumn(cell, index);
            _shell.Children.Add(cell);
        }
        var note = new StackPanel { Classes = { "wordPresentationNotes" } };
        if (data.PendingChange is { } pending)
        {
            if (pending.IsUncertain)
            {
                var sentence = new WrapPanel { Classes = { "reviewActions" } };
                sentence.Children.Add(Text("Now reads", "reviewSmall reviewGapEnd"));
                sentence.Children.Add(PendingSentence(pending.AfterWords));
                note.Children.Add(sentence);
            }
            var actions = new WrapPanel { Classes = { "reviewActions" } };
            if (!string.IsNullOrWhiteSpace(data.StagedText))
                actions.Children.Add(Text(data.StagedText, "wordPresentationStaged reviewGapEnd"));
            if (!string.IsNullOrWhiteSpace(data.Note))
            {
                var detail = (CopyableTextBlock)Text(data.Note, "reviewSmall reviewGapEnd");
                detail.TextWrapping = TextWrapping.Wrap;
                actions.Children.Add(detail);
            }
            if (pending.StillFits)
            {
                actions.Children.Add(new Border
                {
                    Classes = { "reviewPill" },
                    Child = Text("Still fits", "wordPresentationNote"),
                });
            }
            if (pending.IsUncertain)
                actions.Children.Add(ActionButton(data, "Check again", WordAction.ReconfirmChange,
                    automationName: pending.ReconfirmAutomationName));
            actions.Children.Add(ActionButton(data, "Undo", WordAction.UndoChange,
                automationName: pending.UndoAutomationName));
            if (actions.Children.Count > 0) note.Children.Add(actions);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(data.StagedText))
                note.Children.Add(Text(data.StagedText, "wordPresentationStaged"));
            if (!string.IsNullOrWhiteSpace(data.Note))
                note.Children.Add(Text(data.Note, "wordPresentationNote"));
            if (!string.IsNullOrWhiteSpace(data.MeasuredText))
                note.Children.Add(Text(data.MeasuredText, "wordPresentationNote"));
        }
        if (note.Children.Count > 0)
        {
            Grid.SetRow(note, 1);
            Grid.SetColumnSpan(note, Math.Max(1, columns.Count));
            _shell.Children.Add(note);
        }
        AutomationProperties.SetName(this, data.Facts.Summary);
        AutomationProperties.SetAutomationId(this, data.Facts.AutomationIdOf(AutomationList(data.Owner), "row"));
        SyncState();
    }

    private WordCard? _card;

    private Control? Cell(WordPresentation data, WordListColumn column)
    {
        var facts = data.Facts;
        return column switch
        {
            WordListColumn.Tick => TickCell(data),
            WordListColumn.Word => WordCell(data),
            WordListColumn.FieldWorks => FieldWorksCell(data),
            WordListColumn.PanGloss => PanGlossCell(data),
            WordListColumn.Meaning => new NamedMark { Mark = facts.MeaningMark, Text = facts.Meaning, Classes = { "wordPresentationMeaning" } },
            WordListColumn.Warnings => facts.HasWarningsNamed
                ? new NamedMark { Mark = facts.WarningMark, Text = facts.WarningsText, Classes = { "wordPresentationWarnings" } }
                : new Panel(),
            WordListColumn.Places => PlacesCell(facts),
            WordListColumn.Time => TimeCell(data),
            WordListColumn.Read => facts.ShowUnread ? UnreadCell(facts) : new Panel(),
            WordListColumn.Next => NextCell(data),
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };
    }

    private static Control WordCell(WordPresentation data)
    {
        var facts = data.Facts;
        var cell = new StackPanel { Classes = { "wordPresentationWord" } };
        AutomationProperties.SetAutomationId(cell, facts.AutomationIdOf(AutomationList(data.Owner), "word"));
        var form = LanguageText(facts.Word, facts.WordWritingSystem, "wordPresentationForm");
        form.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(form, facts.Summary);
        ClearTipPlacement.SetIsEnabled(form, true);
        cell.Children.Add(form);
        if (!string.IsNullOrEmpty(facts.Gloss))
        {
            var gloss = LanguageText(facts.Gloss, facts.GlossWritingSystem, "wordPresentationGloss");
            gloss.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(gloss, facts.Gloss);
            ClearTipPlacement.SetIsEnabled(gloss, true);
            cell.Children.Add(gloss);
        }
        return cell;
    }

    private Control FieldWorksCell(WordPresentation data)
    {
        var facts = data.Facts;
        if (facts.FieldWorksMorphemes.Count == 0)
            return new NamedMark { Mark = facts.OpinionMark, Text = facts.OpinionLabel };
        var panel = new MorphemePanel
        {
            Morphs = facts.FieldWorksMorphemes,
            ShowCategory = false,
            RevealLinks = true,
            CompactLinks = true,
            LeadingMark = facts.OpinionMark,
            LeadingText = facts.OpinionLabel,
            MaxVisibleMorphs = data.Owner is WordListOwner.Matrix or WordListOwner.FixFirst ? 2 : 6,
            OverflowRequest = new MorphemeOverflowRequest(data.Key.Value, $"{data.Key.Value}:fieldworks",
                data.EvidenceRevision, MorphologyProducer.FieldWorks),
            Classes = { "wordPresentationMorphology" },
        };
        panel.AddHandler(MorphemePanel.OverflowRequestedEvent, OnOverflowRequested);
        return panel;
    }

    private Control PanGlossCell(WordPresentation data)
    {
        var facts = data.Facts;
        if (facts.PanGlossMorphemes.Count > 0)
        {
            var morphs = facts.PanGlossMorphemes.Select(morph => morph.Morph).ToArray();
            var panel = new MorphemePanel
            {
                Morphs = morphs,
                Annotations = facts.PanGlossMorphemes.Select(morph =>
                    new MorphemePanelAnnotation(morph.IsDifferent, morph.EntryDifferenceTip)).ToArray(),
                ShowCategory = false,
                RevealLinks = true,
                CompactLinks = true,
                LeadingMark = facts.OutcomeMark,
                LeadingText = facts.OutcomeLabel,
                MaxVisibleMorphs = data.Owner is WordListOwner.Matrix or WordListOwner.FixFirst ? 2 : 6,
                OverflowRequest = new MorphemeOverflowRequest(data.Key.Value, $"{data.Key.Value}:pangloss",
                    data.EvidenceRevision, MorphologyProducer.PanGloss),
                Classes = { "wordPresentationMorphology" },
            };
            panel.AddHandler(MorphemePanel.OverflowRequestedEvent, OnOverflowRequested);
            return panel;
        }
        return new NamedMark { Mark = facts.OutcomeMark, Text = facts.OutcomeLabel };
    }

    private static Control PlacesCell(WordRowViewModel facts)
    {
        var cell = Text(facts.PlacesText, "wordPresentationNumber");
        cell.Classes.Add("wordPresentationPlaces");
        ToolTip.SetTip(cell, facts.PlacesTooltip);
        return cell;
    }

    private static Control TimeCell(WordPresentation data)
    {
        var timeText = data.TimeText ?? data.Facts.ElapsedText;
        var cell = (CopyableTextBlock)Text(timeText, "wordPresentationNumber");
        cell.Classes.Add("wordPresentationTime");
        cell.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(cell, timeText);
        ClearTipPlacement.SetIsEnabled(cell, true);
        return cell;
    }

    private static Control UnreadCell(WordRowViewModel facts)
    {
        var cell = Text(facts.UnreadText, "wordPresentationUnread");
        AutomationProperties.SetName(cell, facts.UnreadText);
        ToolTip.SetTip(cell, facts.UnreadText);
        return cell;
    }

    private Control TickCell(WordPresentation data)
    {
        var tick = new CheckBox
        {
            IsChecked = State?.IsChecked,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Classes = { "wordPresentationTick" },
        };
        AutomationProperties.SetName(tick, $"Tick {data.Facts.Word}");
        _tick = tick;
        tick.IsCheckedChanged += (_, _) =>
        {
            if (State is { } state) SetCurrentValue(StateProperty, state with { IsChecked = tick.IsChecked });
        };
        return tick;
    }

    private Control NextCell(WordPresentation data)
    {
        var host = new Grid { Classes = { "wordPresentationNextHost" } };
        var next = new WrapPanel
        {
            Classes = { "wordPresentationNext" },
        };
        next.Children.Add(NextAction(data, "Open in text", WordAction.OpenInText));
        next.Children.Add(NextAction(data, "Try a Word", WordAction.TryWord));
        next.Children.Add(NextAction(data, data.Facts.WordAnalysesLabel, WordAction.OpenAnalyses,
            data.Facts.HasWordAnalysesLink));
        host.Children.Add(next);
        return host;
    }

    private Button NextAction(WordPresentation data, string label, WordAction action, bool enabled = true)
    {
        var button = ActionButton(data, label, action, enabled);
        button.Classes.Add("wordPresentationNextAction");
        return button;
    }

    private Button ActionButton(
        WordPresentation data,
        string label,
        WordAction action,
        bool enabled = true,
        string? automationName = null)
    {
        Button button = action == WordAction.OpenAnalyses
            ? new HyperlinkButton { Content = label, NavigateUri = data.Facts.WordAnalysesLink }
            : new Button { Content = label };
        button.IsEnabled = enabled;
        button.Classes.Add("wordPresentationAction");
        AutomationProperties.SetName(button, automationName ?? action switch
        {
            WordAction.OpenInText => data.Facts.OpenInTextAutomationName,
            WordAction.TryWord => $"Try {data.Facts.Word} in Try a Word",
            WordAction.UndoChange => $"Undo: {data.Facts.Word}",
            WordAction.ReconfirmChange => $"Check again: {data.Facts.Word}",
            _ => data.Facts.WordAnalysesName,
        });
        if (action == WordAction.OpenAnalyses)
        {
            ToolTip.SetTip(button, data.Facts.WordAnalysesTip);
            ToolTip.SetShowOnDisabled(button, true);
            ClearTipPlacement.SetIsEnabled(button, true);
        }
        if (action != WordAction.OpenAnalyses)
            button.Click += async (_, _) => await SendAsync(new WordRequest(data.Key, data.EvidenceRevision, action));
        return button;
    }

    private static Control Text(string? text, string style) => new CopyableTextBlock
        { Text = text ?? string.Empty, Classes = { style } };

    private static CopyableTextBlock LanguageText(string text, string? writingSystem, string style)
    {
        var control = new CopyableTextBlock { Text = text, Classes = { style } };
        WritingSystemText.SetId(control, writingSystem);
        WritingSystemText.SetStyleName(control, "Normal");
        return control;
    }

    private static ProgressiveItemsControl PendingSentence(IReadOnlyList<WordCardSentenceToken> words)
    {
        var items = new ProgressiveItemsControl
        {
            Name = "WordRowPendingAfterWordsItems",
            FullItemsSource = words,
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }),
            ItemTemplate = new FuncDataTemplate<WordCardSentenceToken>((word, _) =>
            {
                if (word is null) return new Panel();
                var token = new Border
                {
                    Classes = { "reviewSentenceToken" },
                    Child = LanguageText(word.Form, word.WritingSystem, "reviewSmall"),
                };
                token.Classes.Set("changed", word.IsChanged);
                return token;
            }),
        };
        AutomationProperties.SetName(items, "Current sentence");
        return items;
    }

    private async void StartCardRead()
    {
        if (Data is not { } data || Host is not { } host || State?.IsOpen != true || !IsEffectivelyVisible) return;
        CancelRead();
        var cancellation = new CancellationTokenSource();
        _readCancellation = cancellation;
        _card = new WordCard { Classes = { "wordPresentationCard" }, Failure = "Loading word details…" };
        _card.RetryRequested += (_, _) => StartCardRead();
        _card.ActionRequested += action => _ = SendAsync(
            new WordRequest(data.Key, data.EvidenceRevision, action));
        SyncState();
        try
        {
            var result = await host.ReadCardAsync(data.Key, data.EvidenceRevision, cancellation.Token);
            if (cancellation.IsCancellationRequested || Data?.Key != data.Key ||
                Data?.EvidenceRevision != data.EvidenceRevision || State?.IsOpen != true) return;
            if (result.Document is { } document &&
                (document.Key != data.Key || document.EvidenceRevision != data.EvidenceRevision)) return;
            _card.Document = result.Document;
            _card.Failure = result.Failure;
            _card.CanRetry = result.CanRetry;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            if (cancellation.IsCancellationRequested) return;
            _card.Failure = failure.Message;
            _card.CanRetry = true;
        }
    }

    private void RefreshCardHost()
    {
        if (State?.IsOpen == true) StartCardRead();
    }

    private void CancelRead()
    {
        _readCancellation?.Cancel();
        _readCancellation?.Dispose();
        _readCancellation = null;
        _card = null;
    }

    private async Task SendAsync(WordRequest request)
    {
        if (Host is { } host)
        {
            var result = await host.HandleAsync(request, CancellationToken.None);
            if (result.StatusMessage is { Length: > 0 } message)
                KeyboardShortcutStatus.Announce(this, message);
        }
    }

    private void OnOverflowRequested(object? sender, MorphemeOverflowRequestedEventArgs e)
    {
        if (Data is not { } data) return;
        SetCurrentValue(StateProperty, (State ?? new WordInteractionState(data.Key)) with { IsOpen = true });
        _ = SendAsync(new WordRequest(data.Key, data.EvidenceRevision, WordAction.OpenMorphology,
            Producer: e.Request.Producer));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var nested = e.Source is Control source && source.GetSelfAndVisualAncestors()
            .TakeWhile(control => !ReferenceEquals(control, this))
            .Any(control => control is Button or ToggleButton or CheckBox or TextBox or ComboBox);
        _pressedAt = point.Properties.IsLeftButtonPressed && e.ClickCount == 1 && !nested
            ? point.Position : null;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedAt is not { } start) return;
        _pressedAt = null;
        var moved = e.GetPosition(this) - start;
        if (Math.Abs(moved.X) > 4 || Math.Abs(moved.Y) > 4 ||
            e.Source is SelectableTextBlock { SelectedText.Length: > 0 }) return;
        FocusWord();
        ToggleOpen();
    }

    [KeyboardShortcutHandler(
        "WordList:OpenWordCard", "WordList:CloseWordCard", "WordList:PreviousRow", "WordList:NextRow",
        "WordList:PreviousScreen", "WordList:NextScreen", "WordList:FirstItem", "WordList:LastItem",
        "WordList:Approve", "WordList:Disapprove", "WordList:Unknown",
        "Lists:OpenWordCard", "Lists:CloseWordCard", "Lists:PreviousRow", "Lists:NextRow",
        "Lists:PreviousScreen", "Lists:NextScreen", "Lists:FirstItem", "Lists:LastItem",
        "Lists:Approve", "Lists:Disapprove", "Lists:Unknown",
        "Matrix:OpenWordCard", "Matrix:CloseWordCard", "Matrix:PreviousRow", "Matrix:NextRow",
        "Matrix:PreviousScreen", "Matrix:NextScreen", "Matrix:FirstItem", "Matrix:LastItem",
        "ReviewChanges:OpenWordCard", "ReviewChanges:CloseWordCard", "ReviewChanges:PreviousRow",
        "ReviewChanges:NextRow", "ReviewChanges:PreviousScreen", "ReviewChanges:NextScreen",
        "ReviewChanges:FirstItem", "ReviewChanges:LastItem")]
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Data is not { } data || State is null) return;
        var scope = data.Owner switch
        {
            WordListOwner.WordList => KeyboardShortcutScope.WordList,
            WordListOwner.Lists => KeyboardShortcutScope.Lists,
            WordListOwner.Matrix or WordListOwner.FixFirst => KeyboardShortcutScope.Matrix,
            WordListOwner.Review => KeyboardShortcutScope.ReviewChanges,
            _ => (KeyboardShortcutScope?)null,
        };
        if (scope is null) return;
        var opinionShortcut = scope is { } shortcutScope
            ? KeyboardShortcutRegistry.Find(shortcutScope, e.Key, e.KeyModifiers,
                targetBehaviors: [KeyboardShortcutBehavior.Approve, KeyboardShortcutBehavior.Disapprove,
                    KeyboardShortcutBehavior.Unknown])
            : null;
        if (opinionShortcut is { Behavior: KeyboardShortcutBehavior.Approve or
                KeyboardShortcutBehavior.Disapprove or KeyboardShortcutBehavior.Unknown } &&
            KeyboardShortcutRegistry.Allows(opinionShortcut, KeyboardShortcutRegistry.IsTextInput(e.Source),
                hasFocusedItem: true))
        {
            var opinion = opinionShortcut.Behavior switch
            {
                KeyboardShortcutBehavior.Approve => OpinionMarkKind.Approved,
                KeyboardShortcutBehavior.Disapprove => OpinionMarkKind.Disapproved,
                _ => OpinionMarkKind.Unknown,
            };
            _ = SendAsync(new WordRequest(data.Key, data.EvidenceRevision, WordAction.SetOpinion, opinion));
            e.Handled = true;
            return;
        }
        var shortcut = KeyboardShortcutRegistry.Find(scope.Value, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.OpenWordCard, KeyboardShortcutBehavior.CloseWordCard,
                KeyboardShortcutBehavior.PreviousRow, KeyboardShortcutBehavior.NextRow,
                KeyboardShortcutBehavior.PreviousScreen, KeyboardShortcutBehavior.NextScreen,
                KeyboardShortcutBehavior.FirstItem, KeyboardShortcutBehavior.LastItem]);
        if (shortcut is null || !KeyboardShortcutRegistry.Allows(shortcut,
                KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: true)) return;
        if (e.Source is Control source && source.GetSelfAndVisualAncestors()
                .TakeWhile(control => !ReferenceEquals(control, this))
                .Any(control => control is Button or CheckBox or TextBox or ComboBox or ToggleButton)) return;
        if (shortcut.Behavior == KeyboardShortcutBehavior.CloseWordCard)
        {
            if (!State.IsOpen) return;
            SetCurrentValue(StateProperty, State with { IsOpen = false });
            FocusWord();
        }
        else if (shortcut.Behavior == KeyboardShortcutBehavior.OpenWordCard)
            ToggleOpen();
        else
        {
            var action = shortcut.Behavior switch
            {
                KeyboardShortcutBehavior.PreviousRow => WordAction.NavigatePrevious,
                KeyboardShortcutBehavior.NextRow => WordAction.NavigateNext,
                KeyboardShortcutBehavior.PreviousScreen => WordAction.NavigatePreviousPage,
                KeyboardShortcutBehavior.NextScreen => WordAction.NavigateNextPage,
                KeyboardShortcutBehavior.FirstItem => WordAction.NavigateFirst,
                _ => WordAction.NavigateLast,
            };
            _ = SendAsync(new WordRequest(data.Key, data.EvidenceRevision, action));
        }
        e.Handled = true;
    }

    private void ToggleOpen()
    {
        if (Data is not { } data) return;
        var state = State ?? new WordInteractionState(data.Key);
        SetCurrentValue(StateProperty, state with { IsOpen = !state.IsOpen });
        _ = SendAsync(new WordRequest(data.Key, data.EvidenceRevision,
            state.IsOpen ? WordAction.CloseCard : WordAction.OpenCard));
    }

    private static string AutomationList(WordListOwner owner) => owner switch
    {
        WordListOwner.WordList => "word-list",
        WordListOwner.Lists => "lists",
        WordListOwner.Matrix => "matrix",
        WordListOwner.FixFirst => "fix-first",
        WordListOwner.Review => "review",
        WordListOwner.Timing => "timing-rule-words",
        WordListOwner.WhatChanged => "what-changed",
        WordListOwner.Warnings => "warnings",
        _ => owner.ToString().ToLowerInvariant(),
    };

    private static IReadOnlyList<WordListColumn> DefaultColumns() =>
        WordListSchema.Create(WordListColumnSet.All).OrderedColumns();
}
