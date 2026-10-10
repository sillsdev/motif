using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>Renders typed evidence sections after a word row opens.</summary>
public sealed partial class WordCard : UserControl
{
    public static readonly StyledProperty<WordCardDocument?> DocumentProperty =
        AvaloniaProperty.Register<WordCard, WordCardDocument?>(nameof(Document));

    public static readonly StyledProperty<Control?> HeaderProperty =
        AvaloniaProperty.Register<WordCard, Control?>(nameof(Header));

    public static readonly StyledProperty<string?> FailureProperty =
        AvaloniaProperty.Register<WordCard, string?>(nameof(Failure));

    public static readonly StyledProperty<bool> CanRetryProperty =
        AvaloniaProperty.Register<WordCard, bool>(nameof(CanRetry));

    public static readonly StyledProperty<IWordPresentationHost?> HostProperty =
        AvaloniaProperty.Register<WordCard, IWordPresentationHost?>(nameof(Host));

    public static readonly StyledProperty<WordPresentationKey?> KeyProperty =
        AvaloniaProperty.Register<WordCard, WordPresentationKey?>(nameof(Key));

    public static readonly StyledProperty<long> EvidenceRevisionProperty =
        AvaloniaProperty.Register<WordCard, long>(nameof(EvidenceRevision));

    public static readonly StyledProperty<bool> HandlesTextReaderShortcutsProperty =
        AvaloniaProperty.Register<WordCard, bool>(nameof(HandlesTextReaderShortcuts));

    private readonly StackPanel _sections;
    private CancellationTokenSource? _readCancellation;

    public WordCard()
    {
        AvaloniaXamlLoader.Load(this);
        _sections = this.FindControl<StackPanel>("Sections") ??
            throw new InvalidOperationException("A word card needs its section host.");
        Focusable = true;
        Classes.Add("wordPresentationCard");
        AddHandler(Button.ClickEvent, OnActionButtonClick, RoutingStrategies.Bubble);
        KeyDown += OnKeyDown;
    }

    /// <summary>The identity-matched evidence to show.</summary>
    public WordCardDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>The optional host-specific heading displayed above the evidence sections.</summary>
    public Control? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>The host's visible read failure, if the evidence could not be loaded.</summary>
    public string? Failure
    {
        get => GetValue(FailureProperty);
        set => SetValue(FailureProperty, value);
    }

    /// <summary>Whether the host can retry a failed evidence read.</summary>
    public bool CanRetry
    {
        get => GetValue(CanRetryProperty);
        set => SetValue(CanRetryProperty, value);
    }

    /// <summary>The list-owned reader and action host for this card.</summary>
    public IWordPresentationHost? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    /// <summary>The stable word identity whose evidence this card requests.</summary>
    public WordPresentationKey? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    /// <summary>The evidence version this card requests from its host.</summary>
    public long EvidenceRevision
    {
        get => GetValue(EvidenceRevisionProperty);
        set => SetValue(EvidenceRevisionProperty, value);
    }

    /// <summary>Whether keyboard shortcuts from the Text reader apply to this card.</summary>
    public bool HandlesTextReaderShortcuts
    {
        get => GetValue(HandlesTextReaderShortcutsProperty);
        set => SetValue(HandlesTextReaderShortcutsProperty, value);
    }

    public event EventHandler? RetryRequested;

    /// <summary>Raises a typed request for an action shown in this card.</summary>
    public event Action<WordAction>? ActionRequested;

