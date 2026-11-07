using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract;
using SIL.Motif.Host;
using SIL.Motif.Commands.Requests;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalsPageModel
{
    private IAsyncRelayCommand<OpenRequestRowViewModel>? _withdrawRequestCommand;
    private IAsyncRelayCommand<ProposalCardViewModel>? _askAgainCommand;
    private IAsyncRelayCommand? _showMoreRequestsCommand;
    private int _requestGeneration;

    /// <summary>The Requests nobody has answered yet, shown above the Ready Proposals.</summary>
    public OpenRequestsViewModel Requests { get; } = new();

    /// <summary>Takes an open Request back.</summary>
    public IAsyncRelayCommand<OpenRequestRowViewModel> WithdrawRequestCommand =>
        _withdrawRequestCommand ??= new AsyncRelayCommand<OpenRequestRowViewModel>(WithdrawRequestAsync);

    /// <summary>Makes a new Request from a declined Proposal's Request.</summary>
    public IAsyncRelayCommand<ProposalCardViewModel> AskAgainCommand =>
        _askAgainCommand ??= new AsyncRelayCommand<ProposalCardViewModel>(AskAgainAsync);

    /// <summary>Loads the next page of open Requests.</summary>
    public IAsyncRelayCommand ShowMoreRequestsCommand => _showMoreRequestsCommand ??=
        new AsyncRelayCommand(() => Context.ProjectPath is { } path
            ? LoadRequestsAsync(path, CancellationToken.None, append: true) : Task.CompletedTask);

    private async Task LoadRequestsAsync(string projectPath, CancellationToken cancellationToken, bool append = false)
    {
        var generation = ++_requestGeneration;
        var read = await Context.Commands.ListRequestsAsync(
            new ListRequestsRequest(projectPath, MotifProductVersion.CurrentText, "open", append ? Requests.Rows.Count : 0, 200),
            cancellationToken).ConfigureAwait(true);
        if (projectPath != Context.ProjectPath || generation != _requestGeneration) return;
        if (!read.Succeeded) { ShownRefusal = WindowRefusal.From(read.Refusal!); return; }
        Requests.Show(read.Value!.Requests, Context.Clock.GetUtcNow(), append, read.Value.Total);
    }

    protected override Task OnRequestsChangedAsync(CancellationToken cancellationToken) =>
        Context.ProjectPath is { } path ? LoadRequestsAsync(path, cancellationToken) : Task.CompletedTask;

    private async Task WithdrawRequestAsync(OpenRequestRowViewModel? row)
    {
        if (row is null || Context.ProjectPath is not { } path) return;
        var withdrawn = await Context.Commands.WithdrawRequestAsync(
            new WithdrawRequestRequest(path, MotifProductVersion.CurrentText, row.RequestId),
            CancellationToken.None).ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        ShownRefusal = withdrawn.Succeeded ? null : WindowRefusal.From(withdrawn.Refusal!);
        await LoadRequestsAsync(path, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task AskAgainAsync(ProposalCardViewModel? card)
    {
        if (card is null || !card.CanAskAgain || Context.ProjectPath is not { } path) return;
        var asked = await Context.Commands.AskAgainAsync(
            new AskAgainRequest(path, MotifProductVersion.CurrentText, card.ProposalId),
            CancellationToken.None).ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        ShownRefusal = asked.Succeeded ? null : WindowRefusal.From(asked.Refusal!);
        await LoadRequestsAsync(path, CancellationToken.None).ConfigureAwait(true);
    }
}
