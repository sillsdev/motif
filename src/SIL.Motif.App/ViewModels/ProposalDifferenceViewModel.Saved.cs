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

    /// <summary>The unreadable saved Differences, named by Text and reference.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedNote), nameof(SavedNoteText))]
    private IReadOnlyList<ProposalDifferenceDamaged> _savedDamaged = [];

    // Past this many the note counts the rest, so it stays a few lines however many are damaged.
    private const int MostDamagedNamed = 3;

    /// <summary>Whether the list of saved Differences shows: only when there is more than one to choose from.</summary>
    public bool ShowsSavedChoices => SavedChoices.Count > 1;

    public bool HasSavedNote => SavedNotListed > 0 || SavedUnreadable > 0 || SavedDamaged.Count > 0;

    /// <summary>
    /// "2 more saved Differences are not listed. The Difference for Hadithi, read against people's decisions and made
    /// 11/1/2026, could not be read. Make Difference replaces it."
    /// </summary>
    public string SavedNoteText => string.Join(' ', new[]
    {
        SavedNotListed > 0 ? PluralText.Format(MotifAppStrings.ProposalDifferenceSavedMorePattern, SavedNotListed) : null,
    }.Concat(DamagedNotes()).OfType<string>());

    // Names the first few; a count alone is all an older response gives.
    private IEnumerable<string?> DamagedNotes()
    {
        foreach (var damaged in SavedDamaged.Take(MostDamagedNamed))
            yield return DamagedNote(damaged);
        var unnamed = SavedUnreadable - Math.Min(SavedDamaged.Count, MostDamagedNamed);
        if (unnamed > 0)
            yield return PluralText.Format(MotifAppStrings.ProposalDifferenceSavedUnreadablePattern, unnamed);
    }

    private string DamagedNote(ProposalDifferenceDamaged damaged)
    {
        var sourceValidated = damaged.Reference == ProposalDifferenceReferences.SourceValidated;
        var title = TextChoices.FirstOrDefault(choice => string.Equals(choice.Id.ToString("D"), damaged.TextId,
            StringComparison.OrdinalIgnoreCase))?.Title ?? damaged.TextId[..Math.Min(8, damaged.TextId.Length)];
        var note = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceSavedDamagedFormat, title,
            sourceValidated ? MotifAppStrings.ProposalDifferenceSavedSourceValidated : MotifAppStrings.ProposalDifferenceSavedHuman,
            MadeText(damaged.CreatedUtc));
        // Only a Difference against people's decisions is made from here, and only while the Make button is offered.
        return sourceValidated || !OffersMake ? note : note + " " + MotifAppStrings.ProposalDifferenceSavedDamagedRemake;
    }

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
            SavedDamaged = response?.SavedDamaged ?? [];
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
        return new DifferenceSavedChoice(saved.DifferenceId, saved.TextId, saved.Reference,
            string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceSavedItemFormat, title,
                saved.Reference == ProposalDifferenceReferences.SourceValidated
                    ? MotifAppStrings.ProposalDifferenceSavedSourceValidated : MotifAppStrings.ProposalDifferenceSavedHuman, MadeText(saved.CreatedUtc)));
    }

    private static string MadeText(string createdUtc) =>
        DateTimeOffset.TryParse(createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
            ? when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : createdUtc;

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
