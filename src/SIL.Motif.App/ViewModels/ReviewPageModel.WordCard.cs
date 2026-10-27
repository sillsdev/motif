using SIL.Motif.Contract.Ids;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ReviewPageModel
{
    private ReviewWordCardDetails? _wordCardDetails;
    private Task _wordCardStop = Task.CompletedTask;

    /// <summary>Reads an actionable card from the current reader for this exact Review change and evidence.</summary>
    /// <param name="change">The change whose Inspector requested the card.</param>
    /// <param name="cancellationToken">The requesting card's lifetime.</param>
    internal async Task<(ResultsTokenViewModel? Token, string? Failure)> ReadWordCardTokenAsync(
        ChangeViewModel change, CancellationToken cancellationToken)
    {
        ReleaseWordCard();
        await _wordCardStop.ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        var reads = Context.SelectionReads;
        var generation = reads.Generation;
        var reader = reads.Reader;
        var lens = Context.EvidenceLens;
        var revision = change.Presentation.EvidenceRevision;
        if (reader is null || Context.CurrentPage != WorkspacePage.Review ||
            !CanonicalId.TryParse(change.WordformId, out var canonical) ||
            Context.Assess.TextWords?.WordCardActions is not { } actions)
            return (null, MotifAppStrings.ReviewCardDetailsUnavailable);
        var id = canonical.ToGuid();
        var matches = (reads.Summary?.Words ?? []).Where(word => word.Key.Form == change.Word &&
            (word.Key.WordformId == id || word.Key.WordformId is null &&
                word.Actions.CandidateWordformIds is { Count: 1 } candidates && candidates[0] == id)).ToArray();
        if (matches.Length != 1) return (null, MotifAppStrings.ReviewCardDetailsUnavailable);
        var word = matches[0];
        var target = new WordActionTarget(change.Word, id, change.Occurrence, word.Assessment?.AssessmentId,
            reader.Context.ExpectedWriteContext());
        var source = new ReviewWordCardDetails(reader, word, Context.Assess.Words.Sources, target,
            Path.GetFileNameWithoutExtension(Context.ProjectPath) ?? string.Empty);
        _wordCardDetails = source;
        var opened = await source.OpenAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_wordCardDetails, source) || !ReferenceEquals(reader, reads.Reader) ||
            generation != reads.Generation || !ReferenceEquals(lens, Context.EvidenceLens) ||
            Context.CurrentPage != WorkspacePage.Review || change.Presentation.EvidenceRevision != revision ||
            !Entries.Any(entry => ReferenceEquals(entry.Change, change)))
        {
            if (ReferenceEquals(_wordCardDetails, source)) ReleaseWordCard();
            return (null, MotifAppStrings.ReviewCardEvidenceChanged);
        }
        if (!opened || source.Token is not { } token)
        {
            var failure = source.Failure ?? MotifAppStrings.ReviewCardDetailsUnavailable;
            ReleaseWordCard();
            return (null, failure);
        }
        token.Actions = actions;
        RefreshWordCardState();
        return (token, null);
    }

    private void RefreshWordCardState()
    {
        if (_wordCardDetails?.Token is not { } token) return;
        var relevant = Changes.Items.Where(change => change.Addresses(token)).ToArray();
        token.PendingState = PendingChangeStates.FromChanges(relevant);
        token.IsPending = relevant.Length > 0;
        token.SetStagedMarkings(relevant);
        token.AppliedPending = relevant;
        var lens = Context.EvidenceLens;
        token.ShowProposal(lens, lens.Proposal is { } trial ? reading => ProposalBuiltBy.For(trial, reading) : null,
            Context.ProposalStanding);
    }

    private void ReleaseWordCard()
    {
        var previous = _wordCardDetails;
        _wordCardDetails = null;
        if (previous is null) return;
        previous.Release();
        _wordCardStop = Task.WhenAll(_wordCardStop, previous.StopAsync());
    }
}