    static WordCard()
    {
        DocumentProperty.Changed.AddClassHandler<WordCard>((card, _) =>
        {
            card.Build();
            card.LoadCardTiming();
        });
        HeaderProperty.Changed.AddClassHandler<WordCard>((card, _) => card.Build());
        FailureProperty.Changed.AddClassHandler<WordCard>((card, _) => card.Build());
        CanRetryProperty.Changed.AddClassHandler<WordCard>((card, _) => card.Build());
        HostProperty.Changed.AddClassHandler<WordCard>((card, _) => card.StartHostedRead());
        KeyProperty.Changed.AddClassHandler<WordCard>((card, _) => card.StartHostedRead());
        EvidenceRevisionProperty.Changed.AddClassHandler<WordCard>((card, _) => card.StartHostedRead());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LoadCardTiming();
        StartHostedRead();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelHostedRead();
        if (Host is not null) Document = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Build()
    {
        // Removed section presenters can outlive a frame; release their evidence before recycling them.
        foreach (var section in _sections.Children.OfType<ContentControl>())
        {
            section.Content = null;
            section.DataContext = null;
        }
        _sections.Children.Clear();
        if (Header is { } header) _sections.Children.Add(header);
        if (Document is { } document)
        {
            foreach (var section in document.Sections)
            {
                switch (section)
                {
                    case WordCardHeading heading:
                        _sections.Children.Add(new CopyableTextBlock
                            { Text = heading.Text, Classes = { "wordPresentationCardHeading" } });
                        break;
                    case WordCardText text:
                        AddTextSection(_sections, text);
                        break;
                    case WordCardMorphology morphology:
                        _sections.Children.Add(new CopyableTextBlock
                            { Text = morphology.Heading, Classes = { "wordPresentationCardHeading" } });
                        _sections.Children.Add(new MorphemePanel
                            { Morphs = morphology.Morphs, Annotations = morphology.Annotations, ShowCategory = true });
                        break;
                    case WordCardAnalysis analysis:
                        var template = Resources["WordCardAnalysisTemplate"] as IDataTemplate ??
                            throw new InvalidOperationException("A word card needs its typed analysis template.");
                        _sections.Children.Add(new ContentControl { Content = analysis, ContentTemplate = template });
                        break;
                    case WordCardOccurrences occurrences:
                        var occurrenceTemplate = Resources["WordCardOccurrencesTemplate"] as IDataTemplate ??
                            throw new InvalidOperationException("A word card needs its typed occurrence template.");
                        _sections.Children.Add(new ContentControl
                            { Content = occurrences, ContentTemplate = occurrenceTemplate });
                        break;
                    case WordCardPendingChange pending:
                        var pendingTemplate = Resources["WordCardPendingChangeTemplate"] as IDataTemplate ??
                            throw new InvalidOperationException("A word card needs its typed pending-change template.");
                        _sections.Children.Add(new ContentControl
                            { Content = pending, ContentTemplate = pendingTemplate });
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown word-card section: {section.GetType().Name}.");
                }
            }
        }
        if (Failure is { Length: > 0 } failure)
        {
            var message = new CopyableTextBlock { Text = failure, Classes = { "wordPresentationCardFailure" } };
            AutomationProperties.SetName(message, failure);
            _sections.Children.Add(message);
            if (CanRetry)
            {
                var retry = new Button { Content = "Try again", Classes = { "wordPresentationAction" } };
                AutomationProperties.SetName(retry, "Try loading word details again");
                retry.Click += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);
                retry.Click += (_, _) => StartHostedRead();
                _sections.Children.Add(retry);
            }
        }
    }

    private void LoadCardTiming()
    {
        if (TopLevel.GetTopLevel(this) is null) return;
        var token = Document?.Sections.OfType<WordCardAnalysis>().Select(section => section.Token)
            .FirstOrDefault(item => item is not null);
        if (token?.Actions is { } actions) _ = actions.LoadCardTimingAsync(token);
    }

