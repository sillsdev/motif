using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDifferenceViewModel
{
    private bool _settingSaved;

    /// <summary>The Proposal's saved Differences, one per Text and decisions, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSavedChoices))]
    private IReadOnlyList<DifferenceSavedChoice> _savedChoices = [];

    /// <summary>The saved Difference the section shows; choosing another reads that one.</summary>
    [ObservableProperty]
    private DifferenceSavedChoice? _selectedSaved;

    /// <summary>How many saved Differences the list leaves out because it is bounded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedNote), nameof(SavedNoteText))]
    private int _savedNotListed;

    /// <summary>How many saved Differences the list leaves out because their summary could not be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedNote), nameof(SavedNoteText))]
    private int _savedUnreadable;

    /// <summary>Whether the list of saved Differences shows: only when there is more than one to choose from.</summary>
    public bool ShowsSavedChoices => SavedChoices.Count > 1;

    public bool HasSavedNote => SavedNotListed > 0 || SavedUnreadable > 0;

    /// <summary>"2 more saved Differences are not listed. 1 saved Difference could not be read."</summary>
    public string SavedNoteText => string.Join(' ', new[]
    {
        SavedNotListed > 0 ? PluralText.Format(MotifAppStrings.ProposalDifferenceSavedMorePattern, SavedNotListed) : null,
        SavedUnreadable > 0 ? PluralText.Format(MotifAppStrings.ProposalDifferenceSavedUnreadablePattern, SavedUnreadable) : null,
    }.OfType<string>());

    /// <summary>Says which Text and whose decisions the shown Difference compares, so it is never taken for another.</summary>
    public string BasisText => Summary is { } summary
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceBasisFormat, summary.TextTitle,
            summary.Reference == ProposalDifferenceReferences.SourceValidated
                ? MotifAppStrings.ProposalDifferenceBasisSourceValidated : MotifAppStrings.ProposalDifferenceBasisHuman)
        : string.Empty;

    partial void OnSelectedSavedChanged(DifferenceSavedChoice? value)
    {
        if (_settingSaved || value is null || value.DifferenceId == _differenceId) return;
        _ = ShowSavedAsync(value);
    }

    private void SetSaved(ProposalDifferenceResponse? response)
    {
        _settingSaved = true;
        try
        {
            SavedChoices = response?.Saved is { } saved ? Choices(saved) : [];
            SavedUnreadable = response?.SavedUnreadable ?? 0;
            SavedNotListed = response?.Saved is { } listed
                ? Math.Max(0, response.SavedTotal - listed.Count - response.SavedUnreadable) : 0;
            SelectedSaved = response is null ? null : SavedChoices.FirstOrDefault(choice =>
                choice.DifferenceId == response.Summary.DifferenceId);
        }
        finally
        {
            _settingSaved = false;
        }
    }

    // The made time tells apart Differences of different Trials; a title two Texts share also carries the Text id.
    private static DifferenceSavedChoice[] Choices(IReadOnlyList<ProposalDifferenceSaved> saved)
    {
        var sharedTitles = saved.GroupBy(item => item.TextTitle, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.TextId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        return [.. saved.Select(item => Choice(item, sharedTitles.Contains(item.TextTitle)))];
    }

    private static DifferenceSavedChoice Choice(ProposalDifferenceSaved saved, bool showTextId)
    {
        var title = showTextId
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceSavedTextFormat, saved.TextTitle,
                saved.TextId[..Math.Min(8, saved.TextId.Length)])
            : saved.TextTitle;
        var made = DateTimeOffset.TryParse(saved.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
            out var when) ? when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : saved.CreatedUtc;
        return new DifferenceSavedChoice(saved.DifferenceId, saved.TextId, saved.Reference,
            string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceSavedItemFormat, title,
                saved.Reference == ProposalDifferenceReferences.SourceValidated
                    ? MotifAppStrings.ProposalDifferenceSavedSourceValidated : MotifAppStrings.ProposalDifferenceSavedHuman, made));
    }

    // Reads another of the Proposal's saved Differences by id, leaving what the Make button is doing alone.
    private async Task ShowSavedAsync(DifferenceSavedChoice choice)
    {
        var generation = Reset();
        _differenceId = choice.DifferenceId;
        State = ProposalDifferenceState.Loading;
        var read = await ReadAsync(Request(0), Restart(ref _loadCancellation)).ConfigureAwait(true);
        if (generation != _generation || read is null) return;
        if (!read.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(read.Refusal!, MotifAppStrings.ProposalDifferenceUnreadable);
            State = ProposalDifferenceState.Refused;
            return;
        }
        Adopt(read.Value!);
    }
}
