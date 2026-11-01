using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDifferenceViewModel
{
    /// <summary>Reads the newest saved Difference of a Proposal; the section stays hidden when there is none.</summary>
    /// <param name="projectPath">The open project.</param>
    /// <param name="proposalId">The Proposal whose Difference is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task LoadAsync(string projectPath, string proposalId, CancellationToken cancellationToken = default)
    {
        var generation = Reset();
        _projectPath = projectPath;
        _proposalId = proposalId;
        State = ProposalDifferenceState.Loading;
        var read = await ReadAsync(Request(0), Restart(ref _loadCancellation, cancellationToken)).ConfigureAwait(true);
        if (generation != _generation || read is null) return;
        if (!read.Succeeded)
        {
            if (read.Refusal?.Code == RefusalCodes.DifferenceNone)
            {
                State = ProposalDifferenceState.None;
                return;
            }
            ShownRefusal = WindowRefusal.From(read.Refusal!, MotifAppStrings.ProposalDifferenceUnreadable);
            State = ProposalDifferenceState.Refused;
            return;
        }
        var response = read.Value!;
        _differenceId = response.Summary.DifferenceId;
        Summary = response.Summary;
        WordDetailKept = response.WordDetailKept;
        BuildChips(response.Summary, response.WordDetailKept);
        ApplyWords(response, replace: true);
        State = ProposalDifferenceState.Shown;
    }

    /// <summary>Forgets the Difference, as when no Proposal is selected or the project closes.</summary>
    public void Clear()
    {
        Reset();
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
        _checkOnly = false;
        _bucket = _limitation = _beforeState = _afterState = null;
        _nextWordOffset = _nextPlaceOffset = null;
        ShownRefusal = null;
        Summary = null;
        WordDetailKept = true;
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

    private void BuildChips(ProposalDifferenceSummary summary, bool canChoose)
    {
        MovedChips = [.. summary.Matrix.Select(cell => new DifferenceChipViewModel(DifferenceChipKind.Moved,
            ProposalDifferenceWords.Moved(cell.BeforeState, cell.AfterState), cell.Words,
            cell.BeforeState + "\n" + cell.AfterState, canChoose, Chosen))];
        CheckChips =
        [
            new DifferenceChipViewModel(DifferenceChipKind.CheckOnly, MotifAppStrings.ProposalDifferenceCheckChip,
                summary.CouldBeBadWords, string.Empty, canChoose, Chosen),
            .. BucketChips(summary, ProposalDifferenceWords.CouldBeBad, canChoose),
        ];
        ImprovedChips = [.. BucketChips(summary, ProposalDifferenceWords.Improved, canChoose)];
        LimitationChips = [.. summary.Limitations.Select(limit => new DifferenceChipViewModel(
            DifferenceChipKind.Limitation, ProposalDifferenceWords.Limitation(limit.Limitation), limit.Words,
            ProposalDifferenceWords.Code(limit.Limitation), canChoose, Chosen))];
    }

    private IEnumerable<DifferenceChipViewModel> BucketChips(ProposalDifferenceSummary summary,
        IReadOnlyList<ProposalDifferenceBucket> buckets, bool canChoose) =>
        buckets.Select(bucket => (bucket, count: summary.Buckets.FirstOrDefault(item => item.Bucket == bucket)))
            .Where(item => item.count is { Words: > 0 })
            .Select(item => new DifferenceChipViewModel(DifferenceChipKind.Bucket,
                ProposalDifferenceWords.Bucket(item.bucket), item.count!.Words,
                ProposalDifferenceWords.Code(item.bucket), canChoose, Chosen));

    private void ApplyWords(ProposalDifferenceResponse response, bool replace)
    {
        var added = response.Words.Select(word => new DifferenceChipViewModel(DifferenceChipKind.Word, word.Word,
            word.Occurrences, word.Word, true, Chosen) { IsActive = word.Word == SelectedWord?.Word });
        Words = replace ? [.. added] : [.. Words, .. added];
        MatchingWords = response.MatchingWords;
        _nextWordOffset = response.NextOffset;
        CanShowMoreWords = response.NextOffset is not null && Words.Count < MostShown;
    }

    // True when a read found the word detail gone (Proposal decided meanwhile); the section is then summary only.
    private bool AdoptWordDetail(ProposalDifferenceResponse response)
    {
        if (response.WordDetailKept == WordDetailKept) return false;
        _checkOnly = false;
        _bucket = _limitation = _beforeState = _afterState = null;
        _nextWordOffset = _nextPlaceOffset = null;
        ShownRefusal = null;
        ClearSelectedWord();
        Summary = response.Summary;
        WordDetailKept = response.WordDetailKept;
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
    }
}
