using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>A listed mechanical effect, without an inferred occurrence-level approval decision.</summary>
public sealed record ProposalEvidenceWord(string Word, string Effect, IRelayCommand ShowPlacesCommand, string? WritingSystemId = null);

/// <summary>Projects complete mechanical observations, preserving unavailable totals and Text scope.</summary>
public sealed class ProposalEvidenceColumnsViewModel : ObservableObject
{
    private readonly TextStyles? _textStyles;
    private bool _current;
    private ProposalDetailProjection? _detail;
    private string? _trialJobId;
    private BaselineToken? _baseline;

    public ProposalEvidenceColumnsViewModel(ProposalDifferenceViewModel difference, TextStyles? textStyles = null)
    {
        Difference = difference;
        _textStyles = textStyles;
        Difference.PropertyChanged += OnDifferenceChanged;
    }

    public ProposalDifferenceViewModel Difference { get; }
    public IReadOnlyList<ProposalEvidenceWord> WillFillIn { get; private set; } = [];
    public IReadOnlyList<ProposalEvidenceWord> WillChange { get; private set; } = [];
    public bool HasFillIn => CanShowFacts && (WillFillIn.Count > 0 || Complete);
    public bool HasChange => CanShowFacts && (WillChange.Count > 0 || Complete);
    public bool HasApprovedPlaces => false;
    public string ApprovedPlacesNotice => MotifAppStrings.ProposalEvidencePlacesUnavailable;
    public bool CanShowFacts => _current && Difference is { IsShown: true, WordDetailKept: true, WordDetailUnreadable: false } &&
        Difference.Summary is { } summary && Compatible(summary);
    public bool Complete => CanShowFacts && Difference.Summary is { } summary &&
        summary.UnboundOccurrences == 0 && summary.Limitations.Count == 0 &&
        summary.ListedWordCount == summary.Words && Difference.EvidenceWords.Count == summary.Words &&
        Difference.EvidenceWords.All(word => CompleteFacts(word.Facts) &&
            CompleteSpelling(word.Facts.Before) && CompleteSpelling(word.Facts.After));
    public string Scope => Difference.Summary is { } summary
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEvidenceScope, summary.TextTitle)
        : string.Empty;
    public bool HasScope => Scope.Length > 0;
    public string NotesText => string.Join(Environment.NewLine, Difference.Notes);
    public bool HasNotes => Difference.HasNotes;
    public string Availability => !CanShowFacts ? Unavailable() :
        Complete ? string.Empty : MotifAppStrings.ProposalEvidencePartial;

    // Said once; a Difference not made yet is named as such, not called unavailable.
    private string Unavailable() => Difference.State == ProposalDifferenceState.Absent && Difference.ShowsMakeControls
        ? MotifAppStrings.ProposalEvidenceNotMadeYet
        : Difference.ShowsNotTriedNote || Difference.ShowsMakeControls ||
        Difference.ShowsNoTextsNote || Difference.ShownRefusal is not null
        ? MotifAppStrings.ProposalEvidenceUnavailable
        : MotifAppStrings.ProposalEvidenceUnavailable + " " + MotifAppStrings.ProposalEvidenceUnavailableNextStep;
    public string FillInCount => Count(WillFillIn.Count);
    public string ChangeCount => Count(WillChange.Count);
    public bool ShowsZeroFillIn => HasFillIn && Complete && WillFillIn.Count == 0;
    public bool ShowsZeroChange => HasChange && Complete && WillChange.Count == 0;

    public void SetCurrent(bool current, ProposalDetailProjection? detail = null, string? trialJobId = null, BaselineToken? baseline = null)
    {
        _current = current;
        _detail = detail;
        _trialJobId = trialJobId;
        _baseline = baseline;
        Rebuild();
    }

    private bool Compatible(ProposalDifferenceSummary summary) =>
        (_detail is null || (summary.ProposalId == _detail.ProposalId &&
            !string.IsNullOrEmpty(_detail.CurrentIntentDigest) && !string.IsNullOrEmpty(_trialJobId) &&
            MatchesBaseline(summary.BaselineToken) &&
            summary.ProposalIntentDigest == _detail.CurrentIntentDigest)) &&
        (_trialJobId is null || summary.TrialJobId == _trialJobId) &&
        summary.Before.ParserSha256 is { Length: > 0 } parser && parser == summary.After.ParserSha256 &&
        summary.Before.ParserVersion == summary.After.ParserVersion &&
        summary.Before.SelectionSha256 == summary.After.SelectionSha256 &&
        summary.Before.TokeniserName == summary.After.TokeniserName &&
        summary.Before.TokeniserVersion == summary.After.TokeniserVersion &&
        summary.Before.GrammarExportFormat == summary.After.GrammarExportFormat;

    private bool MatchesBaseline(string json)
    {
        if (_baseline is null) return false;
        try
        {
            return JsonSerializer.Deserialize<BaselineToken>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.HasSameSemanticIdentity(_baseline) == true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException) { return false; }
    }

    private string Count(int count) => string.Format(CultureInfo.CurrentCulture,
        Complete ? MotifAppStrings.ProposalEvidenceCompleteCount : MotifAppStrings.ProposalEvidenceListedCount, count);

    private static bool CompleteFacts(ProposalDifferenceCaseFacts facts) => facts.CompleteComparison &&
        CompleteSide(facts.Before) && CompleteSide(facts.After);
    private static bool CompleteSide(HumanAgreementFacts side) =>
        side.Availability == HumanAgreementEvidenceAvailability.Available &&
        side.Completion == HumanAgreementSearchCompletion.Complete && side.Limitations.Count == 0;
    private static bool CompleteSpelling(HumanAgreementFacts side) => side.SpellingAcceptance is
        HumanAgreementSpellingAcceptance.NotApplicable or HumanAgreementSpellingAcceptance.AcceptedMisspelling or
        HumanAgreementSpellingAcceptance.RejectedMisspelling;

    private void Rebuild()
    {
        var words = CanShowFacts ? Difference.EvidenceWords.Where(word => CompleteFacts(word.Facts)) : [];
        WillFillIn = words.Where(word => word.Facts.Before.ProducedIdentityCount == 0 &&
                word.Facts.After.ProducedIdentityCount > 0)
            .Select(word => Row(word, MotifAppStrings.BoardWillFillIn)).ToArray();
        WillChange = words.Where(word => word.Facts.Before.ProducedIdentityCount > 0 &&
                word.Facts.After.ProducedIdentityCount > 0 &&
                (word.Facts.GainedIdentities.Count > 0 || word.Facts.LostIdentities.Count > 0) ||
                SpellingChanged(word.Facts))
            .Select(word => Row(word, SpellingChanged(word.Facts) ? MotifAppStrings.ProposalSpellingChanged :
                MotifAppStrings.ProposalReadingChanged)).ToArray();
        foreach (var property in new[] { nameof(WillFillIn), nameof(WillChange), nameof(HasFillIn), nameof(HasChange),
                     nameof(CanShowFacts), nameof(Complete), nameof(Scope), nameof(HasScope), nameof(Availability), nameof(NotesText), nameof(HasNotes), nameof(FillInCount),
                     nameof(ChangeCount), nameof(ShowsZeroFillIn), nameof(ShowsZeroChange) }) OnPropertyChanged(property);
    }

    private static bool SpellingChanged(ProposalDifferenceCaseFacts facts) =>
        facts.Before.SpellingAcceptance != facts.After.SpellingAcceptance &&
        facts.Before.SpellingAcceptance is HumanAgreementSpellingAcceptance.AcceptedMisspelling or
            HumanAgreementSpellingAcceptance.RejectedMisspelling &&
        facts.After.SpellingAcceptance is HumanAgreementSpellingAcceptance.AcceptedMisspelling or
            HumanAgreementSpellingAcceptance.RejectedMisspelling;

    private ProposalEvidenceWord Row(ProposalDifferenceWord word, string effect) => new(word.Word, effect,
        new RelayCommand(() => Difference.Words.FirstOrDefault(chip => chip.Code == word.Word)?.ChooseCommand.Execute(null)),
        _textStyles?.DefaultVernacularId);
    private void OnDifferenceChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();
}
