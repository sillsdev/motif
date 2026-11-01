using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDifferenceViewModel
{
    /// <summary>The run that makes this Proposal's Difference for the chosen Text.</summary>
    public ProposalDifferenceMaker Maker { get; }

    /// <summary>The Texts a Difference can compare.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersMake), nameof(ShowsMakeControls), nameof(ShowsNoTextsNote))]
    private IReadOnlyList<DifferenceTextChoice> _textChoices = [];

    /// <summary>The Text the next Difference compares.</summary>
    [ObservableProperty]
    private DifferenceTextChoice? _selectedText;

    partial void OnSelectedTextChanged(DifferenceTextChoice? value)
    {
        OnPropertyChanged(nameof(MakeLabel));
        Maker.Refresh();
    }

    /// <summary>Whether a Difference can be made now: offered, not decided, and not being read.</summary>
    public bool OffersMake => _offer is DifferenceOffer.Available or DifferenceOffer.Deferred &&
        State is ProposalDifferenceState.Absent or ProposalDifferenceState.Shown;

    /// <summary>Whether the section says the words are gone and making the Difference again brings them back.</summary>
    public bool ShowsRemakeHint => IsSummaryOnly && OffersMake;

    /// <summary>Whether the Text list and the Make Difference button are shown.</summary>
    public bool ShowsMakeControls => OffersMake && TextChoices.Count > 0;

    /// <summary>Whether the line explaining the button is shown; a Proposal that has a Difference needs no lesson.</summary>
    public bool ShowsMakeAdvice => State == ProposalDifferenceState.Absent;

    /// <summary>Whether the Baseline lists no Text to compare.</summary>
    public bool ShowsNoTextsNote => OffersMake && TextChoices.Count == 0;

    /// <summary>Whether the note that the Proposal was never tried is shown instead of the button.</summary>
    public bool ShowsNotTriedNote => State == ProposalDifferenceState.Absent && _offer == DifferenceOffer.NotTried;

    /// <summary>Whether the note that a decided Proposal can no longer have a Difference is shown.</summary>
    public bool ShowsDecidedNote => State == ProposalDifferenceState.Absent && _offer == DifferenceOffer.Decided;

    /// <summary>The button's words: making it again only while the Proposal has a Difference for the chosen Text made against people's decisions.</summary>
    public string MakeLabel => State == ProposalDifferenceState.Shown && SavedForChosenText()
        ? MotifAppStrings.ProposalDifferenceMakeAgain : MotifAppStrings.ProposalDifferenceMake;

    /// <summary>
    /// Takes the offer the Proposal's current card makes, as when a Trial ended, without touching a run in progress
    /// or the Difference shown.
    /// </summary>
    /// <param name="offer">Whether a Difference can be made for the Proposal now.</param>
    public void SetOffer(DifferenceOffer offer)
    {
        if (_offer == offer) return;
        _offer = offer;
        RaiseMake();
    }

    /// <summary>
    /// Whether <paramref name="offer"/> moves the Proposal into, out of or between states that keep no word detail, as
    /// deferred to decided does when the words made again are dropped.
    /// </summary>
    /// <param name="offer">The offer the Proposal's card now makes.</param>
    public bool IsDecidedChangedBy(DifferenceOffer offer) =>
        _offer != offer && (KeepsNoWords(_offer) || KeepsNoWords(offer));

    private static bool KeepsNoWords(DifferenceOffer offer) => offer is DifferenceOffer.Decided or DifferenceOffer.Deferred;

    /// <summary>Cancels a run in progress and returns once it has ended, so no command outlives the project.</summary>
    public Task StopAsync() => Maker.DisposeAsync().AsTask();

    /// <summary>Reads the Texts the Baseline lists now, keeping the chosen one when it is still there.</summary>
    public void RefreshTexts()
    {
        var choices = _textSource();
        if (!choices.SequenceEqual(TextChoices)) TextChoices = choices;
        if (SelectedText is { } chosen && choices.Contains(chosen)) return;
        SelectedText = ChoiceOfSummary(choices) ?? choices.FirstOrDefault();
    }

    // The window makes a Difference against people's decisions only, so only a saved one of those is made again.
    private bool SavedForChosenText() => SelectedText is { } chosen &&
        SavedChoices.Any(saved => saved.Reference == ProposalDifferenceReferences.Human &&
            string.Equals(saved.TextId, chosen.Id.ToString("D"), StringComparison.Ordinal));

    private bool ShownTextIsChosen() => SelectedText is { } chosen && ChoiceOfSummary(TextChoices) == chosen;

    private DifferenceTextChoice? ChoiceOfSummary(IReadOnlyList<DifferenceTextChoice> choices) =>
        Summary is { } summary && Guid.TryParse(summary.TextId, out var id)
            ? choices.FirstOrDefault(choice => choice.Id == id) : null;

    private ProposalDifferenceProduceRequest? Describe() =>
        OffersMake && _projectPath is { } project && _proposalId is { } proposal && SelectedText is { } text
            ? new ProposalDifferenceProduceRequest(project, proposal, text.Id.ToString("D")) : null;

    // The made Difference replaces what the section showed, with filters and the open word cleared.
    private void AdoptMade(ProposalDifferenceResponse response)
    {
        Reset();
        Adopt(response);
    }

    private void RaiseMake()
    {
        OnPropertyChanged(nameof(MakeLabel));
        OnPropertyChanged(nameof(OffersMake));
        OnPropertyChanged(nameof(ShowsMakeControls));
        OnPropertyChanged(nameof(ShowsNoTextsNote));
        OnPropertyChanged(nameof(ShowsMakeAdvice));
        OnPropertyChanged(nameof(ShowsNotTriedNote));
        OnPropertyChanged(nameof(ShowsDecidedNote));
        OnPropertyChanged(nameof(ShowsRemakeHint));
        OnPropertyChanged(nameof(SavedNoteText));
        Maker.Refresh();
    }

    private static Task<CommandOutcome<ProposalDifferenceResponse>> NotAvailable(
        ProposalDifferenceProduceRequest request, IProgress<AssessmentProgress> progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
            RefusalCodes.WindowDifferenceMakeFailed, FailureReason.Refused, "Making a Difference is not available.")));
}
