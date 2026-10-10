using Avalonia.Controls;
using System.Runtime.CompilerServices;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed partial class ReviewUndoRealClientTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void AddedWordCardsRetainReaderCapabilitiesAndDisplayWithoutInventingAnOccurrence(
        bool ambiguous, bool incorrect, bool unrelatedText)
    {
        using var project = new WalkthroughProject(pristine);
        if (ambiguous) AddHomograph(project);
        var unrelatedId = Guid.Empty;
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                if (incorrect) cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
                    .GetObject(project.Text.AnalysedWordformId).SpellingStatus = 2;
                if (unrelatedText)
                {
                    var text = cache.ServiceLocator.GetInstance<ITextFactory>().Create();
                    text.Name.set_String(cache.DefaultAnalWs, "Unrelated");
                    text.ContentsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                    unrelatedId = text.Guid;
                }
            }));
        CaptureBaseline(project);
        var root = RecordTextAssessment(project);
        var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath),
            Path.GetFileNameWithoutExtension(project.FwDataPath));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!.Token;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var client = new FakeCommandClient { SelectionReaderHandler = SelectionReader.OpenAsync };
            var selection = new SelectionViewModel(client);
            var reads = new WorkspaceSelection(client);
            var words = new TextWordsViewModel(client, selection, reads);
            var assess = new AssessViewModel(client, selection);
            var owner = new ResultsInTextViewModel(words, assess, _ => { }, _ => { },
                new ChangesViewModel(client), client, reads);
            var panel = new ResultsInTextPanel(owner);
            var created = new List<WeakReference<object>>();
            owner.NativeModelCreated += created.Add;
            var host = new Border { Child = panel };
            var window = new Window { Width = 1240, Height = 780, Content = host };
            try
            {
                var form = SeededProject.AnalysedWordForm;
                await reads.ReloadAsync(project.FwDataPath, unrelatedText ? [unrelatedId] : [], [form],
                    shownAssessment: new SelectionAssessmentEvidence(baseline, root, [], []));
                Assert.Null(reads.Refusal);
                var summary = Assert.Single(reads.Summary!.Words);
                Assert.Equal(ambiguous ? 2 : 1, summary.Actions.CandidateWordformIds.Count);
                Assert.True(summary.IsAddedWord);
                Assert.Empty(reads.Summary.SourcePositions);
                var detail = await reads.Reader!.ReadWordDetailsAsync([summary.Key]);
                Assert.True(detail.Succeeded, detail.Refusal?.Message);
                using var acquired = detail.Value!;
                var stored = acquired.Value.Wordforms.GetValueOrDefault(summary.Key);
                var result = new AssessmentWordResult(form, "analysed", false, "Complete", 1, null)
                {
                    Morphology = summary.Assessment!.Morphology,
                    Origin = summary.Assessment.Origin,
                    StoredAnalyses = stored is null ? [] : SelectionDisplayProjection.StoredAnalyses(stored)
                        .Select(analysis => new ParserReading(analysis.Morphs)
                        {
                            StoredAnalysisId = analysis.StoredAnalysisId,
                            StoredAnalysisOpinion = analysis.StoredAnalysisOpinion,
                            Identity = analysis.Identity,
                        }).ToArray(),
                    StoredAnalysesAvailable = !ambiguous,
                };
                using var card = owner.GetCardToken(result);
                Assert.Equal(ambiguous ? null : project.Text.AnalysedWordformId, card.WordformId);
                Assert.Equal(!ambiguous, card.IncorrectSpellingAction.IsAvailable);
                Assert.NotEmpty(card.Marking.FixChoices);
                if (ambiguous) Assert.All(card.BoundFixChoices, choice => Assert.False(choice.IsAvailable));
                if (!ambiguous)
                {
                    Assert.Equal(incorrect, card.Comparison.Standing == ProjectStanding.IncorrectSpelling);
                    Assert.Contains(card.BoundFixChoices, choice => choice.IsAvailable && choice.Choice.StoredAnalysisId is not null);
                    Assert.All(card.BoundFixChoices.Where(choice => choice.IsAvailable), choice =>
                        Assert.Equal(project.Text.AnalysedWordformId, choice.Target.WordformId));
                }
                assess.Result = new AssessCommandResponse(new BaselineCaptureResponse(baseline, project.FwDataPath,
                    DateTimeOffset.UtcNow, false, false), new SelectionProjection([], []), [root], "Fixture")
                { Words = [result] };
                window.Show();
                owner.SelectWord(form);
                await owner.SelectionRefresh;
                AnalyzeTextsLayoutTests.Settle(window);
                AssertStandaloneCardTarget(owner, card.WordformId);
                Assert.Single(panel.GetVisualDescendants().OfType<WordCard>().Where(control =>
                    control.IsEffectivelyVisible && control.Document is not null).Select(control => control.Key));
                Assert.Equal(1, reads.Reader.Diagnostics.LiveCardModels);
                Assert.Equal(1, reads.Reader.Diagnostics.LivePinnedModels);
                Assert.Equal(1, reads.Reader.Diagnostics.LiveLineModels);
                owner.CloseTokenCard();
                AnalyzeTextsLayoutTests.Settle(window);
                await Task.Yield();
                Assert.Equal(0, reads.Reader.Diagnostics.LiveCardModels);
                Assert.Equal(0, reads.Reader.Diagnostics.LivePinnedModels);
                Assert.Equal(0, reads.Reader.Diagnostics.LiveLineModels);
                ScaleCountHarness.AssertLiveViewModelBudget(created, typeof(ResultsLineViewModel), 0, 0);
                ScaleCountHarness.AssertLiveViewModelBudget(created, typeof(ResultsTokenViewModel), 0, 0);
                owner.SelectWord(form);
                await owner.SelectionRefresh;
                host.IsVisible = false;
                AnalyzeTextsLayoutTests.Settle(window);
                await Task.Yield();
                Assert.Null(owner.SelectedToken);
                Assert.Equal(0, reads.Reader.Diagnostics.LiveCardModels);
                Assert.Equal(0, reads.Reader.Diagnostics.LivePinnedModels);
                Assert.Equal(0, reads.Reader.Diagnostics.LiveLineModels);
                ScaleCountHarness.AssertLiveViewModelBudget(created, typeof(ResultsLineViewModel), 0, 0);
                ScaleCountHarness.AssertLiveViewModelBudget(created, typeof(ResultsTokenViewModel), 0, 0);
            }
            finally
            {
                window.Close();
                await owner.StopAsync();
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void StandaloneWordCardShowsOnlyPendingChangesForItsWordform() => VerifyStandalonePendingIdentity(true);

    [Fact]
    public void StandaloneWordCardShowsAndUndoesAChangeForOnlyItsWordform() => VerifyStandalonePendingIdentity(false);

    private void VerifyStandalonePendingIdentity(bool alreadyStaged)
    {
        using var project = new WalkthroughProject(pristine);
        CaptureBaseline(project);
        var root = RecordTextAssessment(project);
        var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath),
            Path.GetFileNameWithoutExtension(project.FwDataPath));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!.Token;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var selectedId = project.Text.AnalysedWordformId;
            var otherId = Guid.NewGuid();
            var form = SeededProject.AnalysedWordForm;
            var selected = new PendingChange("selected", CanonicalId.FromGuid(selectedId).Value, form,
                ChangeKinds.IncorrectSpelling, null, null, []);
            var other = selected with { ChangeId = "homograph", WordformId = CanonicalId.FromGuid(otherId).Value };
            var client = new FakeCommandClient { SelectionReaderHandler = SelectionReader.OpenAsync };
            client.PendingChangesIs(new PendingChangesSnapshot("draft/standalone", "revision/standalone",
                alreadyStaged ? [selected, other] : [other], []));
            var selection = new SelectionViewModel(client);
            var reads = client.ReaderOwner;
            var words = new TextWordsViewModel(client, selection, reads);
            var assess = new AssessViewModel(client, selection);
            var changes = new ChangesViewModel(client);
            var owner = new ResultsInTextViewModel(words, assess, _ => { }, _ => { }, changes, client, reads);
            try
            {
                await changes.OpenProjectAsync(project.FwDataPath);
                await reads.ReloadAsync(project.FwDataPath, [], [form],
                    shownAssessment: new SelectionAssessmentEvidence(baseline, root, [], []));
                Assert.Null(reads.Refusal);
                var summary = Assert.Single(reads.Summary!.Words);
                Assert.Equal([selectedId], summary.Actions.CandidateWordformIds);
                Assert.Empty(reads.Summary.SourcePositions);
                assess.Result = new AssessCommandResponse(new BaselineCaptureResponse(baseline, project.FwDataPath,
                    DateTimeOffset.UtcNow, false, false), new SelectionProjection([], []), [root], "Fixture")
                {
                    Words = [new AssessmentWordResult(form, "analysed", false, "Complete", 1, null)
                    { Morphology = summary.Assessment!.Morphology, Origin = summary.Assessment.Origin }],
                };
                owner.SelectWord(form, CanonicalId.FromGuid(selectedId).Value);
                await owner.SelectionRefresh;
                Assert.Null(owner.SelectedToken!.Occurrence);
                if (!alreadyStaged)
                {
                    Assert.True(owner.AddChangeCommand.CanExecute(ChangeKinds.IncorrectSpelling));
                    await owner.AddChangeCommand.ExecuteAsync(ChangeKinds.IncorrectSpelling);
                    var request = Assert.Single(client.PendingPutRequests);
                    Assert.Equal(CanonicalId.FromGuid(selectedId).Value, request.Change.WordformId);
                    Assert.Equal(root, request.ExpectedContext!.SelectionEvidence!.RootAssessmentId);
                }
                var card = owner.SelectedToken!;
                Assert.True(card.HasStagedChanges);
                Assert.False(card.ShowsActions);
                var staged = Assert.Single(card.StagedChanges).Change;
                Assert.Equal(CanonicalId.FromGuid(selectedId).Value, staged.WordformId);
                if (alreadyStaged) Assert.Equal("selected", staged.ChangeId);
                else
                {
                    await changes.RemoveCommand.ExecuteAsync(staged);
                    Assert.False(card.HasStagedChanges);
                    Assert.True(card.ShowsActions);
                    Assert.False(card.IsPending);
                    Assert.Equal(staged.ChangeId, Assert.Single(client.PendingRemoveRequests).ChangeId);
                    Assert.Equal("homograph", Assert.Single(changes.Items).ChangeId);
                }
            }
            finally
            {
                await owner.StopAsync();
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertStandaloneCardTarget(ResultsInTextViewModel owner, Guid? target)
    {
        var selected = Assert.IsType<ResultsTokenViewModel>(owner.SelectedToken);
        Assert.Null(selected.Occurrence);
        Assert.Equal(target, selected.WordformId);
    }
}
