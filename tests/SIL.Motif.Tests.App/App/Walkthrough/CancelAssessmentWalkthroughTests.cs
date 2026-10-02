using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class CancelAssessmentWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ParserRefusalKeepsTheLastCompletedResultsVisibleAndAllowsRetry()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            WalkthroughSteps.StartAssessmentOverPastedWords(walkthrough, deadline);
            walkthrough.WaitUntil(() => walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the first Assessment did not finish");

            var assess = walkthrough.Workspace.Assess;
            var earlierWords = assess.Words.Rows.Select(row => row.Word).ToArray();
            Assert.NotEmpty(earlierWords);
            var invocationsBeforeRefusal = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            Assert.NotEmpty(invocationsBeforeRefusal);
            var priorInvocation = invocationsBeforeRefusal[^1];
            var liveChildren = PanglossProcesses.Snapshot(parserPath);
            FakeParser.BehaveBesideExecutable(parserPath, new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new { mode = "noReport" },
                },
            });
            walkthrough.TypePastedWords(SeededProject.FirstForm);
            walkthrough.Click("Parse all words in the Selection");
            walkthrough.WaitUntil(() => assess.State == RunState.Refused &&
                    assess.RunCommand.ExecutionTask is { IsCompleted: true } &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the missing parser report did not show a refusal");

            Assert.True(assess.ShowsEarlierResults);
            Assert.Equal(earlierWords, assess.Words.Rows.Select(row => row.Word));
            var retainedAfterRefusal = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            Assert.Equal(invocationsBeforeRefusal.Count, retainedAfterRefusal.Count);
            Assert.Equal(priorInvocation.InvocationId, retainedAfterRefusal[^1].InvocationId);
            Assert.Empty(PanglossProcesses.Snapshot(parserPath).Except(liveChildren));

            var childrenBeforeRetry = PanglossProcesses.Snapshot(parserPath);
            FakeParser.BehaveBesideExecutable(parserPath, new { });
            walkthrough.Click("Parse all words in the Selection");
            walkthrough.WaitUntil(() => assess.State == RunState.Completed &&
                    assess.Result is { } result && result.InvocationId != priorInvocation.InvocationId &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the retry did not replace the visible Assessment");
            Assert.False(assess.ShowsEarlierResults);
            var retained = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            Assert.True(retained.Count > invocationsBeforeRefusal.Count);
            Assert.Equal(assess.Result!.InvocationId, retained[^1].InvocationId);
            Assert.Empty(PanglossProcesses.Snapshot(parserPath).Except(childrenBeforeRetry));
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void SubstantialTextCanBeReadToItsLastOccurrenceAndSwitchedAfterUndo()
    {
        using var firstProject = SubstantialTextWalkthroughProject.Create(pristine);
        using var secondProject = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(firstProject.ManagedRoot);
        var deadline = Stopwatch.GetTimestamp() + 360 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                firstProject.ManagedRoot, firstProject.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            walkthrough.Check(SeededProject.TextTitle);
            WalkthroughSteps.EnsureAssessmentForAnalyze(walkthrough, WalkthroughSteps.Remaining(deadline));
            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowTextsTab(TextsTab.AnalyzeTexts);

            var inText = walkthrough.Workspace.PageModel<TextsPageModel>().ResultsInText;
            walkthrough.WaitUntil(() => inText.Texts.SingleOrDefault()?.Lines.Count == 302,
                WalkthroughSteps.Remaining(deadline), "Analyze texts did not load all generated lines");
            await inText.ReadStateRefresh;
            var text = Assert.Single(inText.Texts);
            var finalLine = text.Lines[^1];
            var finalToken = finalLine.Tokens[^1];
            Assert.Equal(302, finalLine.Number);
            Assert.Equal(SeededProject.AnalysedWordForm, finalToken.Form);
            Assert.Equal(7, finalToken.OccurrenceIndex);
            Assert.Equal(firstProject.Text.TextId, finalToken.Occurrence?.TextId);

            var panel = walkthrough.Window.GetLogicalDescendants().OfType<SIL.Motif.App.Views.ResultsInTextPanel>()
                .Single();
            var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(), viewer =>
                viewer.IsEffectivelyVisible && viewer.Content is ItemsControl);
            reader.Offset = new Vector(reader.Offset.X, Math.Max(0, reader.Extent.Height - reader.Viewport.Height));
            Dispatcher.UIThread.RunJobs();
            walkthrough.Window.UpdateLayout();
            var finalStrip = Assert.Single(panel.GetVisualDescendants().OfType<Border>(), strip =>
                strip.Name == "WordStrip" && ReferenceEquals(strip.Tag, finalToken));
            var stripOrigin = finalStrip.TranslatePoint(new Point(0, 0), reader);
            Assert.NotNull(stripOrigin);
            var viewport = new Rect(0, 0, reader.Viewport.Width, reader.Viewport.Height);
            Assert.True(viewport.Intersects(new Rect(stripOrigin!.Value, finalStrip.Bounds.Size)),
                "The final occurrence is outside the reader's visible bounds after scrolling to the end.");

            inText.SetFilterCommand.Execute(ResultsInTextFilter.Unread);
            Assert.Contains(finalLine, inText.VisibleLines);
            await inText.MarkReadAsync(finalToken);
            Assert.Null(inText.ReadStateRefusal);
            Assert.Null(inText.ReadStateNotice);
            Assert.False(finalToken.ShowUnread);
            Assert.True(finalToken.IsDimmed);
            await inText.MarkUnreadAsync(finalToken);
            Assert.Null(inText.ReadStateRefusal);
            Assert.Null(inText.ReadStateNotice);
            Assert.True(finalToken.ShowUnread);
            Assert.False(finalToken.IsDimmed);

            await inText.OpenTokenCardAsync(finalToken);
            reader.Offset = new Vector(reader.Offset.X, Math.Max(0, reader.Extent.Height - reader.Viewport.Height));
            Dispatcher.UIThread.RunJobs();
            walkthrough.Window.UpdateLayout();
            var card = Assert.Single(panel.GetVisualDescendants().OfType<Border>(), border =>
                border.Classes.Contains("wordCard") && border.IsEffectivelyVisible);
            var cardOrigin = card.TranslatePoint(new Point(0, 0), reader);
            Assert.NotNull(cardOrigin);
            Assert.True(viewport.Intersects(new Rect(cardOrigin!.Value, card.Bounds.Size)),
                "The final occurrence's card is outside the reader's visible bounds.");
            var removal = Assert.Single(finalToken.Marking.FixChoices, choice =>
                choice.Kind == AnalysisMarkingActionKind.RemoveAnalysis);
            await finalToken.StageMarkingChoiceForTokenCommand!.ExecuteAsync(removal);
            var change = Assert.Single(inText.Changes.Items);
            Assert.True(finalToken.IsPending);
            await inText.Changes.RemoveCommand.ExecuteAsync(change);
            Assert.Empty(inText.Changes.Items);
            Assert.False(finalToken.IsPending);

            walkthrough.ProjectPath = secondProject.FwDataPath;
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntilProjectIsQuiet(WalkthroughSteps.Remaining(deadline),
                "the second project's dispatcher work did not finish");
            walkthrough.WaitUntil(() => inText.Texts.Count == 0 || inText.Texts.Single().Lines.Count != 302,
                WalkthroughSteps.Remaining(deadline), "the first project's long Text remained in Analyze texts");
            Assert.Empty(inText.Changes.Items);
            Assert.False(walkthrough.Workspace.Context.IsOpeningProject);
            Assert.False(walkthrough.Workspace.RefreshCommand.IsRunning);
            Assert.True(Dispatcher.UIThread.CheckAccess());
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void CancellingAssessmentAddsNoInvocationAndAllowsARerun()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var batchStarted = Path.Combine(project.ManagedRoot, "cancelled-assessment-batch-started");
        var releaseBatch = Path.Combine(project.ManagedRoot, "release-cancelled-assessment-batch");
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            walkthrough.TypePastedWords(string.Join(Environment.NewLine,
                SeededProject.FirstForm, SeededProject.SecondForm));
            InteractiveControlSweep.AssertScene(walkthrough, "ready to assess pasted words",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Check,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.FocusableSurface,
                InteractiveControlFamily.Occurrence,
                InteractiveControlFamily.Collection);
            var beforeCancellation = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            var setupInvocation = Assert.Single(beforeCancellation);
            Assert.Equal(new[] { SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm }
                    .Order(StringComparer.Ordinal),
                setupInvocation.Selection.ResolvedWords.Order(StringComparer.Ordinal));

            var existingParserIds = PanglossProcesses.Snapshot(parserPath);
            FakeParser.BehaveBesideExecutable(parserPath, new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new { startedPath = batchStarted, holdUntilPath = releaseBatch },
                },
            });
            WalkthroughSteps.StartSlowAssessment(walkthrough, deadline,
                [SeededProject.FirstForm, SeededProject.SecondForm]);
            walkthrough.WaitUntil(
                () => File.Exists(batchStarted) &&
                    PanglossProcesses.Snapshot(parserPath).Except(existingParserIds).Any(),
                WalkthroughSteps.Remaining(deadline), "the fake PanGloss process did not reach its held batch");
            InteractiveControlSweep.AssertScene(walkthrough, "Assessment running",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Check,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.MatrixCell,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.Morpheme,
                InteractiveControlFamily.Progress,
                InteractiveControlFamily.FocusableSurface,
                InteractiveControlFamily.Container,
                InteractiveControlFamily.Collection);
            var processId = Assert.Single(PanglossProcesses.Snapshot(parserPath).Except(existingParserIds));

            try
            {
                walkthrough.Click("Cancel parsing all words");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State == RunState.Cancelled &&
                        walkthrough.Workspace.Assess.RunCommand.CanExecute(null) &&
                        walkthrough.Workspace.Assess.RunCommand.ExecutionTask is { IsCompleted: true } &&
                        !PanglossProcesses.AnyAlive(parserPath, [processId]),
                    WalkthroughSteps.Remaining(deadline), "the Assessment cancellation did not complete");
                InteractiveControlSweep.AssertScene(walkthrough, "Assessment cancelled",
                    InteractiveControlFamily.Action,
                    InteractiveControlFamily.Link,
                    InteractiveControlFamily.Filter,
                    InteractiveControlFamily.TextEntry,
                    InteractiveControlFamily.Choice,
                    InteractiveControlFamily.Check,
                    InteractiveControlFamily.Disclosure,
                    InteractiveControlFamily.List,
                    InteractiveControlFamily.MatrixCell,
                    InteractiveControlFamily.SelectableText,
                    InteractiveControlFamily.Mark,
                    InteractiveControlFamily.Morpheme,
                    InteractiveControlFamily.FocusableSurface,
                    InteractiveControlFamily.Container,
                    InteractiveControlFamily.Collection);
                Assert.False(PanglossProcesses.AnyAlive(parserPath, [processId]),
                    "the cancelled Assessment left its PanGloss process alive");

                Assert.Equal("assessment.cancelled", walkthrough.Workspace.Assess.Refusal?.Code);
                Assert.True(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
                Assert.True(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
                var afterCancellation = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
                Assert.Equal(beforeCancellation.Select(invocation => invocation.InvocationId),
                    afterCancellation.Select(invocation => invocation.InvocationId));
            }
            finally
            {
                File.WriteAllText(releaseBatch, string.Empty);
            }

            FakeParser.BehaveBesideExecutable(parserPath, new { });
            var previousRefresh = walkthrough.Workspace.RefreshCommand.ExecutionTask;
            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => !ReferenceEquals(previousRefresh, walkthrough.Workspace.RefreshCommand.ExecutionTask) &&
                    walkthrough.Workspace.RefreshCommand.ExecutionTask is { IsCompleted: true } &&
                    !walkthrough.Workspace.RefreshCommand.IsRunning &&
                    walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Baseline.ShownRefusal is null &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "refreshing after cancellation did not publish a Baseline");
            var refresh = Assert.IsAssignableFrom<Task>(walkthrough.Workspace.RefreshCommand.ExecutionTask);
            Assert.NotSame(previousRefresh, refresh);
            refresh.GetAwaiter().GetResult();
            var baselineToken = Assert.IsType<SIL.Motif.Contract.Baselines.BaselineToken>(
                walkthrough.Workspace.Baseline.Token);

            Assert.True(walkthrough.Workspace.Assess.RunCommand.CanExecute(null));
            var rerun = walkthrough.Workspace.Assess.RunCommand.ExecuteAsync(null);
            walkthrough.WaitUntil(
                () => rerun.IsCompleted && walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the Assessment rerun after cancellation did not complete");

            var afterRerun = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            Assert.Equal(beforeCancellation.Count + 1, afterRerun.Count);
            var invocation = Assert.Single(afterRerun, record =>
                beforeCancellation.All(existingRecord => existingRecord.InvocationId != record.InvocationId));
            Assert.Equal(new[] { SeededProject.FirstForm, SeededProject.SecondForm }.Order(StringComparer.Ordinal),
                invocation.Selection.PastedWords.Order(StringComparer.Ordinal));
            Assert.Equal(new[]
                {
                    SeededProject.FirstForm,
                    SeededProject.AnalysedWordForm,
                    SeededProject.SecondForm,
                    SeededProject.UnanalysedWordForm,
                }.Order(StringComparer.Ordinal),
                invocation.Selection.ResolvedWords.Order(StringComparer.Ordinal));
            Assert.True(baselineToken.HasSameSemanticIdentity(invocation.BaselineToken));
            Assert.Equal(baselineToken.BundleDigest, invocation.BaselineToken.BundleDigest);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private sealed class SubstantialTextWalkthroughProject : IDisposable
    {
        private readonly WalkthroughProject _project;

        private SubstantialTextWalkthroughProject(WalkthroughProject project) => _project = project;

        public string FwDataPath => _project.FwDataPath;

        public string ManagedRoot => _project.ManagedRoot;

        public SeededText Text => _project.Text;

        public static SubstantialTextWalkthroughProject Create(PristineProjectFixture pristine)
        {
            var project = new WalkthroughProject(pristine);
            try
            {
                var loader = new FwDataProjectLoader();
                using var cache = loader.LoadCache(project.FwDataPath);
                SubstantialTextSeed.AppendLines(cache, project.Text);
                loader.Save(cache);
                return new SubstantialTextWalkthroughProject(project);
            }
            catch
            {
                project.Dispose();
                throw;
            }
        }

        public void Dispose() => _project.Dispose();
    }
}
