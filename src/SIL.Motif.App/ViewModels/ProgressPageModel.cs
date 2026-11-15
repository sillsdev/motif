using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Reads stored project facts for Progress and the shared health strip.</summary>
public sealed partial class ProgressPageModel : PageModel
{
    private int _generation;
    private CancellationTokenSource _stop = new();
    private readonly List<Task> _reads = [];

    public ProgressPageModel(WorkspaceContext context) : base(context) { }

    /// <summary>The health facts shared with the shell.</summary>
    public HealthStripViewModel Health => Context.Health;

    /// <summary>How many Proposals are ready for a person's decision, when a read succeeded.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(HasWaitingProposals), nameof(WaitingProposalsText))]
    private int? _waitingProposals;

    /// <summary>Whether a Proposal needs a decision.</summary>
    public bool HasWaitingProposals => WaitingProposals > 0;

    /// <summary>The number of Proposals needing a decision.</summary>
    public string WaitingProposalsText => WaitingProposals is { } count
        ? PluralText.Format(MotifAppStrings.ProgressWaitingProposalsPattern, count) : string.Empty;

    protected override void OnProjectCleared()
    {
        _generation++;
        Health.ClearProject();
        WaitingProposals = null;
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        RefreshAsync(projectPath, cancellationToken);

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) => RefreshHealthAsync(cancellationToken);

    protected override Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken) =>
        RefreshHealthAsync(cancellationToken);

    protected override Task OnGrammarCheckedAsync(CancellationToken cancellationToken) => RefreshHealthAsync(cancellationToken);

    protected override Task OnRequestsChangedAsync(CancellationToken cancellationToken) => RefreshCurrentAsync(cancellationToken);

    private Task RefreshHealthAsync(CancellationToken cancellationToken)
    {
        Health.Overview = null;
        return RefreshCurrentAsync(cancellationToken);
    }

    private Task RefreshCurrentAsync(CancellationToken cancellationToken) => Context.ProjectPath is { } project
        ? RefreshAsync(project, cancellationToken) : Task.CompletedTask;

    protected override async Task OnStopWorkAsync()
    {
        _generation++;
        _stop.Cancel();
        await Task.WhenAll(_reads).ConfigureAwait(true);
        _reads.Clear();
        _stop.Dispose();
        _stop = new CancellationTokenSource();
    }

    private Task RefreshAsync(string project, CancellationToken cancellationToken)
    {
        _reads.RemoveAll(task => task.IsCompletedSuccessfully);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var read = ReadAsync(project, ++_generation, linked);
        _reads.Add(read);
        return read;
    }

    private async Task ReadAsync(string project, int generation, CancellationTokenSource linked)
    {
        try
        {
            await Task.WhenAll(ReadOverviewAsync(project, generation, linked.Token),
                ReadStandingsAsync(project, generation, linked.Token)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally { linked.Dispose(); }
    }

    private bool IsCurrent(string project, int generation, CancellationToken cancellationToken) =>
        generation == _generation && !cancellationToken.IsCancellationRequested && Context.ProjectPath == project;

    private async Task ReadOverviewAsync(string project, int generation, CancellationToken cancellationToken)
    {
        var result = await Context.Commands.OverviewAsync(new OverviewRequest(project), cancellationToken).ConfigureAwait(true);
        if (IsCurrent(project, generation, cancellationToken)) Health.Overview = result.Succeeded ? result.Value : null;
    }

    private async Task ReadStandingsAsync(string project, int generation, CancellationToken cancellationToken)
    {
        var result = await Context.Commands.ProposalStandingsAsync(new ProposalStandingsRequest(project), cancellationToken)
            .ConfigureAwait(true);
        if (IsCurrent(project, generation, cancellationToken))
            WaitingProposals = result.Succeeded && result.Value is { Unreadable.Count: 0 } value
                ? value.Proposals.Count(card => card.Group == ProposalGroup.WaitingForYou) : null;
    }
}
