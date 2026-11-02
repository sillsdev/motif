using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class TextsPageModel
{
    /// <summary>What a held matrix side belongs to: the project, Proposal and Trial it was counted from.</summary>
    private sealed record MatrixKey(string Project, string ProposalId, string TrialId);

    private MatrixProposalSummary? _matrixSummary;
    private MatrixKey? _matrixHeld;
    private MatrixKey? _matrixReading;
    private MatrixKey? _matrixFailed;
    private (long Page, EvidenceLens Lens)? _matrixFailedAt;
    private Task _matrixRead = Task.CompletedTask;
    private int _matrixGeneration;

    /// <summary>Reduces the Trial's words to the matrix summary; tests replace it to see a failed read.</summary>
    internal Func<IReadOnlyList<AssessmentWordResult>, MatrixProposalSummary> SummariseTrial { get; set; } =
        MatrixProposalSummary.Of;

    private MatrixKey? CurrentMatrixKey() => Context.ProjectPath is { } path &&
        Context.EvidenceLens is { InProposalMode: true, Proposal: { } trial }
        ? new MatrixKey(path, trial.Trial.ProposalId, trial.Trial.JobId) : null;

    private MatrixSideState MatrixState => CurrentMatrixKey() is not { } key ? MatrixSideState.None
        : key == _matrixHeld ? MatrixSideState.Held
        : key == _matrixFailed ? MatrixSideState.Unreadable : MatrixSideState.Reading;

    private bool OffToday => Context.Lens is not null && ProposalViewScope.ViewOf(Context) != MarkupView.Today;

    /// <summary>Whether the tab's content is today's alone, which the Proposal view does not draw yet.</summary>
    public bool ShowsTabContent => !(Context.Lens is not null && ProposalViewScope.ViewOf(Context) == MarkupView.Proposal &&
        Tab is TextsTab.Lists or TextsTab.WhatChanged);

    /// <summary>The note saying what the tab pairs, what stays today's and why the Proposal may have nothing to show.</summary>
    public string? ProposalNote => Tab switch
    {
        TextsTab.Matrix when !Assess.Compare.HasWords => null,
        TextsTab.Matrix when MatrixState == MatrixSideState.Reading && Context.Lens is not null &&
            ProposalViewScope.ViewOf(Context) == MarkupView.TodayToProposal => MotifAppStrings.TextsProposalReadingNote,
        TextsTab.Matrix => ProposalPageNote.For(Context, new ProposalPageNote.Parts(MotifAppStrings.TextsMatrixCountsNoun,
            MatrixState == MatrixSideState.Reading, MatrixState == MatrixSideState.Unreadable,
            MatrixState == MatrixSideState.Held, MatrixPairedNote(), MotifAppStrings.TextsProposalViewNote)),
        TextsTab.AnalyzeTexts => OffToday ? MotifAppStrings.TextsProposalWordCardsNote : null,
        _ when !OffToday || !Assess.Compare.HasWords => null,
        _ => ProposalViewScope.ViewOf(Context) == MarkupView.Proposal
            ? MotifAppStrings.TextsProposalTabNotPairedViewNote : MotifAppStrings.TextsProposalTabNotPairedNote,
    };

    /// <summary>Whether the page has a note to show.</summary>
    public bool HasProposalNote => ProposalNote is not null;

    // Says the cells are paired only when the two sides were checked to cover the same words.
    private string MatrixPairedNote() => Assess.Compare.ProposalWordsMatch == true
        ? MotifAppStrings.TextsProposalPairedNote : MotifAppStrings.TextsProposalDifferentWordsNote;

    // Reads nothing: draws the held side, or its absence, for the bar's current view.
    private void ShowMatrixProposal()
    {
        if (CurrentMatrixKey() is not { } shown) ForgetMatrixSide();
        else if (shown != _matrixHeld) ForgetHeldSide();
        var view = Context.Lens is null ? MarkupView.Today : ProposalViewScope.ViewOf(Context);
        Assess.Compare.ShowProposal(view, MatrixState, _matrixHeld == CurrentMatrixKey() ? _matrixSummary : null);
        OnPropertyChanged(nameof(ProposalNote));
        OnPropertyChanged(nameof(HasProposalNote));
        OnPropertyChanged(nameof(ShowsTabContent));
        OnPropertyChanged(nameof(ShowListsContent));
        OnPropertyChanged(nameof(ShowWhatChangedContent));
    }

    // Counts the open Proposal's Trial only while this page is shown and the held side is for another key.
    private Task EnsureMatrixSideAsync(CancellationToken cancellationToken)
    {
        if (Context.CurrentPage != WorkspacePage.Texts || CurrentMatrixKey() is not { } key || key == _matrixHeld)
            return Task.CompletedTask;
        // A failure is retried by a later show or newer evidence, not by the same show's other trigger.
        if (key == _matrixFailed && _matrixFailedAt == (Context.PageRequestVersion, Context.EvidenceLens))
            return Task.CompletedTask;
        return key == _matrixReading ? _matrixRead : _matrixRead = ReadMatrixSideAsync(key, cancellationToken);
    }

    private async Task ReadShownMatrixSideAsync()
    {
        try { await EnsureMatrixSideAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
    }

    private async Task ReadMatrixSideAsync(MatrixKey key, CancellationToken cancellationToken)
    {
        var generation = ++_matrixGeneration;
        var startedAt = (Context.PageRequestVersion, Context.EvidenceLens);
        _matrixReading = key;
        _matrixFailed = null;
        ShowMatrixProposal();
        try
        {
            var words = Context.EvidenceLens.Proposal!.Words;
            if (await SummariseAsync(words, SummariseTrial, cancellationToken).ConfigureAwait(true) is not { } read) return;
            if (generation != _matrixGeneration || key != CurrentMatrixKey()) return;
            _matrixSummary = read.Summary;
            _matrixFailed = read.Failed ? key : null;
            _matrixFailedAt = read.Failed ? startedAt : null;
            _matrixHeld = read.Failed ? null : key;
            ShowMatrixProposal();
        }
        finally
        {
            if (generation == _matrixGeneration) _matrixReading = null;
        }
    }

    // A thrown error is a failed read; a cancelled one is null and silent; the caller's own cancel passes.
    private static async Task<(MatrixProposalSummary? Summary, bool Failed)?> SummariseAsync(
        IReadOnlyList<AssessmentWordResult> words, Func<IReadOnlyList<AssessmentWordResult>, MatrixProposalSummary> summarise,
        CancellationToken cancellationToken)
    {
        try { return (await Task.Run(() => summarise(words), cancellationToken).ConfigureAwait(true), false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return (null, true); }
    }

    // A side held for another Proposal or Trial is of no use to this one, so it is dropped at once.
    private void ForgetHeldSide()
    {
        _matrixHeld = null;
        _matrixSummary = null;
    }

    // With no Proposal open nothing is held or failed, so no side is kept for one nobody can see.
    private void ForgetMatrixSide()
    {
        ForgetHeldSide();
        _matrixFailed = null;
        _matrixFailedAt = null;
    }

    private void ClearMatrixProposal()
    {
        _matrixGeneration++;
        _matrixReading = null;
        ForgetMatrixSide();
        ShowMatrixProposal();
    }
}
