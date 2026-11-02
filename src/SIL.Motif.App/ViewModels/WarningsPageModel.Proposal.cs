using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class WarningsPageModel
{
    private IReadOnlyList<GrammarWarning>? _proposalFindings;
    private ProposalCheckKey? _failedKey;
    private ProposalCheckKey? _heldKey;
    private ProposalCheckKey? _readingKey;
    private Task _proposalRead = Task.CompletedTask;
    private int _proposalGeneration;

    /// <summary>What a held check belongs to: the project, the Proposal and the Trial it was read for.</summary>
    private sealed record ProposalCheckKey(string Project, string ProposalId, string TrialId);

    private ProposalCheckKey? CurrentKey() => Context.ProjectPath is { } path &&
        Context.EvidenceLens is { InProposalMode: true, Proposal: { } trial }
        ? new ProposalCheckKey(path, trial.Trial.ProposalId, trial.Trial.JobId) : null;

    // What is held counts only for the key it was read for, so an older Proposal's rows can never show.
    private bool Holds => CurrentKey() is { } key && key == _heldKey;

    private IReadOnlyList<GrammarWarning>? HeldFindings => Holds ? _proposalFindings : null;

    // A failed read is not held: the page says so and reads again when next shown or published to.
    private bool ProposalReading => CurrentKey() is { } key && key != _heldKey && key != _failedKey;

    private bool ProposalUnreadable => _failedKey is { } failed && failed == CurrentKey();

    /// <summary>
    /// What the page says about its findings while a Proposal is open: what is paired, what stays today's, or why the
    /// Proposal has no check to show. Null outside Proposal mode, and while the Proposal's check is read except in the pair view.
    /// </summary>
    public string? ProposalNote => ProposalReading && Context.Lens is not null &&
        ProposalViewScope.ViewOf(Context) == MarkupView.TodayToProposal ? MotifAppStrings.WarningsProposalReadingNote
        : ProposalPageNote.For(Context, new ProposalPageNote.Parts(
        MotifAppStrings.WarningsFindingsNoun, ProposalReading, ProposalUnreadable, HeldFindings is not null,
        MotifAppStrings.WarningsProposalPairedNote, MotifAppStrings.WarningsProposalViewNote,
        Context.Lens is not null && ProposalViewScope.ViewOf(Context) == MarkupView.TodayToProposal &&
        !Grammar.HasChecked ? MotifAppStrings.WarningsProposalTodayNotCheckedNote : null));

    /// <summary>Whether the page has a note to show.</summary>
    public bool HasProposalNote => ProposalNote is not null;

    /// <summary>Whether the Proposal view has no check of the Proposal's own to list, so it says so instead.</summary>
    public bool ShowProposalNone => Context.Lens is not null && ProposalViewScope.ViewOf(Context) == MarkupView.Proposal &&
        HeldFindings is null;

    /// <summary>What the page says in place of the findings the Proposal lacks.</summary>
    public string ProposalNoneText => ProposalReading ? MotifAppStrings.OverviewProposalReading : MotifAppStrings.ProposalPageNone;

    // Reads the Proposal's check only while this page is shown and the held one is for another key.
    private Task EnsureProposalCheckAsync(CancellationToken cancellationToken)
    {
        if (Context.CurrentPage != WorkspacePage.Warnings || CurrentKey() is not { } key || key == _heldKey)
            return Task.CompletedTask;
        return key == _readingKey ? _proposalRead : _proposalRead = ReadProposalCheckAsync(key, cancellationToken);
    }

    private async Task ReadProposalCheckAsync(ProposalCheckKey key, CancellationToken cancellationToken)
    {
        var generation = ++_proposalGeneration;
        _readingKey = key;
        _failedKey = null;
        ShowHeldProposalCheck();
        try
        {
            if (await ReadFindingsAsync(key, cancellationToken).ConfigureAwait(true) is not { } read) return;
            var (findings, failed) = read;
            if (generation != _proposalGeneration || key != CurrentKey()) return;
            _failedKey = failed ? key : null;
            _heldKey = failed ? null : key;
            _proposalFindings = failed ? null : findings;
            ShowHeldProposalCheck();
        }
        finally
        {
            if (generation == _proposalGeneration) _readingKey = null;
        }
    }

    // A refusal or a thrown error is a failed read; a cancelled one is null and silent.
    private async Task<(IReadOnlyList<GrammarWarning>? Findings, bool Failed)?> ReadFindingsAsync(
        ProposalCheckKey key, CancellationToken cancellationToken)
    {
        try
        {
            var read = await Context.Commands.ReadStoredGrammarCheckAsync(
                new GrammarCheckRequest(key.Project, key.ProposalId), cancellationToken).ConfigureAwait(true);
            return read.Succeeded ? (read.Value?.Check?.Findings, false) : (null, true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception) { return (null, true); }
    }

    private async Task ReadShownProposalCheckAsync()
    {
        try { await EnsureProposalCheckAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
    }

    private void ClearProposalCheck()
    {
        _proposalGeneration++;
        _heldKey = null;
        _readingKey = null;
        _proposalFindings = null;
        _failedKey = null;
        Grammar.Warnings.ShowProposal(null, MarkupView.Today);
    }

    private void ShowHeldProposalCheck()
    {
        var view = Context.Lens is null ? MarkupView.Today : ProposalViewScope.ViewOf(Context);
        Grammar.Warnings.ShowProposal(HeldFindings, view);
        OnPropertyChanged(nameof(ProposalNote));
        OnPropertyChanged(nameof(HasProposalNote));
        OnPropertyChanged(nameof(ShowProposalNone));
        OnPropertyChanged(nameof(ProposalNoneText));
        OnPropertyChanged(nameof(IsGrammarNotChecked));
    }
}
