using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDifferenceViewModel
{
    /// <summary>Reads the newest saved Difference of a Proposal; with none, the section offers to make one.</summary>
    /// <param name="projectPath">The open project.</param>
    /// <param name="proposalId">The Proposal whose Difference is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <param name="offer">Whether a Difference can be made for the Proposal at all.</param>
    public async Task LoadAsync(string projectPath, string proposalId, CancellationToken cancellationToken = default,
        DifferenceOffer offer = DifferenceOffer.Available)
    {
        var generation = Reset();
        Maker.Reset();
        _projectPath = projectPath;
        _proposalId = proposalId;
        _offer = offer;
        RefreshTexts();
        State = ProposalDifferenceState.Loading;
        var read = await ReadAsync(Request(0), Restart(ref _loadCancellation, cancellationToken)).ConfigureAwait(true);
        if (generation != _generation || read is null) return;
        if (!read.Succeeded)
        {
            if (read.Refusal?.Code == RefusalCodes.DifferenceNone)
            {
                State = ProposalDifferenceState.Absent;
                return;
            }
            if (read.Refusal?.Code == RefusalCodes.DifferenceUnreadable)
            {
                // Every saved Difference is damaged: name them and offer to make one, as with none saved.
                SavedDamaged = ProposalDifferenceDamagedFacts.FromFacts(read.Refusal.Facts);
                SavedUnreadable = SavedDamaged.Count;
                State = ProposalDifferenceState.Absent;
                return;
            }
            ShownRefusal = WindowRefusal.From(read.Refusal!, MotifAppStrings.ProposalDifferenceUnreadable);
            State = ProposalDifferenceState.Refused;
            return;
        }
        Adopt(read.Value!);
    }

    // Shows a Difference whose summary and first page of words were just read or just made.
    private void Adopt(ProposalDifferenceResponse response)
    {
        _differenceId = response.Summary.DifferenceId;
        SetSaved(response);
        Summary = response.Summary;
        WordDetailKept = response.WordDetailKept;
        WordDetailUnreadable = response.WordDetailUnreadable;
        BuildChips(response.Summary, response.WordDetailKept);
        ApplyWords(response, replace: true);
        State = ProposalDifferenceState.Shown;
        // After the Texts were listed, so the list follows the Text the shown Difference compares.
        SelectedText = ChoiceOfSummary(TextChoices) ?? SelectedText;
        OnPropertyChanged(nameof(MakeLabel));
    }

    /// <summary>Forgets the Difference, as when no Proposal is selected or the project closes.</summary>
    public void Clear()
    {
        Reset();
        Maker.Reset();
        _projectPath = null;
        _proposalId = null;
        State = ProposalDifferenceState.None;
    }

    private int Reset()
    {
        var generation = ++_generation;
        _wordsGeneration++;
        _placesGeneration++;
        Stop(ref _loadCancellation);
        Stop(ref _wordsCancellation);
        Stop(ref _placesCancellation);
        _differenceId = null;
        SetSaved(null);
        _checkOnly = false;
        _bucket = _limitation = _beforeState = _afterState = null;
        _nextWordOffset = _nextPlaceOffset = null;
        ShownRefusal = null;
        Summary = null;
        UnreadableEvidence = [];
        WordDetailKept = true;
        WordDetailUnreadable = false;
        MovedChips = CheckChips = ImprovedChips = LimitationChips = Words = [];
        MatchingWords = 0;
        HasFilter = false;
        CanShowMoreWords = false;
        ClearSelectedWord();
        return generation;
    }

    private ProposalDifferenceShowRequest Request(int offset) =>
        new(_projectPath!, _proposalId, _differenceId, _checkOnly, _bucket, _beforeState, _afterState, offset, PageSize,
            _limitation);

    private void BuildChips(ProposalDifferenceSummary summary, bool kept)
    {
        // A count that lists none of its words filters nothing; once the words are gone nothing filters.
        var listedAny = kept && summary.ListedWordCount > 0;
        MovedChips = [.. summary.Matrix.Select(cell =>
        {
            var filters = kept && cell.WordsListed > 0 && !(summary.WordsListed is not null && cell.BeforeState == cell.AfterState);
            return new DifferenceChipViewModel(DifferenceChipKind.Moved,
                ListedLabel(ProposalDifferenceWords.Moved(cell.BeforeState, cell.AfterState), cell.Words, cell.WordsListed, filters),
                cell.Words, cell.BeforeState + "\n" + cell.AfterState, filters, Chosen);
        })];
        CheckChips =
        [
            new DifferenceChipViewModel(DifferenceChipKind.CheckOnly, MotifAppStrings.ProposalDifferenceCheckChip,
                summary.CouldBeBadWords, string.Empty, listedAny, Chosen),
            .. BucketChips(summary, ProposalDifferenceWords.CouldBeBad, kept),
        ];
        ImprovedChips = [.. BucketChips(summary, ProposalDifferenceWords.Improved, kept)];
        LimitationChips = [.. summary.Limitations.Select(limit => new DifferenceChipViewModel(
            DifferenceChipKind.Limitation,
            ListedLabel(ProposalDifferenceWords.Limitation(limit.Limitation), limit.Words, limit.WordsListed,
                kept && limit.WordsListed > 0),
            limit.Words, ProposalDifferenceWords.Code(limit.Limitation), kept && limit.WordsListed > 0, Chosen))];
    }

    // Says on a chip that opens a list how many of its words are listed when some are only counted.
    private static string ListedLabel(string label, int words, int listed, bool opens) => opens && listed < words
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceChipListedFormat, label, listed)
        : label;

    private IEnumerable<DifferenceChipViewModel> BucketChips(ProposalDifferenceSummary summary,
        IReadOnlyList<ProposalDifferenceBucket> buckets, bool kept) =>
        buckets.Select(bucket => (bucket, count: summary.Buckets.FirstOrDefault(item => item.Bucket == bucket)))
            .Where(item => item.count is { Words: > 0 })
            .Select(item => new DifferenceChipViewModel(DifferenceChipKind.Bucket,
                ListedLabel(ProposalDifferenceWords.Bucket(item.bucket), item.count!.Words, item.count.WordsListed,
                    kept && item.count.WordsListed > 0),
                item.count.Words, ProposalDifferenceWords.Code(item.bucket), kept && item.count.WordsListed > 0, Chosen));

    private void ApplyWords(ProposalDifferenceResponse response, bool replace)
    {
        var added = response.Words.Select(word => new DifferenceChipViewModel(DifferenceChipKind.Word, word.Word,
            word.Occurrences, word.Word, true, Chosen) { IsActive = word.Word == SelectedWord?.Word });
        Words = replace ? [.. added] : [.. Words, .. added];
        MatchingWords = response.MatchingWords;
        RaiseWordsStatus();
        UnreadableEvidence = response.UnreadableEvidence ?? [];
        _nextWordOffset = response.NextOffset;
        CanShowMoreWords = response.NextOffset is not null && Words.Count < MostShown;
    }

    // True when a read found the word detail gone (Proposal decided meanwhile); the section is then summary only.
    private bool AdoptWordDetail(ProposalDifferenceResponse response)
    {
        if (response.WordDetailKept == WordDetailKept && response.WordDetailUnreadable == WordDetailUnreadable) return false;
        _checkOnly = false;
        _bucket = _limitation = _beforeState = _afterState = null;
        _nextWordOffset = _nextPlaceOffset = null;
        ShownRefusal = null;
        ClearSelectedWord();
        Summary = response.Summary;
        WordDetailKept = response.WordDetailKept;
        WordDetailUnreadable = response.WordDetailUnreadable;
        BuildChips(response.Summary, response.WordDetailKept);
        Words = [];
        MatchingWords = 0;
        HasFilter = false;
        CanShowMoreWords = false;
        return true;
    }

    private void Chosen(DifferenceChipViewModel chip)
    {
        if (chip.Kind == DifferenceChipKind.Word)
        {
            _ = SelectWordAsync(chip);
            return;
        }
        var lifting = chip.IsActive;
        switch (chip.Kind)
        {
            case DifferenceChipKind.CheckOnly: _checkOnly = !lifting; break;
            case DifferenceChipKind.Bucket: _bucket = lifting ? null : chip.Code; break;
            case DifferenceChipKind.Limitation: _limitation = lifting ? null : chip.Code; break;
            default:
                var states = chip.Code.Split('\n');
                (_beforeState, _afterState) = lifting ? (null, null) : (states[0], states[1]);
                break;
        }
        _ = RefilterAsync();
    }

    private Task ClearFiltersAsync()
    {
        _checkOnly = false;
        _bucket = _limitation = _beforeState = _afterState = null;
        return RefilterAsync();
    }

    private async Task RefilterAsync()
    {
        ClearSelectedWord();
        UpdateActive();
        // The old filter's page offset and Show More button must not reach the new filter's read.
        _nextWordOffset = null;
        CanShowMoreWords = false;
        var generation = ++_wordsGeneration;
        var read = await ReadAsync(Request(0), Restart(ref _wordsCancellation)).ConfigureAwait(true);
        if (generation != _wordsGeneration || read is null) return;
        if (!read.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(read.Refusal!, MotifAppStrings.ProposalDifferenceUnreadable);
            return;
        }
        if (AdoptWordDetail(read.Value!)) return;
        ShownRefusal = null;
        ApplyWords(read.Value!, replace: true);
    }

    private async Task ShowMoreWordsAsync()
    {
        if (_nextWordOffset is not { } offset) return;
        var generation = ++_wordsGeneration;
        var read = await ReadAsync(Request(offset), Restart(ref _wordsCancellation)).ConfigureAwait(true);
        if (generation != _wordsGeneration || read is null) return;
        if (!read.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(read.Refusal!, MotifAppStrings.ProposalDifferenceUnreadable);
            return;
        }
        if (AdoptWordDetail(read.Value!)) return;
        ShownRefusal = null;
        ApplyWords(read.Value!, replace: false);
    }

    // Starts a read on a fresh token and cancels the one it replaces, so a superseded read stops early.
    private static CancellationToken Restart(ref CancellationTokenSource? slot, CancellationToken outer = default)
    {
        var old = slot;
        slot = outer.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(outer) : new CancellationTokenSource();
        Stop(ref old);
        return slot.Token;
    }

    private static void Stop(ref CancellationTokenSource? slot)
    {
        var old = slot;
        slot = null;
        if (old is null) return;
        old.Cancel();
        old.Dispose();
    }

    // One read: null when cancelled, a refusal when it threw, and IsReadingMore holds while it runs.
    private async Task<CommandOutcome<ProposalDifferenceResponse>?> ReadAsync(
        ProposalDifferenceShowRequest request, CancellationToken cancellationToken)
    {
        _reads++;
        OnPropertyChanged(nameof(IsReadingMore));
        try
        {
            return await _read(request, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                RefusalCodes.WindowDifferenceReadFailed, FailureReason.Refused, exception.Message));
        }
        finally
        {
            _reads--;
            OnPropertyChanged(nameof(IsReadingMore));
        }
    }

    private void UpdateActive()
    {
        HasFilter = _checkOnly || _bucket is not null || _limitation is not null || _beforeState is not null;
        foreach (var chip in MovedChips)
            chip.IsActive = _beforeState is not null && chip.Code == _beforeState + "\n" + _afterState;
        foreach (var chip in CheckChips)
            chip.IsActive = chip.Kind == DifferenceChipKind.CheckOnly ? _checkOnly : chip.Code == _bucket;
        foreach (var chip in ImprovedChips) chip.IsActive = chip.Code == _bucket;
        foreach (var chip in LimitationChips) chip.IsActive = chip.Code == _limitation;
        RaiseWordsStatus();
    }

    private void RaiseWordsStatus()
    {
        OnPropertyChanged(nameof(WordsStatusText));
        OnPropertyChanged(nameof(NoMatchingText));
        OnPropertyChanged(nameof(ListedNoteText));
        OnPropertyChanged(nameof(HasListedNote));
    }
}
