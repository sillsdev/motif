using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

internal static class LayoutInvariantStateFixtures
{
    internal static async Task VisitAsync(FakeCommandClient fake, WorkspaceShellViewModel workspace, Action<string> inspect)
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

        var timing = workspace.PageModel<TimingPageModel>();
        await Pending<TimingResponse>(WorkspacePage.Timing, task => fake.OnTiming((_, _) => task),
            () => timing.SelectWordSetCommand.ExecuteAsync("all"),
            () => timing.IsLoadingTiming, () => timing.HasTimingRefusal);

        var warnings = workspace.PageModel<WarningsPageModel>();
        await Pending<GrammarCheckResponse>(WorkspacePage.Warnings, task => fake.OnCheckGrammar((_, _) => task),
            () => warnings.CheckGrammarCommand.ExecuteAsync(null),
            () => warnings.Grammar.IsLoading, () => warnings.Grammar.ShowRefused);

        var handoff = workspace.PageModel<AiHandoffPageModel>().Handoff;
        await Pending<HandoffCommandResponse>(WorkspacePage.AiHandoff, task => fake.OnHandoff((_, _, _) => task),
            () => handoff.RunCommand.ExecuteAsync(null), () => handoff.IsActive, () => handoff.ShownRefusal is not null);

        workspace.CurrentPage = WorkspacePage.Review;
        fake.PendingLoadHandler = (_, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Refused(refusal));
        await workspace.Context.Changes.ReloadAsync();
        Assert.True(workspace.Context.Changes.HasError);
        inspect("Review/refused");

        var selection = workspace.Context.SelectionReads;
        await Pending<SelectionReader>(WorkspacePage.Texts, task => fake.SelectionReaderHandler = (_, _) => task,
            () => selection.ReloadAsync(workspace.Context.ProjectPath!, workspace.Selection.ChosenTextIds,
                workspace.Selection.PastedWordEntries), () => selection.IsLoading, () => selection.Refusal is not null);

        workspace.CurrentPage = WorkspacePage.Timing;
        var inspection = new TaskCompletionSource<CommandOutcome<InspectResponse>>();
        fake.OnInspect((_, _) => inspection.Task);
        workspace.Context.OpenInspector(InspectorSubject.Morpheme("a-form", "msa-a", "a-")!);
        try
        {
            Assert.True(workspace.Inspector.IsLoading);
            inspect("Timing/inspector-loading");
        }
        finally { inspection.SetResult(CommandOutcome<InspectResponse>.Refused(refusal)); await workspace.Inspector.Loading; }
        Assert.True(workspace.Inspector.HasMessage);
        inspect("Timing/inspector-refused");
        workspace.Inspector.Close();

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
