using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class SourcePassageNavigationTests
{
    [Fact]
    public async Task SwitchingProjectDropsLateSourceRowsAndLocalInput()
    {
        var old = new TaskCompletionSource<CommandOutcome<SourceListResponse>>();
        var client = new FakeCommandClient
        {
            ListSourcesHandler = (request, _) => request.ProjectPath == "old" ? old.Task
                : Task.FromResult(CommandOutcome<SourceListResponse>.Success(new([SourcesPageTests.Source("new")]))),
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        var loading = model.LoadAsync("old");
        model.NewFolder = "/private/old";
        await model.LoadAsync("new");
        old.SetResult(CommandOutcome<SourceListResponse>.Success(new([SourcesPageTests.Source("old")])));
        await loading;
        Assert.Equal("new", Assert.Single(model.Sources).Id);
        Assert.Empty(model.NewFolder);
        await model.StopAsync();
    }

    [Fact]
    public async Task ContextIsStagedWhileCurrentIsLocalAndNeitherChangesTheSavedContext()
    {
        var reloads = 0;
        var client = new FakeCommandClient
        {
            SourceRows = [SourcesPageTests.Source("source")],
            StageSourceContextHandler = (_, _) => Task.FromResult(CommandOutcome<SourceStageResponse>.Success(new("source", "pending", []))),
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client), _ => { reloads++; return Task.CompletedTask; });
        await model.LoadAsync("project");
        model.SelectedSource = Assert.Single(model.Sources);
        await model.Loading;
        model.ContextText = "MAT 5";
        await model.StageContextCommand.ExecuteAsync(null);
        Assert.Equal("MAT 5", Assert.Single(client.StageSourceContextRequests).Context);
        Assert.Equal("MAT 1-3", model.Detail!.Source.Context);
        Assert.Equal(1, reloads);
        Assert.Equal(MotifAppStrings.SourcesStaged, model.Notice);
        Assert.Empty(client.SetSourceCurrentRequests);
        model.CurrentText = "MAT 6";
        await model.SetCurrentCommand.ExecuteAsync(null);
        Assert.Equal("MAT 6", Assert.Single(client.SetSourceCurrentRequests).Ref);
        Assert.Equal(1, reloads);
        await model.StopAsync();
    }

    [Fact]
    public async Task StaleSourceMetadataCannotEnableWritesOrShowAnExternalReader()
    {
        var client = new FakeCommandClient
        {
            ListSourcesHandler = (_, _) => Task.FromResult(CommandOutcome<SourceListResponse>.Success(
                new([SourcesPageTests.Source("source")]) { IsStale = true })),
            SourceRows = [SourcesPageTests.Source("source")],
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.SelectedSource = Assert.Single(model.Sources);
        await model.Loading;
        Assert.False(model.StageContextCommand.CanExecute(null));
        Assert.False(model.AddParatextCommand.CanExecute(null));
        Assert.False(model.CanReadPassage);
        model.ShowFieldWorksCommand.Execute(null);
        Assert.True(model.CanReadPassage);
        await model.StopAsync();
    }

    [Fact]
    public async Task ARefusedCurrentDoesNotStageAProposalOrReplaceItsContext()
    {
        var client = new FakeCommandClient
        {
            SourceRows = [SourcesPageTests.Source("source")],
            SetSourceCurrentHandler = (_, _) => Task.FromResult(CommandOutcome<SourceLocalResponse>.Refused(
                new Refusal(RefusalCodes.SourceRefInvalid, FailureReason.InvalidArgument, "Invalid chapter"))),
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.SelectedSource = Assert.Single(model.Sources);
        await model.Loading;
        model.CurrentText = "invalid";
        await model.SetCurrentCommand.ExecuteAsync(null);
        Assert.Equal(MotifAppStrings.SourcesInvalidChapter, model.ShownRefusal!.Sentence);
        Assert.Empty(client.StageSourceContextRequests);
        Assert.Equal("MAT 1-3", model.ContextSummary);
        await model.StopAsync();
    }

    [Fact]
    public async Task AContextReloadInvalidatesTheEarlierDetailAndPreservesTheSourceChoice()
    {
        var old = new TaskCompletionSource<CommandOutcome<SourceShowResponse>>();
        var calls = 0;
        var client = new FakeCommandClient
        {
            SourceRows = [SourcesPageTests.Source("source")],
            SourceShowHandler = (_, _) => ++calls == 1 ? old.Task
                : Task.FromResult(CommandOutcome<SourceShowResponse>.Success(
                    new(SourcesPageTests.Source("source") with { Context = "MAT 5" }, 1))),
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.SelectedSource = Assert.Single(model.Sources);
        var reading = model.Loading;
        await model.LoadAsync("project");
        await model.Loading;
        old.SetResult(CommandOutcome<SourceShowResponse>.Success(new(SourcesPageTests.Source("source"), 3)));
        await reading;
        Assert.Equal("MAT 5", model.ContextSummary);
        Assert.Equal("source", model.SelectedSource!.Id);
        await model.StopAsync();
    }

    [Fact]
    public async Task ALocalWriteCompletingAfterASourceSwitchCannotPublishTheOldCurrent()
    {
        var saved = new TaskCompletionSource<CommandOutcome<SourceLocalResponse>>();
        var client = new FakeCommandClient
        {
            SourceRows = [SourcesPageTests.Source("first"), SourcesPageTests.Source("second")],
            SetSourceCurrentHandler = (_, _) => saved.Task,
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.SelectedSource = model.Sources[0];
        await model.Loading;
        model.CurrentText = "MAT 6";
        var writing = model.SetCurrentCommand.ExecuteAsync(null);
        model.SelectedSource = model.Sources[1];
        await model.Loading;
        saved.SetResult(CommandOutcome<SourceLocalResponse>.Success(new("first", null, "MAT 6")));
        await writing;
        Assert.Equal("second", model.Detail!.Source.Id);
        Assert.Equal("MAT 4", model.CurrentText);
        Assert.Null(model.Notice);
        await model.StopAsync();
    }

    [Fact]
    public async Task AFailedCurrentReadCannotEnableActionsFromAnOlderDetail()
    {
        var client = new FakeCommandClient
        {
            SourceRows = [SourcesPageTests.Source("source")],
            GetSourceCurrentHandler = (_, _) => Task.FromResult(CommandOutcome<CurrentGetResponse>.Refused(
                new Refusal(RefusalCodes.GdrBaselineChanged, FailureReason.Refused, "Baseline changed"))),
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.SelectedSource = Assert.Single(model.Sources);
        await model.Loading;
        Assert.False(model.StageContextCommand.CanExecute(null));
        Assert.False(model.SetCurrentCommand.CanExecute(null));
        Assert.NotNull(model.ShownRefusal);
        await model.StopAsync();
    }

    [Fact]
    public async Task ReloadingExternalMetadataKeepsItsPassageAndNeverFlashesTheFieldWorksReader()
    {
        var client = new FakeCommandClient { SourceRows = [SourcesPageTests.Source("source")] };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.Navigate(new("source", "MAT 2"));
        await model.Loading;
        var pending = new TaskCompletionSource<CommandOutcome<SourceListResponse>>();
        client.ListSourcesHandler = (_, _) => pending.Task;
        var reloading = model.LoadAsync("project");
        Assert.False(model.CanReadPassage);
        Assert.False(model.AddParatextCommand.CanExecute(null));
        pending.SetResult(CommandOutcome<SourceListResponse>.Success(new(client.SourceRows)));
        await reloading;
        Assert.Equal("source", model.SelectedSource!.Id);
        Assert.Equal("MAT 2", model.PassageReference);
        Assert.False(model.CanReadPassage);
        await model.StopAsync();
    }

    [Fact]
    public async Task ChoosingFieldWorksDuringASourceReloadKeepsTheReaderSelectedAfterTheListArrives()
    {
        var client = new FakeCommandClient { SourceRows = [SourcesPageTests.Source("source")] };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.Navigate(new("source", "MAT 2"));
        await model.Loading;
        var pending = new TaskCompletionSource<CommandOutcome<SourceListResponse>>();
        client.ListSourcesHandler = (_, _) => pending.Task;
        var reloading = model.LoadAsync("project");
        model.ShowFieldWorksCommand.Execute(null);
        Assert.True(model.CanReadPassage);
        pending.SetResult(CommandOutcome<SourceListResponse>.Success(new(client.SourceRows)));
        await reloading;
        Assert.Equal("source", Assert.Single(model.Sources).Id);
        Assert.Null(model.SelectedSource);
        Assert.True(model.IsFieldWorks);
        Assert.True(model.CanReadPassage);
        Assert.Null(model.PassageReference);
        Assert.Single(client.ShowSourceRequests);
        Assert.Single(client.WhereSourceRequests);
        await model.StopAsync();
    }

    [Theory]
    [InlineData("source", "MAT 3")]
    [InlineData("second", "MAT 1")]
    public async Task ANewerPassageDuringASourceReloadKeepsItsLocatorAfterTheListArrives(string source, string passage)
    {
        var client = new FakeCommandClient
        {
            SourceRows = [SourcesPageTests.Source("source"), SourcesPageTests.Source("second")],
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.Navigate(new("source", "MAT 2"));
        await model.Loading;
        var pending = new TaskCompletionSource<CommandOutcome<SourceListResponse>>();
        client.ListSourcesHandler = (_, _) => pending.Task;
        var reloading = model.LoadAsync("project");
        model.Navigate(new(source, passage));
        pending.SetResult(CommandOutcome<SourceListResponse>.Success(new(client.SourceRows)));
        await reloading;
        Assert.Equal(2, model.Sources.Count);
        Assert.Equal(source, model.SelectedSource!.Id);
        Assert.Equal(passage, model.PassageReference);
        Assert.False(model.CanReadPassage);
        Assert.Equal(new[] { "MAT 2", passage }, client.WhereSourceRequests.Select(request => request.Ref));
        Assert.Equal(source, client.WhereSourceRequests.Last().Source);
        await model.StopAsync();
    }

    [Fact]
    public async Task SourceStagingNeverWritesIntoAnotherProposalsTarget()
    {
        var pending = false;
        var client = new FakeCommandClient { SourceRows = [SourcesPageTests.Source("source")] };
        var model = new SourcesViewModel(client, new SelectionViewModel(client), canStage: () => pending,
            usePending: _ => { pending = true; return Task.CompletedTask; });
        await model.LoadAsync("project");
        model.SelectedSource = Assert.Single(model.Sources);
        await model.Loading;
        Assert.False(model.StageContextCommand.CanExecute(null));
        Assert.False(model.AddParatextCommand.CanExecute(null));
        await model.UsePendingProposalCommand.ExecuteAsync(null);
        model.RefreshStagingTarget();
        Assert.True(model.StageContextCommand.CanExecute(null));
        Assert.True(model.AddParatextCommand.CanExecute(null));
        Assert.Empty(client.StageSourceContextRequests);
        Assert.Empty(client.StageSourceAddRequests);
        await model.StopAsync();
    }

    [Fact]
    public async Task AChangedBaselineWhileLocatingAPassageDisablesMutationsUntilReload()
    {
        var client = new FakeCommandClient
        {
            SourceRows = [SourcesPageTests.Source("source")],
            WhereSourceHandler = (_, _) => Task.FromResult(CommandOutcome<SourceWhereResponse>.Refused(
                new Refusal(RefusalCodes.GdrBaselineChanged, FailureReason.Refused, "Baseline changed"))),
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.Navigate(new("source", "MAT 2"));
        await model.Loading;
        Assert.Null(model.PassageReference);
        Assert.Equal(MotifAppStrings.SourcesOutdated, model.Notice);
        Assert.False(model.AddParatextCommand.CanExecute(null));
        Assert.False(model.SetCurrentCommand.CanExecute(null));
        Assert.False(model.StageContextCommand.CanExecute(null));
        client.WhereSourceHandler = null;
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.True(model.AddParatextCommand.CanExecute(null));
        await model.StopAsync();
    }

    [Fact]
    public async Task AnInvalidExternalChapterIsRefusedByTheEngineWithoutChangingCurrent()
    {
        var client = new FakeCommandClient
        {
            SourceRows = [SourcesPageTests.Source("source")],
            WhereSourceHandler = (_, _) => Task.FromResult(CommandOutcome<SourceWhereResponse>.Refused(
                new Refusal(RefusalCodes.SourceRefInvalid, FailureReason.InvalidArgument, "Invalid chapter"))),
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project");
        model.Navigate(new("source", "invalid"));
        await model.Loading;
        Assert.Equal("invalid", Assert.Single(client.WhereSourceRequests).Ref);
        Assert.Equal(MotifAppStrings.SourcesInvalidChapter, model.ShownRefusal!.Sentence);
        Assert.Null(model.PassageReference);
        Assert.Equal("MAT 4", model.CurrentText);
        Assert.Empty(client.SetSourceCurrentRequests);
        Assert.True(model.AddParatextCommand.CanExecute(null));
        Assert.True(model.StageContextCommand.CanExecute(null));
        await model.StopAsync();
    }

    [Fact]
    public void PassageLocatorKeepsSourceAndOccurrenceIdentity()
    {
        var anchor = new OccurrenceAnchor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4);
        var fieldWorks = new PassageChoiceViewModel(null, Occurrence: anchor);
        var external = new PassageChoiceViewModel("source", "MAT 4");
        Assert.Equal(anchor, fieldWorks.Occurrence);
        Assert.Equal("source", external.SourceId);
        Assert.Equal("MAT 4", external.Passage);
        Assert.Null(external.Occurrence);
    }
}
