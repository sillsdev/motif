using System.Globalization;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDifferenceViewModel
{
    private ProposalDifferenceShowRequest PlacesRequest(string word, int offset) =>
        new(_projectPath!, _proposalId, _differenceId, Limit: 1, Word: word, OccurrenceOffset: offset,
            OccurrenceLimit: PageSize);

    private void ClearSelectedWord()
    {
        _placesGeneration++;
        Stop(ref _placesCancellation);
        _nextPlaceOffset = null;
        _placesTotal = 0;
        _placesListed = 0;
        SelectedWord = null;
        Places = [];
        CanShowMorePlaces = false;
        foreach (var chip in Words) chip.IsActive = false;
    }

    private async Task SelectWordAsync(DifferenceChipViewModel chip)
    {
        var lifting = chip.IsActive;
        ClearSelectedWord();
        if (lifting) return;
        chip.IsActive = true;
        var generation = _placesGeneration;
        var read = await ReadAsync(PlacesRequest(chip.Code, 0), Restart(ref _placesCancellation)).ConfigureAwait(true);
        if (generation != _placesGeneration || read is null) return;
        if (!read.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(read.Refusal!, MotifAppStrings.ProposalDifferenceUnreadable);
            return;
        }
        var response = read.Value!;
        if (AdoptWordDetail(response)) return;
        ShownRefusal = null;
        SelectedWord = response.Words.FirstOrDefault(word => word.Word == chip.Code);
        ApplyPlaces(response.Occurrences, replace: true);
    }

    private async Task ShowMorePlacesAsync()
    {
        if (SelectedWord is not { } word || _nextPlaceOffset is not { } offset) return;
        var generation = ++_placesGeneration;
        var read = await ReadAsync(PlacesRequest(word.Word, offset), Restart(ref _placesCancellation)).ConfigureAwait(true);
        if (generation != _placesGeneration || read is null) return;
        if (!read.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(read.Refusal!, MotifAppStrings.ProposalDifferenceUnreadable);
            return;
        }
        if (AdoptWordDetail(read.Value!)) return;
        ShownRefusal = null;
        ApplyPlaces(read.Value!.Occurrences, replace: false);
    }

    private void ApplyPlaces(ProposalDifferenceOccurrencePage? page, bool replace)
    {
        var added = (page?.Places ?? []).Select(place => string.Format(CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalDifferencePlaceFormat, place.LineNumber, place.OccurrenceIndex + 1));
        Places = replace ? [.. added] : [.. Places, .. added];
        _placesTotal = page?.Total ?? 0;
        _placesListed = page?.PlacesListed ?? 0;
        OnPropertyChanged(nameof(PlacesStatusText));
        OnPropertyChanged(nameof(ShowsPlacesStatus));
        _nextPlaceOffset = page?.NextOffset;
        CanShowMorePlaces = page?.NextOffset is not null && Places.Count < MostShown;
    }
}
