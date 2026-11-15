using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

internal static class LayoutInvariantStateFixtures
{
    internal static async Task VisitAsync(FakeCommandClient fake, WorkspaceShellViewModel workspace, SettingsViewModel settings,
        Action<string> inspect)
    {
        var refusal = new Refusal(RefusalCodes.StoreOtherVersion, FailureReason.Refused, "Store belongs to another version.");
        var proposals = workspace.PageModel<ProposalsPageModel>();
        await Pending<ProposalStandings>(WorkspacePage.Proposals,
            task => fake.OnProposalStandings((_, _) => task),
            () =>
            {
                proposals.ProjectCleared();
                return proposals.ProjectOpenedAsync(workspace.Context.ProjectPath!, CancellationToken.None);
            }, () => proposals.IsLoading, () => proposals.ShownRefusal is not null);

        settings.ShowHandoffByCopyingFiles = true;
        var handoff = workspace.PageModel<SettingsPageModel>().Handoff.Handoff;
        await Pending<HandoffCommandResponse>(WorkspacePage.Settings, task => fake.OnHandoff((_, _, _) => task),
            () => handoff.RunCommand.ExecuteAsync(null), () => handoff.IsActive, () => handoff.ShownRefusal is not null);

        workspace.CurrentPage = WorkspacePage.Proposals;
        fake.PendingLoadHandler = (_, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Refused(refusal));
        await workspace.Context.Changes.ReloadAsync();
        Assert.True(workspace.Context.Changes.HasError);
        inspect("Draft/refused");

        var selection = workspace.Context.SelectionReads;
        await Pending<SelectionReader>(WorkspacePage.Texts, task => fake.SelectionReaderHandler = (_, _) => task,
            () => selection.ReloadAsync(workspace.Context.ProjectPath!, workspace.Selection.ChosenTextIds,
                workspace.Selection.PastedWordEntries), () => selection.IsLoading, () => selection.Refusal is not null);

        async Task Pending<T>(WorkspacePage page, Action<Task<CommandOutcome<T>>> install, Func<Task> start,
            Func<bool> loading, Func<bool> refused) where T : class
        {
            workspace.CurrentPage = page;
            var completion = new TaskCompletionSource<CommandOutcome<T>>();
            install(completion.Task);
            var operation = start();
            try
            {
                Assert.True(loading(), $"{page} did not enter its real loading state.");
                inspect($"{page}/loading");
            }
            finally { completion.SetResult(CommandOutcome<T>.Refused(refusal)); await operation; }
            Assert.True(refused(), $"{page} did not display the command refusal.");
            inspect($"{page}/refused");
        }
    }
}
