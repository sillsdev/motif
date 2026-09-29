using System.Text.Json;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReviewUndoRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task RemoveAnalysisStagesAsRemovedAndUndoUsesTheRealClient()
    {
        using var project = new WalkthroughProject(pristine);
        SetTextParseCurrent(project);
        CaptureBaseline(project);
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        var changes = new ChangesViewModel(client);
        await changes.OpenProjectAsync(project.FwDataPath);

        await changes.RemoveAnalysisAsync(
            CanonicalId.FromGuid(project.Text.AnalysedWordformId).Value,
            SeededProject.AnalysedWordForm,
            CanonicalId.FromGuid(project.Text.ApprovedAnalysisId).Value);

        var removed = Assert.Single(changes.Items);
        Assert.Equal(ChangeKinds.RemoveAnalysis, removed.Kind);
        Assert.Equal("Remove analysis", removed.ReviewLabel);
        Assert.Equal("Removed", removed.StagedTransition.AfterApply);
        await changes.RemoveCommand.ExecuteAsync(removed);

        Assert.Empty(changes.Items);
        Assert.Empty(changes.Snapshot.Changes);
    }

    [Fact]
    public async Task AcceptNewSetGroupsRealChangesAndUndoRemovesTheWholeGroup()
    {
        using var project = new WalkthroughProject(pristine);
        SetTextParseCurrent(project);
        CaptureBaseline(project);
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        var assessmentId = RecordTextAssessment(project);
        var changes = new ChangesViewModel(client);
        await changes.OpenProjectAsync(project.FwDataPath);

        await changes.AcceptNewSetAsync(assessmentId, textId: project.Text.TextId);

        Assert.True(changes.Items.Count > 1);
        Assert.All(changes.Items, item => Assert.Equal(ChangeKinds.AddCandidate, item.Kind));
        Assert.All(changes.Items, item => Assert.Equal("Accepting a set", item.SourceText));
        var groupId = Assert.Single(changes.Items.Select(item => item.GroupId).Distinct());
        Assert.False(string.IsNullOrWhiteSpace(groupId));
        await changes.RemoveCommand.ExecuteAsync(changes.Items[0]);

        Assert.Empty(changes.Items);
        Assert.Empty(changes.Snapshot.Changes);
    }

    [Fact]
    public async Task UncertainChangesDisableApplyAfterARealRefresh()
    {
        using var project = new WalkthroughProject(pristine);
        AddSecondWord(project);
        CaptureBaseline(project);
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        var selection = new SelectionViewModel(client);
        var changes = new ChangesViewModel(client);
        var context = new WorkspaceContext(selection, new AssessViewModel(client, selection), changes, client,
            new FolderPicker(), new DragSource(), new BaselineViewModel(client));
        var review = new ReviewPageModel(context);
        await context.OpenProjectAsync(project.FwDataPath);
        var words = TextWordsQuery.Query(new TextWordsRequest(project.FwDataPath, [project.Text.TextId]));
        Assert.True(words.Succeeded, words.Refusal?.Message);
        var firstWord = words.Value!.Texts.Single().Lines
            .Single(line => line.ParagraphId == project.Text.FirstParagraphId).Tokens
            .Single(token => token.Form == SeededProject.AnalysedWordForm);
        await changes.PutAsync(new ChangeIntent(CanonicalId.Mint().Value, ChangeKinds.Reject,
            CanonicalId.FromGuid(project.Text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
            StoredAnalysisId: CanonicalId.FromGuid(project.Text.ApprovedAnalysisId).Value,
            OriginPage: WorkspacePage.Texts.ToString(),
            Occurrence: new OccurrenceAnchor(project.Text.TextId, project.Text.FirstParagraphId,
                project.Text.FirstSegmentId, firstWord.OccurrenceIndex)));

        EditSecondWord(project, "changedword");
        CaptureBaseline(project);
        await context.PublishBaselineCapturedAsync();

        Assert.True(Assert.Single(changes.Items).IsUncertain);
        Assert.False(review.CanApply);
        Assert.False(review.ApplyCommand.CanExecute(null));
        Assert.Equal("1 change needs another look because its sentence changed. Check it again or undo it.",
            review.ApplyBlockReason);
    }

    private static void CaptureBaseline(WalkthroughProject project)
    {
        var result = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath),
            project.ManagedRoot);
        Assert.True(result.Succeeded, result.Refusal?.Message);
    }

    private static void SetTextParseCurrent(WalkthroughProject project) =>
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
        {
            var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(project.Text.TextId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var paragraph in text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>())
                    paragraph.ParseIsCurrent = true;
            });
        });

    private static void AddSecondWord(WalkthroughProject project) =>
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(project.Text.FirstParagraphId);
            var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>()
                .GetObject(project.Text.FirstSegmentId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var secondWord = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("secondword", cache.DefaultVernWs));
                segment.AnalysesRS.Insert(1, secondWord);
                paragraph.Contents = TsStringUtils.MakeString(
                    $"{SeededProject.AnalysedWordForm} secondword.", cache.DefaultVernWs);
                paragraph.ParseIsCurrent = true;
            });
        });

    private static void EditSecondWord(WalkthroughProject project, string form) =>
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(project.Text.FirstParagraphId);
            var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>()
                .GetObject(project.Text.FirstSegmentId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var replacement = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(form, cache.DefaultVernWs));
                segment.AnalysesRS.RemoveAt(1);
                segment.AnalysesRS.Insert(1, replacement);
                paragraph.Contents = TsStringUtils.MakeString(
                    $"{SeededProject.AnalysedWordForm} {form}.", cache.DefaultVernWs);
                paragraph.ParseIsCurrent = true;
            });
        });

    private static string RecordTextAssessment(WalkthroughProject project)
    {
        var words = TextWordsQuery.Query(new TextWordsRequest(project.FwDataPath, [project.Text.TextId]));
        Assert.True(words.Succeeded, words.Refusal?.Message);
        var assessedWords = words.Value!.Texts.SelectMany(text => text.Lines)
            .SelectMany(line => line.Tokens)
            .Where(token => token.WordformId is not null && !string.IsNullOrEmpty(token.Form))
            .GroupBy(token => token.Form, StringComparer.Ordinal)
            .Select((group, index) => new AssessedWord(group.Key!, "analysed", [])
            {
                Morphology = new ParseWordEvidence("v1", 0, group.Key!, 1, false, false, false,
                    [new ParseAnalysis([new ParseMorph(null, null, null, $"accepted-{index}")])], []),
            }).ToArray();
        var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath),
            Path.GetFileNameWithoutExtension(project.FwDataPath));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        using var baselineCache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
        var repository = new AssessmentRepository(database);
        var composition = SelectionComposer.Compose(baselineCache,
            new SelectionRequest(false, [project.Text.TextId], [], false, null), repository,
            JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions()));
        Assert.True(composition.Succeeded, composition.Refusal?.Message);
        var assessmentId = "assessment/review/" + Guid.NewGuid().ToString("N");
        repository.Record(new NewAssessmentRecord(assessmentId, null, null, "test",
            AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope", "whitespace-and-punctuation", "1",
            JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions()), composition.Value!.Selection,
            "sha256:outcome", "sha256:semantic", "sha256:grammar", "model", "pipeline", 0, assessedWords,
            SavedUtc: DateTimeOffset.UtcNow.ToString("O")));
        return assessmentId;
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
