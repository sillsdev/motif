using System.Reflection;
using Avalonia.Input;
using SIL.LCModel;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReopenRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ANewWorkspaceReadsStoredEvidenceIntoPagesWithoutOverview()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var firstClient = RealCommandClient.Create(project.ManagedRoot, parserPath);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(3);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var capture = await firstClient.CaptureBaselineAsync(
                new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(capture.Succeeded, capture.Refusal?.Message);

            var configured = await firstClient.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, "Default", [], ["motifa"]), CancellationToken.None);
            Assert.True(configured.Succeeded, configured.Refusal?.Message);
            var skipped = await firstClient.SkipSetupAsync(
                new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(skipped.Succeeded, skipped.Refusal?.Message);

            var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath),
                Path.GetFileNameWithoutExtension(project.FwDataPath));
            using (var database = ProjectMotifDatabase.Open(project.FwDataPath))
            {
                var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
                FakeParser.Behave(Path.GetDirectoryName(baseline.FwDataPath)!, new
                {
                    words = new[] { new { word = "motifa", outcome = "complete" } },
                    traceJson = "{\"type\":\"WordAnalysis\",\"inputShape\":\"motifa\",\"children\":[" +
                        "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"SeededRule\",\"children\":[" +
                        "{\"type\":\"Successful\",\"children\":[]}]}]}",
                });
            }

            var assessed = await firstClient.AssessAsync(
                new AssessRequest(project.FwDataPath), new Progress<AssessmentProgress>(), CancellationToken.None);
            Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
            var assessmentId = Assert.Single(assessed.Value!.Measurements,
                measurement => measurement.Kind == AssessmentKinds.ParseTime).AssessmentId;

            var pending = await firstClient.LoadPendingChangesAsync(
                new PendingChangesRequest(project.FwDataPath, MotifProductVersion.CurrentText), CancellationToken.None);
            Assert.True(pending.Succeeded, pending.Refusal?.Message);
            Guid wordformId;
            using (var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath))
            {
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                    .Single(wordform => wordform.Form.VernacularDefaultWritingSystem?.Text ==
                        SeededProject.AnalysedWordForm).Guid;
            }
            var changeId = CanonicalId.Mint().Value;
            var put = await firstClient.PutPendingChangeAsync(new PutPendingChangeRequest(
                project.FwDataPath, MotifProductVersion.CurrentText, pending.Value!.Revision,
                new ChangeIntent(changeId, "incorrect-spelling",
                    CanonicalId.FromGuid(wordformId).Value, SeededProject.AnalysedWordForm)), CancellationToken.None);
            Assert.True(put.Succeeded, put.Refusal?.Message);
            changeId = Assert.Single(put.Value!.Changes).ChangeId;

            var secondClient = RealCommandClient.Create(project.ManagedRoot, parserPath);
            var recording = DispatchProxy.Create<ICommandClient, RecordingCommandClient>();
            var recorder = (RecordingCommandClient)(object)recording;
            recorder.Inner = secondClient;
            var selection = new SelectionViewModel(recording);
            var changes = new ChangesViewModel(recording);
            var baselineModel = new BaselineViewModel(recording);
            var assess = new AssessViewModel(recording, selection);
            var context = new WorkspaceContext(selection, assess, changes, recording,
                new NoFolderPicker(), new NoDragSource(), baselineModel);
            var texts = new TextsPageModel(context);
            var timing = new TimingPageModel(context);
            var tryWord = new TryWordPageModel(context);
            var setup = new SetupViewModel(context, texts.Words);
            context.AttachSetup(setup);

            await context.OpenProjectAsync(project.FwDataPath);
            await context.EvidencePublication;

            Assert.Equal(capture.Value!.Token, baselineModel.Token);
            Assert.Equal("motifa", selection.PastedWordEntries.Single());
            var defaultState = await secondClient.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(defaultState.Succeeded, defaultState.Refusal?.Message);
            Assert.True(defaultState.Value!.SetupSkipped);
            Assert.True(context.Evidence.Assessment!.IsStored);
            Assert.Equal(assessmentId, context.Evidence.ParseTimeAssessmentId);
            Assert.Equal("motifa", Assert.Single(texts.Assess.Words.AllRows).Word);
            Assert.True(timing.HasStoredTiming);
            Assert.Equal(assessmentId, timing.StoredTiming!.AssessmentId);
            Assert.Equal("motifa", texts.Selection.PastedWordEntries.Single());
            Assert.Equal(changeId, Assert.Single(changes.Items).ChangeId);

            context.TryWord("motifa");
            await tryWord.Trace.TryCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(30));
            var ruleTiming = Assert.Single(recorder.TimingRequests,
                request => request.By == "rule" && request.ExplicitWords is { } words &&
                    words.SequenceEqual(["motifa"]));
            Assert.Equal(assessmentId, ruleTiming.AssessmentId);
            Assert.Equal("SeededRule", Assert.Single(tryWord.RulesOnBestPath).Rule);

            using var verificationDatabase = ProjectMotifDatabase.Open(project.FwDataPath);
            var storedAssessment = Assert.Single(
                new AssessmentRepository(verificationDatabase).ListBaselineAssessments(AssessmentKinds.ParseTime));
            Assert.Equal(assessmentId, storedAssessment.AssessmentId);
        }, TimeSpan.FromMilliseconds(Math.Max(1, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds)));
    }

    public class RecordingCommandClient : DispatchProxy
    {
        public ICommandClient Inner { get; set; } = null!;

        public List<TimingRequest> TimingRequests { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICommandClient.TimingAsync))
                TimingRequests.Add((TimingRequest)args![0]!);
            return targetMethod!.Invoke(Inner, args);
        }
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
