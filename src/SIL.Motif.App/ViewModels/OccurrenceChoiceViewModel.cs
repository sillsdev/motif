using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>One place a word occurs, with the check box that includes it in a keep.</summary>
public sealed partial class OccurrenceChoiceViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    public OccurrenceChoiceViewModel(OccurrenceAnchor anchor, string textTitle, int lineNumber, string sentence,
        string? status, string? chosenAnalysisId, ExpectedContext? expectedContext = null)
    {
        Anchor = anchor;
        TextTitle = textTitle;
        LineNumber = lineNumber;
        Sentence = sentence;
        State = PassageWordStates.From(status);
        ChosenAnalysisId = chosenAnalysisId;
        ExpectedContext = expectedContext;
    }

    /// <summary>The FieldWorks segment position; it is not a Paratext verse identity.</summary>
    public OccurrenceAnchor Anchor { get; }
    public string TextTitle { get; }

    /// <summary>The writing system of <see cref="Sentence"/>, so its font and direction are the project's.</summary>
    public string? SentenceWritingSystem { get; init; }

    /// <summary>The named style of <see cref="Sentence"/>.</summary>
    public string SentenceStyle { get; init; } = "Normal";

    /// <summary>The place as a person reads it: the Text and the line.</summary>
    public string PlaceLabel => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        MotifAppStrings.OccurrencesPlaceFormat, TextTitle, LineNumber);

    /// <summary>The one word that names this place's state: Staged, Approved or empty.</summary>
    public string StateLabel => IsStaged ? MotifAppStrings.OccurrencesPlaceStaged :
        IsApproved ? MotifAppStrings.OccurrencesPlaceApproved : string.Empty;

    /// <summary>A stable id for this place's check box, from its segment position.</summary>
    public string PlaceAutomationId => AutomationIds.ForOccurrencePlace(Anchor);

    public bool HasStateLabel => StateLabel.Length > 0;

    /// <summary>Whether the check box can change: an approved or staged place has nothing more to keep.</summary>
    public bool CanCheck => !IsApproved && !IsStaged;
    public int LineNumber { get; }
    public string Sentence { get; }
    public PassageWordState State { get; private set; }
    public string? ChosenAnalysisId { get; }
    public ExpectedContext? ExpectedContext { get; }

    /// <summary>Whether this place is already approved, so a keep has nothing to write here.</summary>
    public bool IsApproved => State == PassageWordState.Approved;

    /// <summary>Whether a keep for this place is staged and waiting for Apply; it is not yet approved.</summary>
    public bool IsStaged { get; private set; }

    /// <summary>Marks the place staged after its change was really written; Apply is what approves it.</summary>
    internal void MarkStaged()
    {
        IsStaged = true;
        OnPropertyChanged(nameof(IsStaged));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(HasStateLabel));
        OnPropertyChanged(nameof(CanCheck));
    }
}
