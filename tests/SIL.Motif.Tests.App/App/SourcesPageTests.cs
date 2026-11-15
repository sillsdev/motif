using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SourcesPageTests
{
    [Fact]
    public async Task SourceMetadataKeepsLocalCurrentSeparateFromContext()
    {
        var client = new FakeCommandClient();
        client.SourceRows = [Source("alpha")];
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project.fwdata");
        model.SelectedSource = Assert.Single(model.Sources);
        await model.Loading;
        Assert.Equal("MAT 1-3", model.ContextText);
        Assert.Equal("MAT 4", model.CurrentText);
        Assert.Equal(MotifAppStrings.SourcesExternalPassageUnavailable, model.PassageMessage);
        Assert.False(model.CanReadPassage);
        await model.StopAsync();
    }

    [Fact]
    public async Task AFailedDetailReadDisablesStagingUntilASuccessfulReload()
    {
        var client = new FakeCommandClient
        {
            SourceRows = [Source("alpha")],
            SourceShowHandler = (_, _) => Task.FromResult(CommandOutcome<SourceShowResponse>.Refused(
                new(RefusalCodes.GdrBaselineChanged, FailureReason.Refused, "Baseline changed"))),
        };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project.fwdata");
        Assert.True(model.AddParatextCommand.CanExecute(null));
        model.SelectedSource = Assert.Single(model.Sources);
        await model.Loading;
        Assert.False(model.AddParatextCommand.CanExecute(null));
        Assert.False(model.StageContextCommand.CanExecute(null));
        Assert.Equal(MotifAppStrings.SourcesOutdated, model.Notice);
        client.SourceShowHandler = null;
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.True(model.AddParatextCommand.CanExecute(null));
        Assert.True(model.StageContextCommand.CanExecute(null));
        await model.StopAsync();
    }

    [Fact]
    public async Task OlderSourceDetailCannotReplaceANewerChoiceEvenWhenCancellationIsIgnored()
    {
        var client = new FakeCommandClient { SourceRows = [Source("alpha"), Source("beta")] };
        var pending = new TaskCompletionSource<CommandOutcome<SourceShowResponse>>();
        client.SourceShowHandler = (request, _) => request.Id == "alpha" ? pending.Task
            : Task.FromResult(CommandOutcome<SourceShowResponse>.Success(new(Source("beta"), 3)));
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project.fwdata");
        model.SelectedSource = model.Sources[0];
        var old = model.Loading;
        model.SelectedSource = model.Sources[1];
        await model.Loading;
        pending.SetResult(CommandOutcome<SourceShowResponse>.Success(new(Source("alpha"), 3)));
        await old;
        Assert.Equal("beta", model.Detail!.Source.Id);
        await model.StopAsync();
    }

    [Theory]
    [InlineData("Removed", null)]
    [InlineData("Active", "Folder unavailable")]
    public async Task UnusableSourceHasNoCurrentOrContextActions(string status, string? error)
    {
        var client = new FakeCommandClient { SourceRows = [Source("alpha") with { Status = status, Error = error }] };
        var model = new SourcesViewModel(client, new SelectionViewModel(client));
        await model.LoadAsync("project.fwdata");
        model.SelectedSource = Assert.Single(model.Sources);
        await model.Loading;
        Assert.False(model.SetCurrentCommand.CanExecute(null));
        Assert.False(model.StageContextCommand.CanExecute(null));
        Assert.False(model.CanReadPassage);
        await model.StopAsync();
    }

    [Fact]
    public void SelectingExternalMetadataHidesTheFieldWorksPassageAndKeepsItsExactPlace()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (client, _) => client.SourceRows = [Source("external")]);
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);
                var page = workspace.PageModel<TextsPageModel>();
                await page.ResultsInText.SelectionRefresh;
                var prompt = window.PageView<TextsPage>().GetVisualDescendants().OfType<ParsePrompt>().Single();
                Assert.True(prompt.IsEffectivelyVisible);
                Assert.NotNull(workspace.Context.SelectionReads.Summary);
                var anchor = workspace.Context.SelectionReads.Summary.SourcePositions.First().Location.Anchor;
                page.Requested(new OpenPassageRequest(new(null, Occurrence: anchor)));
                var chosenText = page.ResultsInText.SelectedText!.TextId;
                page.Requested(new OpenPassageRequest(new("external", "MAT 2")));
                await page.Sources.Loading;
                PageScreenshots.Settle(window);
                var reader = window.PageView<TextsPage>().FindControl<ContentControl>("ReaderHost")!;
                Assert.False(reader.IsEffectivelyVisible);
                Assert.False(prompt.IsEffectivelyVisible);
                Assert.Equal("MAT 2", page.Sources.PassageReference);
                Assert.Equal("external", page.Sources.SelectedSource!.Id);
                Assert.Equal(chosenText, page.ResultsInText.SelectedText!.TextId);
                page.Requested(new OpenPassageRequest(new("missing", "MAT 1")));
                Assert.False(page.Sources.CanReadPassage);
                page.Requested(new OpenPassageRequest(new(null, Occurrence: anchor)));
                PageScreenshots.Settle(window);
                Assert.True(reader.IsEffectivelyVisible);
                Assert.True(prompt.IsEffectivelyVisible);
                Assert.Equal(anchor.TextId, page.ResultsInText.SelectedText!.TextId);
                page.Requested(new OpenPassageRequest(new(null, Occurrence: anchor with { TextId = Guid.NewGuid() })));
                PageScreenshots.Settle(window);
                Assert.False(reader.IsEffectivelyVisible);
                Assert.Equal(MotifAppStrings.SourcesPassageUnavailable, page.Sources.PassageMessage);
                page.Requested(new OpenPassageRequest(new(null, Occurrence: anchor)));
                PageScreenshots.Settle(window);
                Assert.True(reader.IsEffectivelyVisible);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    internal static SourceSummary Source(string id) => new(id, id, "Paratext", "Active", "English", "MAT 1-3",
        [new("MAT", "1-3")], "/private/source", null);
}
