using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Controls.WordPresentation;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One word a page lists beside its own content, such as Timing's costliest words or a change waiting in Review
/// changes: the word's row, the same as on every page, and the card it opens in place.
/// </summary>
public sealed partial class ListedWordViewModel : ObservableObject
{
    private readonly WordPresentationKey _presentationKey;

    public ListedWordViewModel(WordRowViewModel row, CompareWordViewModel? card = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        Row = row;
        Card = card;
        _presentationKey = card?.PresentationFor(WordListOwner.Timing).Key ??
            new WordPresentationKey($"listed-word:{Guid.NewGuid():N}");
    }

    /// <summary>The word with its parse in the window: its row, and its card from the same parse.</summary>
    /// <param name="word">The word as the latest Parse all words left it.</param>
    /// <param name="routes">Where the row's next steps lead, when this page sends them elsewhere than the word's own.</param>
    public static ListedWordViewModel Of(AssessWordRowViewModel word, WordRowRoutes? routes = null,
        Func<SIL.Motif.Contract.Responses.AssessmentWordResult, ResultsTokenViewModel?>? cardTokenFactory = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        var row = routes is null ? word.WordRow : new WordRowViewModel(word.WordRow.Row, routes);
        return new ListedWordViewModel(row, new CompareWordViewModel(word, CompareViewModel.Place(word),
            cardTokenFactory?.Invoke(word.Source)));
    }

    public string Word => Row.Word;

    /// <summary>The module input for this word in Timing.</summary>
    public WordPresentation TimingPresentation => PresentationFor(WordListOwner.Timing);

    /// <summary>The open state retained while this listed word is virtualized.</summary>
    public WordInteractionState PresentationState
    {
        get => new(_presentationKey, IsOpen);
        set
        {
            if (value.Key != _presentationKey)
                throw new InvalidOperationException("Word interaction state belongs to another listed word.");
            IsOpen = value.IsOpen;
        }
    }

    /// <summary>Creates the typed row input for the page that lists this word.</summary>
    public WordPresentation PresentationFor(WordListOwner owner, string? note = null) => new(
        _presentationKey, Card?.EvidenceRevision ?? 0, Row, owner,
        Note: note ?? Note, TimeText: TimeText, MeasuredText: Row.MeasuredText);

    /// <summary>The word's row: its marks, its morphemes and the three next steps.</summary>
    public WordRowViewModel Row { get; }

    /// <summary>What the opened row shows, or <see langword="null"/> when the latest parse did not reach the word.</summary>
    public CompareWordViewModel? Card { get; }

    public ResultsTokenViewModel? CardToken => Card?.CardToken;

    public bool HasCard => Card is not null;

    /// <summary>What the opened row says when there is no parse to show.</summary>
    public string NotParsedText => $"No parse of {Word} is on screen yet. Parse all words to see what PanGloss builds.";

    /// <summary>Whether the row's card is open.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The page's own measure of the word's time, in place of its parse time; <see langword="null"/> for that.</summary>
    public string? TimeText { get; set; }

    /// <summary>One line the page adds under the row, or <see langword="null"/> for none.</summary>
    public string? Note { get; set; }
}
