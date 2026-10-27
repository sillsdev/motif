using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns one Review card's exact word detail and token until its card or reader is released.</summary>
internal sealed class ReviewWordCardDetails(
    SelectionReader reader,
    SelectionWordSummary word,
    IReadOnlyList<AssessmentWordResult> assessments,
    WordActionTarget target,
    string projectName)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private CancellationTokenRegistration _closed;
    private SelectionRead<SelectionWordDetails>? _read;
    private IDisposable? _card;
    private Task _pending = Task.CompletedTask;
    private volatile bool _released;

    internal ResultsTokenViewModel? Token { get; private set; }
    internal string? Failure { get; private set; }

    /// <summary>Reads once from the captured reader, independently of Analyze's display caches.</summary>
    /// <param name="cancellationToken">The card's lifetime; cancellation also releases a completed token.</param>
    internal Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _closed = cancellationToken.Register(Release);
        var current = ReadAsync(_cancellation);
        _pending = current;
        return current;
    }

    private async Task<bool> ReadAsync(CancellationTokenSource cancellation)
    {
        SelectionRead<SelectionWordDetails>? read = null;
        ResultsTokenViewModel? token = null;
        IDisposable? card = null;
        try
        {
            if (_released) return false;
            var stateOutcome = await reader.ReadPresentationAsync(cancellation.Token).ConfigureAwait(true);
            using var state = stateOutcome.Value;
            if (_released || cancellation.IsCancellationRequested) return false;
            if (!stateOutcome.Succeeded)
            {
                Failure = WindowRefusal.From(stateOutcome.Refusal!).Sentence;
                return false;
            }
            var outcome = await reader.ReadWordDetailsAsync([word.Key], cancellation.Token).ConfigureAwait(true);
            read = outcome.Value;
            if (_released || cancellation.IsCancellationRequested) return false;
            if (!outcome.Succeeded)
            {
                Failure = WindowRefusal.From(outcome.Refusal!).Sentence;
                return false;
            }
            if (read!.PresentationStamp != state!.PresentationStamp ||
                read.Context != reader.Context ||
                read.Context.ExpectedWriteContext() != target.ExpectedContext ||
                !read.Value.Wordforms.TryGetValue(word.Key, out var stored) ||
                stored.WordformId != target.WordformId)
            {
                Failure = MotifAppStrings.ReviewCardEvidenceChanged;
                return false;
            }
            var snapshot = state.Value;
            var assessment = word.Assessment is { } producing
                ? assessments.FirstOrDefault(item => item.Word == word.Key.Form &&
                    item.Origin?.AssessmentId == producing.AssessmentId) ??
                    new AssessmentWordResult(word.Key.Form, producing.Outcome, producing.IsIncomplete,
                        string.Empty, null, null)
                    {
                        Morphology = producing.Morphology,
                        ReadingGrades = producing.ReadingGrades,
                        ProjectStanding = producing.ProjectStanding,
                        AnalysisComparison = producing.AnalysisComparison,
                        Origin = producing.Origin,
                        OccurrenceCount = producing.OccurrenceCount,
                        MissedApproved = producing.MissedApproved,
                    }
                : null;
            var analyses = SelectionDisplayProjection.StoredAnalyses(stored, projectName);
            token = new ResultsTokenViewModel(string.Empty, 0,
                new TextToken(target.Form, target.Form, null, null)
                {
                    WordformId = stored.WordformId,
                    FormWritingSystem = word.Key.WritingSystem,
                    TextWritingSystem = word.Key.WritingSystem,
                    StoredAnalyses = analyses,
                    IncorrectSpelling = stored.IncorrectSpelling,
                    WordLink = assessment?.TryWordLink,
                }, assessment, location: target.Form, occurrence: target.Occurrence,
                expectedContext: target.ExpectedContext, producingAssessmentId: target.AssessmentId,
                actionFacts: word.Actions, storedAnalysesAvailable: word.Actions.CandidateWordformIds.Count == 1);
            token.OwnRegistration(read.Lease.RegisterModel(SelectionModelKind.Token, SelectionModelUse.Pinned));
            card = read.Lease.RegisterModel(SelectionModelKind.Card);
            token.SetReadState(target.Occurrence is { } occurrence &&
                snapshot.ReadOccurrences.GetValueOrDefault(occurrence.TextId)?.Contains(occurrence) == true);
            token.SetWarningEvidence(ResultsTokenViewModel.WarningEvidenceFor(target.Form,
                new GrammarCheckResponse(snapshot.Warnings, true), assessments));
            lock (_gate)
            {
                if (_released || cancellation.IsCancellationRequested) return false;
                _read = read;
                read = null;
                _card = card;
                card = null;
                Token = token;
                token = null;
                return true;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (ObjectDisposedException) when (_released || cancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            token?.Dispose();
            card?.Dispose();
            read?.Dispose();
            Interlocked.CompareExchange(ref _cancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    /// <summary>Invalidates publication and releases all completed card ownership immediately.</summary>
    internal void Release()
    {
        ResultsTokenViewModel? token;
        IDisposable? card;
        SelectionRead<SelectionWordDetails>? read;
        lock (_gate)
        {
            _released = true;
            token = Token;
            Token = null;
            card = _card;
            _card = null;
            read = _read;
            _read = null;
        }
        try { _cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        token?.Dispose();
        card?.Dispose();
        read?.Dispose();
    }

    /// <summary>Cancels the card and drains the actual reader work before its owner replaces the reader.</summary>
    internal async Task StopAsync()
    {
        Release();
        try { await _pending.ConfigureAwait(true); }
        finally { _closed.Dispose(); }
    }
}