    private async void OnActionButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: WordAction action } || Host is not { } host || Key is not { } key) return;
        ActionRequested?.Invoke(action);
        await SendAsync(host, key, action);
    }

    /// <summary>Moves keyboard focus to the card's semantic target.</summary>
    public void FocusCard() => Focus(NavigationMethod.Directional);

    [KeyboardShortcutHandler(
        "TextReader:CloseWordCard", "TextReader:PreviousWord", "TextReader:NextWord", "TextReader:PreviousLine",
        "TextReader:NextLine", "TextReader:PreviousScreen", "TextReader:NextScreen", "TextReader:FirstItem",
        "TextReader:LastItem", "TextReader:Approve", "TextReader:Disapprove", "TextReader:Unknown")]
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!HandlesTextReaderShortcuts || Host is not { } host || Key is not { } key ||
            KeyboardShortcutRegistry.IsTextInput(e.Source)) return;
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.CloseWordCard]) ??
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers);
        if (entry is null || !KeyboardShortcutRegistry.Allows(entry, isTextInput: false, hasFocusedItem: true)) return;
        if (entry.Behavior == KeyboardShortcutBehavior.CloseWordCard)
        {
            e.Handled = true;
            await SendAsync(host, key, WordAction.CloseCard);
            return;
        }
        if (e.Source is Control source && !ReferenceEquals(source, this)) return;
        var action = entry.Behavior switch
        {
            KeyboardShortcutBehavior.PreviousWord => WordAction.NavigateCardPrevious,
            KeyboardShortcutBehavior.NextWord => WordAction.NavigateCardNext,
            KeyboardShortcutBehavior.PreviousLine => WordAction.NavigatePreviousLine,
            KeyboardShortcutBehavior.NextLine => WordAction.NavigateNextLine,
            KeyboardShortcutBehavior.PreviousScreen => WordAction.NavigatePreviousPage,
            KeyboardShortcutBehavior.NextScreen => WordAction.NavigateNextPage,
            KeyboardShortcutBehavior.FirstItem => WordAction.NavigateFirst,
            KeyboardShortcutBehavior.LastItem => WordAction.NavigateLast,
            KeyboardShortcutBehavior.Approve => WordAction.SetOpinion,
            KeyboardShortcutBehavior.Disapprove => WordAction.SetOpinion,
            KeyboardShortcutBehavior.Unknown => WordAction.SetOpinion,
            _ => (WordAction?)null,
        };
        if (action is not { } requestAction) return;
        e.Handled = true;
        var opinion = entry.Behavior switch
        {
            KeyboardShortcutBehavior.Approve => OpinionMarkKind.Approved,
            KeyboardShortcutBehavior.Disapprove => OpinionMarkKind.Disapproved,
            KeyboardShortcutBehavior.Unknown => OpinionMarkKind.Unknown,
            _ => (OpinionMarkKind?)null,
        };
            var result = await host.HandleAsync(
                new WordRequest(key, EvidenceRevision, requestAction, opinion), CancellationToken.None);
        if (result.StatusMessage is { Length: > 0 } message) KeyboardShortcutStatus.Announce(this, message);
    }

    private async Task SendAsync(IWordPresentationHost host, WordPresentationKey key, WordAction action)
    {
        var result = await host.HandleAsync(new WordRequest(key, EvidenceRevision, action), CancellationToken.None);
        if (result.StatusMessage is { Length: > 0 } message) KeyboardShortcutStatus.Announce(this, message);
    }

    private async void StartHostedRead()
    {
        if (Host is not { } host || Key is not { } key || TopLevel.GetTopLevel(this) is null) return;
        CancelHostedRead();
        var cancellation = new CancellationTokenSource();
        _readCancellation = cancellation;
        Document = null;
        Failure = "Loading word details…";
        CanRetry = false;
        try
        {
            var result = await host.ReadCardAsync(key, EvidenceRevision, cancellation.Token);
            if (cancellation.IsCancellationRequested || Key != key) return;
            if (result.Document is { } document &&
                (document.Key != key || document.EvidenceRevision != EvidenceRevision)) return;
            Document = result.Document;
            Failure = result.Failure;
            CanRetry = result.CanRetry;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            if (cancellation.IsCancellationRequested) return;
            Failure = failure.Message;
            CanRetry = true;
        }
    }

    private void CancelHostedRead()
    {
        _readCancellation?.Cancel();
        _readCancellation?.Dispose();
        _readCancellation = null;
    }

    private static void AddTextSection(Panel sections, WordCardText section)
    {
        if (!string.IsNullOrWhiteSpace(section.Heading))
            sections.Children.Add(new CopyableTextBlock
                { Text = section.Heading, Classes = { "wordPresentationCardHeading" } });
        var text = new CopyableTextBlock
        {
            Text = section.Text,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Classes = { string.IsNullOrWhiteSpace(section.Heading) ? "muted" : "wordPresentationCardText" },
        };
        WritingSystemText.SetId(text, section.WritingSystem);
        sections.Children.Add(text);
    }

}
