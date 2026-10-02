using Microsoft.Data.Sqlite;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Host.Store;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Worker.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed partial class ReviewUndoRealClientTests
{
    [Theory]
    [InlineData("checked")]
    [InlineData("text")]
    [InlineData("row")]
    [InlineData("non-fitting")]
    [InlineData("visible-group")]
    public async Task GroupedHomographRemovalUsesTheRealStoreWithoutRemovingItsNeighbour(string path)
    {
        using var project = new WalkthroughProject(pristine);
        var otherId = AddHomograph(project);
        CaptureBaseline(project);
        var real = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath,
            IsolatedRunner.Process(project.ManagedRoot));
        var changes = new ChangesViewModel(real);
        await changes.OpenProjectAsync(project.FwDataPath);
        await changes.AcceptNewSetAsync(RecordTextAssessment(project), textId: project.Text.TextId);
        Assert.Null(changes.LastRefusal);
        var first = Assert.Single(changes.Items, item =>
            item.WordformId == CanonicalId.FromGuid(project.Text.AnalysedWordformId).Value);
        var other = Assert.Single(changes.Items, item => item.WordformId == CanonicalId.FromGuid(otherId).Value);
        Assert.Equal(first.GroupId, other.GroupId);
        var response = TextWordsQuery.Query(new TextWordsRequest(project.FwDataPath, [project.Text.TextId])).Value!;
        if (path == "text") response = response with
        {
            Texts = response.Texts.Select(text => text with
            {
                Lines = text.Lines.Select(line => line with
                {
                    Tokens = line.Tokens.Where(token => token.WordformId != otherId).ToArray(),
                }).ToArray(),
            }).ToArray(),
        };
        var fake = IdentityClient(project, response);
        fake.PendingLoadHandler = async (request, cancellation) =>
        {
            var loaded = await real.LoadPendingChangesAsync(request, cancellation);
            return path is not ("non-fitting" or "visible-group") ? loaded : CommandOutcome<PendingChangesSnapshot>.Success(loaded.Value! with
            {
                FitSummary = loaded.Value!.Changes.Select(item =>
                    new ChangeFit(item.ChangeId, item.ChangeId != first.ChangeId, [])).ToArray(),
            });
        };
        fake.PendingRemoveHandler = real.RemovePendingChangeAsync;
        var context = WorkspaceContextTests.NewContext(fake);
        var texts = new TextsPageModel(context);
        var review = new ReviewPageModel(context);
        await context.OpenProjectAsync(project.FwDataPath);
        await texts.Words.ReloadAsync();
        var ownToken = texts.ResultsInText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .First(token => token.WordformId == project.Text.AnalysedWordformId);
        ownToken.IsSelectedForActions = true;

        switch (path)
        {
            case "checked": await texts.ResultsInText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords); break;
            case "text": await texts.ResultsInText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.SelectedText); break;
            case "row": await context.Changes.RemoveCommand.ExecuteAsync(context.Changes.Items.Single(item => item.ChangeId == first.ChangeId)); break;
            case "non-fitting": await review.RemoveNonFittingCommand.ExecuteAsync(null); break;
            case "visible-group": await review.ReviewGroups.Single(group => group.IsNoLongerFits).UndoAllCommand.ExecuteAsync(null); break;
        }

        Assert.DoesNotContain(fake.PendingRemoveRequests, request => request.ChangeId == first.GroupId);
        Assert.Contains(context.Changes.Items, item => item.ChangeId == other.ChangeId);
        var persisted = await real.LoadPendingChangesAsync(new PendingChangesRequest(project.FwDataPath,
            MotifProductVersion.CurrentText), CancellationToken.None);
        Assert.Contains(persisted.Value!.Changes, item => item.ChangeId == other.ChangeId);
        Assert.DoesNotContain(persisted.Value.Changes, item => item.ChangeId == first.ChangeId);
        Assert.False(ownToken.IsPending);
        if (path != "text") Assert.True(texts.ResultsInText.Texts.SelectMany(text => text.Lines)
            .SelectMany(line => line.Tokens).Single(token => token.WordformId == otherId).IsPending);
        await texts.ResultsInText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.ChosenTexts);
        if (path == "text") Assert.Contains(context.Changes.Items, item => item.ChangeId == other.ChangeId);
        else Assert.Empty(context.Changes.Items);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public async Task RefusedRealUndoReloadsThePersistedHomographChoice(string kind)
    {
        using var project = new WalkthroughProject(pristine);
        var otherId = AddHomograph(project);
        CaptureBaseline(project);
        var real = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        await StageHomographChoice(project, real, otherId, kind);
        var fake = IdentityClient(project);
        fake.PendingRemoveHandler = (request, cancellation) => real.RemovePendingChangeAsync(
            request with { ExpectedRevision = "stale-revision" }, cancellation);
        var context = WorkspaceContextTests.NewContext(fake);
        var texts = new TextsPageModel(context);
        await context.OpenProjectAsync(project.FwDataPath);
        await texts.Words.ReloadAsync();
        var choice = Assert.Single(context.Changes.Items);

        await context.Changes.RemoveCommand.ExecuteAsync(choice);

        Assert.Equal(RefusalCodes.ChangeRevisionConflict, context.Changes.LastRefusal?.Code);
        Assert.Equal(choice.ChangeId, Assert.Single(context.Changes.Items).ChangeId);
        var persisted = PendingChanges.Load(new PendingChangesRequest(project.FwDataPath, MotifProductVersion.CurrentText));
        Assert.Equal(choice.ChangeId, Assert.Single(persisted.Value!.Changes).ChangeId);
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, true);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public async Task ClearResetAndReopenKeepPersistedHomographChoicesBoundToTheirIdentities(string kind)
    {
        using var project = new WalkthroughProject(pristine);
        var otherId = AddHomograph(project);
        CaptureBaseline(project);
        var real = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath,
            IsolatedRunner.Process(project.ManagedRoot));
        await StageHomographChoice(project, real, otherId, kind);
        var fake = IdentityClient(project);
        var context = WorkspaceContextTests.NewContext(fake);
        var texts = new TextsPageModel(context);
        await context.OpenProjectAsync(project.FwDataPath);
        await texts.Words.ReloadAsync();
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, true);

        context.Changes.Reset();
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, false);
        await context.Changes.ReloadAsync();
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, true);
        context.ClearProject();
        Assert.Empty(context.Changes.Items);
        Assert.Empty(texts.ResultsInText.Texts);
        await context.OpenProjectAsync(project.FwDataPath);
        await texts.Words.ReloadAsync();

        Assert.Single(context.Changes.Items);
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, true);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling, false)]
    [InlineData(ChangeKinds.IncorrectSpelling, true)]
    [InlineData(ChangeKinds.AddCandidate, false)]
    [InlineData(ChangeKinds.AddCandidate, true)]
    public async Task ApplyReloadReflectsTheRealHomographDraftAndPreservesChoicesOnRefusal(string kind, bool refuse)
    {
        using var project = new WalkthroughProject(pristine);
        var otherId = AddHomograph(project);
        CaptureBaseline(project);
        var real = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath,
            IsolatedRunner.Process(project.ManagedRoot));
        await StageHomographChoice(project, real, otherId, kind);
        var fake = IdentityClient(project);
        var context = WorkspaceContextTests.NewContext(fake);
        var texts = new TextsPageModel(context);
        var review = new ReviewPageModel(context);
        await context.OpenProjectAsync(project.FwDataPath);
        await texts.Words.ReloadAsync();
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, true);
        var measured = await real.MeasurePendingAsync(new MeasurePendingRequest(project.FwDataPath,
            context.Changes.Snapshot.DraftId, context.Changes.Snapshot.Revision, [SeededProject.AnalysedWordForm]),
            new Progress<MeasureProgress>(), CancellationToken.None);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        fake.MeasurePendingCompletesWith(measured.Value!);
        fake.ApplyPendingHandler = (request, cancellation) => real.ApplyPendingAsync(refuse
            ? request with { Revision = "stale-revision" } : request, cancellation);
        await review.MeasureCommand.ExecuteAsync(null);
        Assert.True(review.CanApply, review.ApplyBlockReason);

        await review.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(refuse, review.ApplyRefusal is not null);
        var persisted = await real.LoadPendingChangesAsync(new PendingChangesRequest(project.FwDataPath,
            MotifProductVersion.CurrentText), CancellationToken.None);
        Assert.True(persisted.Succeeded, persisted.Refusal?.Message);
        Assert.Equal(refuse ? 1 : 0, persisted.Value!.Changes.Count);
        Assert.Equal(refuse ? 1 : 0, context.Changes.Items.Count);
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, refuse);
        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath);
        var own = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(project.Text.AnalysedWordformId);
        var other = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(otherId);
        Assert.NotEqual(2, own.SpellingStatus);
        if (kind == ChangeKinds.IncorrectSpelling)
            Assert.Equal(refuse ? 0 : 2,
                other.SpellingStatus);
        else Assert.Equal(refuse ? 0 : 1, other.AnalysesOC.Count);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public async Task RefusedStoreDeletionAndReopenRemoveActualPendingHomographs(string kind)
    {
        using var project = new WalkthroughProject(pristine);
        var otherId = AddHomograph(project);
        CaptureBaseline(project);
        var real = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath,
            IsolatedRunner.Process(project.ManagedRoot));
        await StageHomographChoice(project, real, otherId, kind);
        var fake = IdentityClient(project);
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, new NoIdentityProjectPicker()),
            new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
            new FolderPicker(), new DragSource(), fake);
        var texts = new TextsPageModel(workspace.Context);
        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(project.FwDataPath));
        await texts.Words.ReloadAsync();
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, true);
        var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath), Path.GetFileNameWithoutExtension(project.FwDataPath));
        var store = ProjectDatabaseCatalog.DatabasePathFor(locator);
        using (var connection = new SqliteConnection($"Data Source={store};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};";
            command.ExecuteNonQuery();
        }
        await workspace.Context.Changes.ReloadAsync();
        Assert.Equal(RefusalCodes.StoreOtherVersion, workspace.Context.Changes.LastRefusal?.Code);
        AssertHomographMarkers(texts, project.Text.AnalysedWordformId, otherId, true);
        fake.CurrentBaselineRefusesWith(workspace.Context.Changes.LastRefusal!);
        await workspace.Baseline.CheckAsync();
        var bytes = File.ReadAllBytes(project.FwDataPath);
        fake.OnDeleteRefusedStore((request, _) =>
        {
            var deleted = ProjectStoreReset.DeleteRefused(request);
            Assert.True(deleted.Succeeded, deleted.Refusal?.Message);
            Assert.True(deleted.Value!.Deleted);
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
            fake.ListTextWordsCompletesWith(new TextWordsResponse([], [], false));
            return Task.FromResult(deleted);
        });

        workspace.DeleteRefusedStoreCommand.Execute(workspace.Baseline.ShownRefusal);
        Assert.True(workspace.IsConfirmingStoreDeletion);
        await workspace.ConfirmStoreDeletionCommand.ExecuteAsync(null);

        Assert.Single(fake.DeleteRefusedStoreRequests);
        Assert.Null(workspace.Context.Changes.LastRefusal);
        Assert.Empty(workspace.Context.Changes.Items);
        Assert.Empty(texts.ResultsInText.Texts);
        Assert.Equal(bytes, File.ReadAllBytes(project.FwDataPath));
        var persisted = PendingChanges.Load(new PendingChangesRequest(project.FwDataPath, MotifProductVersion.CurrentText));
        Assert.True(persisted.Succeeded, persisted.Refusal?.Message);
        Assert.Empty(persisted.Value!.Changes);
    }

    private static FakeCommandClient IdentityClient(WalkthroughProject project, TextWordsResponse? response = null)
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.ListTextWordsCompletesWith(response ?? TextWordsQuery.Query(new TextWordsRequest(project.FwDataPath,
            [project.Text.TextId])).Value!);
        fake.PendingLoadHandler = (request, _) => Task.FromResult(PendingChanges.Load(request));
        return fake;
    }

    private static async Task StageHomographChoice(WalkthroughProject project, SIL.Motif.App.Services.ICommandClient real,
        Guid wordformId, string kind)
    {
        var changes = new ChangesViewModel(real);
        await changes.OpenProjectAsync(project.FwDataPath);
        if (kind == ChangeKinds.AddCandidate)
            await changes.AcceptNewSetAsync(RecordTextAssessment(project), CanonicalId.FromGuid(wordformId).Value);
        else await changes.PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind,
            CanonicalId.FromGuid(wordformId).Value, SeededProject.AnalysedWordForm));
        Assert.Null(changes.LastRefusal);
        Assert.Single(changes.Items);
    }

    private static void AssertHomographMarkers(TextsPageModel texts, Guid ownId, Guid otherId, bool pending)
    {
        var tokens = texts.ResultsInText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens).ToArray();
        var own = tokens.First(token => token.WordformId == ownId);
        var other = tokens.Single(token => token.WordformId == otherId);
        Assert.False(own.IsPending);
        Assert.Empty(own.StagedChanges);
        Assert.Equal(pending, other.IsPending);
        Assert.Equal(pending ? 1 : 0, other.StagedChanges.Count);
    }

    private static Guid AddHomograph(WalkthroughProject project)
    {
        var id = Guid.Empty;
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create();
                wordform.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString(SeededProject.AnalysedWordForm,
                    cache.DefaultVernWs));
                id = wordform.Guid;
                var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(project.Text.FirstSegmentId);
                segment.AnalysesRS.Insert(1, wordform);
                var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(project.Text.FirstParagraphId);
                paragraph.Contents = TsStringUtils.MakeString($"{SeededProject.AnalysedWordForm} {SeededProject.AnalysedWordForm}.",
                    cache.DefaultVernWs);
                var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(project.Text.TextId);
                foreach (var para in text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>()) para.ParseIsCurrent = true;
            }));
        return id;
    }

    private sealed class NoIdentityProjectPicker : SIL.Motif.App.Services.IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}
