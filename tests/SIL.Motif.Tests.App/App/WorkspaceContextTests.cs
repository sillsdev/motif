using System.Globalization;
using System.Reflection;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that a page is built from a <see cref="WorkspaceContext"/> alone: a page model reacts to the project and
/// evidence the context publishes, requests pass through context navigation, and page model types do not name
/// the shell or one another.
/// </summary>
public sealed class WorkspaceContextTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private const string OtherProjectPath = @"C:\projects\two.fwdata";

    private static readonly Guid TextId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public void StopPageWorkWaitsForAnInFlightVisibleWordDetailRead()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var wordformId = Guid.NewGuid();
            using var fixture = StoredSelectionFixture.FromDisplayRecords(new TextWordsResponse(
                [new TextWord("word", wordformId.ToString("D"), [], [], [])],
                [new TextLines(TextId, "Alpha", [new TextLine(1,
                    [new TextToken("word", "word", null, "unanalysed") { WordformId = wordformId }])])], true), Token);
            var project = new ProjectLocator(fixture.ProjectPath, "project");
            using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
            using var reached = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            var armed = false;
            var fake = new FakeCommandClient();
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, DateTimeOffset.UtcNow, false));
            fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
            fake.SelectionReaderHandler = (request, cancellation) => SelectionReader.OpenAsync(database,
                new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), request.TextIds, request.AddedWords)
                {
                    ProjectPath = fixture.ProjectPath,
                    PauseAt = point =>
                    {
                        if (point != SelectionReaderPausePoint.AfterContextValidation || !Volatile.Read(ref armed)) return;
                        reached.Set();
                        if (!resume.Wait(TimeSpan.FromSeconds(10)))
                            throw new TimeoutException("The visible word detail read was not released.");
                    },
                }, cancellation);
            var context = NewContext(fake);
            var page = new TextsPageModel(context);
            try
            {
                await context.OpenProjectAsync(fixture.ProjectPath);
                context.Setup?.SkipCommand.Execute(null);
                context.Selection.Texts.Single().IsChecked = true;
                context.OpenPage(WorkspacePage.Texts);
                await context.EvidencePublication;
                await page.ResultsInText.SelectionRefresh;
                var row = page.Words.Rows[0];
                var reader = context.SelectionReads.Reader!;
                var heldResults = reader.Diagnostics.LeasedResults;
                Volatile.Write(ref armed, true);
                page.Words.RealizeRow(row);
                var details = page.Words.SettleVisibleDetailsAsync();
                Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))));
                var stopped = context.StopPageWorkAsync();
                Assert.False(stopped.IsCompleted);
                resume.Set();
                await stopped.WaitAsync(TimeSpan.FromSeconds(10));
                await details;
                Assert.False(row.HasStoredDetail);

                Assert.Equal(0, reader.Diagnostics.LiveRowModels);
                Assert.Equal(heldResults, reader.Diagnostics.LeasedResults);
                await context.StopProjectWorkAsync();
                Assert.Equal(0, reader.Diagnostics.LeasedResults);
            }
            finally
            {
                resume.Set();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task StopProjectWorkWaitsForAnInFlightSelectionReaderOpen()
    {
        var fake = new FakeCommandClient();
        using var fixture = StoredSelectionFixture.FromDisplayRecords(new TextWordsResponse([], [], true));
        fake.SelectionReaderHandler = fixture.OpenAsync;
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        var context = NewContext(fake);
        _ = new TextsPageModel(context);
        await context.Selection.SetProjectAsync(ProjectPath);
        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Texts);
        await context.EvidencePublication;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<CommandOutcome<SelectionReader>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.SelectionReaderHandler = (_, _) =>
        {
            started.SetResult();
            return completion.Task;
        };
        try
        {
            context.Selection.Texts.Single().IsChecked = true;
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stopped = context.StopProjectWorkAsync();
            Assert.False(stopped.IsCompleted);
        }
        finally
        {
            completion.SetResult(await fixture.OpenAsync(new OpenSelectionReaderRequest(fixture.ProjectPath, [], []),
                CancellationToken.None));
            await context.StopProjectWorkAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task OpeningAProjectReadsItsStoredOverviewThroughThePageContext()
    {
        var (fake, context) = NewContextWithFake();
        var overviewPage = new ProgressPageModel(context);
        var current = new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow, null,
            EvidenceFreshness.NoBaseline, null, null, null, null, null);
        var overview = Overview();
        fake.OverviewCompletesWith(overview);
        fake.ReadCurrentEvidenceCompletesWith(current);

        await context.OpenProjectAsync(ProjectPath);

        Assert.NotEmpty(fake.OverviewRequests);
        Assert.All(fake.OverviewRequests, request => Assert.Equal(ProjectPath, request.ProjectPath));
        Assert.Equal(ProjectPath, Assert.Single(fake.CurrentEvidenceRequests));
        Assert.Same(current, context.Evidence.Stored);
        Assert.Same(overview, overviewPage.Health.Overview);
    }

    [Fact]
    public async Task CheckingGrammarReloadsHealthWarningsFromTheCommand()
    {
        var (fake, context) = NewContextWithFake();
        var progress = new ProgressPageModel(context);
        var grammar = context.GrammarCheck;
        fake.OverviewCompletesWith(Overview());
        fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
            null, EvidenceFreshness.NoBaseline, null, null, null, null, null));
        await context.OpenProjectAsync(ProjectPath);
        Assert.False(progress.Health.HasWarnings);
        fake.OverviewCompletesWith(Overview() with
        {
            Warnings = new OverviewWarningsSummary(0, 0, null, null),
        });

        var readsBeforeCheck = fake.OverviewRequests.Count;
        await grammar.CheckCommand.ExecuteAsync(null);

        Assert.True(progress.Health.HasWarnings);
        Assert.Contains("0", progress.Health.WarningText, StringComparison.Ordinal);
        Assert.Equal(readsBeforeCheck + 1, fake.OverviewRequests.Count);
    }

    [Fact]
    public async Task AStoredGrammarReadThatFinishesAfterACheckStartsLeavesTheCheckRunning()
    {
        var (fake, context) = NewContextWithFake();
        var grammar = context.GrammarCheck;
        fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
            null, EvidenceFreshness.NoBaseline, null, null, null, null, null));
        await context.OpenProjectAsync(ProjectPath);
        var storedRead = new TaskCompletionSource<CommandOutcome<StoredGrammarCheckResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var storedReads = fake.StoredGrammarCheckRequests.Count;
        fake.StoredGrammarCheckHandler = (_, _) => storedRead.Task;
        var check = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnCheckGrammar((_, _) => check.Task);

        var publication = context.ReadStoredEvidenceAsync();
        Assert.Equal(storedReads + 1, fake.StoredGrammarCheckRequests.Count);
        var checking = grammar.CheckCommand.ExecuteAsync(null);
        Assert.True(grammar.IsLoading);
        storedRead.SetResult(CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(null)));
        await publication;

        Assert.True(grammar.IsLoading);
        check.SetResult(CommandOutcome<GrammarCheckResponse>.Success(new GrammarCheckResponse([], true)));
        await checking;
        Assert.False(grammar.IsLoading);
        Assert.True(grammar.HasChecked);
    }

    [Fact]
    public async Task OpeningAProjectRestoresTheStoredMatrixAndHandoff()
    {
        var (fake, context) = NewContextWithFake();
        _ = new ProgressPageModel(context);
        var texts = new TextsPageModel(context);
        var handoff = new SettingsPageModel(context).Handoff;
        Assert.Same(context.Changes, texts.Assess.Compare.Changes);
        var baseline = new BaselineRecord("project-1", Token, "root", ProjectPath,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var stored = StoredAssessment();
        var assessment = stored with
        {
            Details = ((AssessmentRecordDetails.Words)stored.Details) with
            {
                Rows = [new AssessedWord("dogs", "no-analysis", [], 10)
                {
                    ProjectStanding = ProjectStanding.Approved,
                    OccurrenceCount = 2,
                }],
                Invocation = new BatchInvocationEvidence("invocation/one", "source", "digest", "digest",
                    "words", "digest", "tsv", "digest", "stderr", "digest", 1000,
                    ParserWorkLimit.Default, 1, false),
            },
        };
        fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
            null, EvidenceFreshness.Current, baseline, null, null, null, assessment)
        {
            MatchingCorrectnessAssessmentId = "correctness-1",
        });
        fake.OverviewCompletesWith(Overview());

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("dogs", Assert.Single(context.Assess.Words.AllRows).Word);
        Assert.Equal(1, texts.Assess.Compare.TotalCount);
        Assert.Equal(assessment.AssessmentId, context.Changes.AssessmentId);
        Assert.Contains(context.Assess.Result!.Measurements,
            item => item.Kind == "Correctness" && item.AssessmentId == "correctness-1");
        Assert.NotNull(handoff.Handoff.LatestAssessmentAt);
        Assert.Equal("invocation/one", handoff.Handoff.InvocationId);
    }

    [Fact]
    public async Task RestoredTimedOutWordRemainsIncomplete()
    {
        var (fake, context) = NewContextWithFake();
        _ = new ProgressPageModel(context);
        _ = new TextsPageModel(context);
        var baseline = new BaselineRecord("project-1", Token, "root", ProjectPath,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
            null, EvidenceFreshness.Current, baseline, null, null, null,
            WithRows(StoredAssessment(), [new AssessedWord("dogs", "timed-out", [])])));
        fake.OverviewCompletesWith(Overview());

        await context.OpenProjectAsync(ProjectPath);

        Assert.True(Assert.Single(context.Assess.Words.AllRows).IsIncomplete);
        Assert.Contains("1 incomplete", context.Assess.Result!.CompletionSummary);
    }

    [Theory]
    [InlineData("morph_rule", "Morphological rules")]
    [InlineData("phon_rule", "Phonological rules")]
    [InlineData("root_index", "Root lookup")]
    [InlineData("lex_entry", "Lexical entries")]
    [InlineData("parser", "Parser")]
    public void TimingDisplaysReadableRuleKindLabels(string storedKind, string label) =>
        Assert.Equal(label, TimingShare.KindName(storedKind));

    [Fact]
    public async Task AnAiHandoffPageModelBuiltFromAContextAloneCoversThePublishedAssessmentAndForgetsItOnClear()
    {
        var context = NewContext();
        var handoff = new SettingsPageModel(context).Handoff;
        Assert.Equal("Write the AI Handoff", handoff.HandoffActionText);

        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        Assert.Equal("invocation/one", handoff.Handoff.InvocationId);
        Assert.StartsWith("Covers the words parsed on ", handoff.Handoff.CoverageText);

        context.ClearProject();
        Assert.Null(handoff.Handoff.InvocationId);
        Assert.False(context.HasEvidence);
    }

    [Fact]
    public async Task PendingChangesFollowAProjectSwitchOnlyThroughTheirSlot()
    {
        var context = NewContext();
        await context.OpenProjectAsync(ProjectPath);
        Assert.Equal(ProjectPath, context.Changes.ProjectPath);

        context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "dogz", string.Empty));
        Assert.Single(context.Changes.Items);

        context.ClearProject();
        Assert.False(context.Changes.HasItems);

        await context.OpenProjectAsync(OtherProjectPath);
        Assert.Equal(OtherProjectPath, context.Changes.ProjectPath);
        Assert.False(context.Changes.HasItems);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling, false, false)]
    [InlineData(ChangeKinds.IncorrectSpelling, true, false)]
    [InlineData(ChangeKinds.AddCandidate, false, false)]
    [InlineData(ChangeKinds.AddCandidate, true, false)]
    [InlineData(ChangeKinds.IncorrectSpelling, false, true)]
    [InlineData(ChangeKinds.AddCandidate, false, true)]
    public void ReaderNavigationPreservesAnOccurrenceFreeHomographsIdentity(
        string kind, bool reverse, bool absent)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var ownId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
            var otherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
            var (fake, context) = NewContextWithFake();
            var texts = new TextsPageModel(context);
                fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
                [new PendingChange("other", CanonicalId.FromGuid(otherId).Value, "dogs", kind, null, null, [])], []));
            var own = new TextToken("dogs", "dogs", null, null) { WordformId = ownId };
            var other = own with { WordformId = otherId };
            var captured = new TextWordsResponse([], [new TextLines(Guid.NewGuid(), "Alpha",
                [new TextLine(1, absent ? [own] : reverse ? [other, own] : [own, other])])], true);
            var evidence = new WorkspaceEvidence(Assessment() with
            {
                Words = [new AssessmentWordResult("dogs", "no-analysis", false, "Complete", 1, null)
                {
                    ProjectStanding = ProjectStanding.Approved,
                    Comparison = new WordComparison(ProjectStanding.Approved, WordRowOutcome.Same,
                        "kept", "Shared spelling comparison", WordRowTone.Fine)
                        { Availability = AnalysisComparisonAvailability.Available },
                }],
            }, null, false);
            await using var fixture = await OpenCapturedSelectionAsync(fake, context, captured, evidence.Assessment);
            context.Assess.Restore(evidence);
            context.PublishEvidence(evidence);
            await context.EvidencePublication;
            await texts.ResultsInText.SelectionRefresh;
            Assert.Null(context.SelectionReads.Refusal);
            var change = Assert.Single(context.Changes.Items);

            context.Open(new OpenWordRequest(change.Word, change.WordformId));

            await texts.ResultsInText.SelectionRefresh;
            var selected = Assert.IsType<ResultsTokenViewModel>(texts.ResultsInText.SelectedToken);
            Assert.Equal(otherId, selected.WordformId);
            if (absent)
            {
                Assert.Equal("Not in a chosen text", selected.Location);
                Assert.Empty(selected.ProjectApprovedAnalyses);
                Assert.Equal(AnalysisComparisonAvailability.Unavailable, selected.Comparison.Availability);
                Assert.Empty(selected.FieldWorksAnalyses);
            }
            else Assert.Contains(context.SelectionReads.Summary!.SourcePositions,
                position => position.Location.Anchor == selected.Occurrence);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void OpeningATypedAssessedWordThroughTheContextSelectsItsReadingDetail()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContextWithFake();
            var texts = new TextsPageModel(context);
            var analysis = new ParseAnalysis(
                [new ParseMorph("typed-only", "bbbbbbbb-0000-0000-0000-000000000001", null, null)]);
            var assessment = Assessment() with
            {
                Words =
                [
                    new AssessmentWordResult("typed-only", "analysed", false, "Search completed", 3, null)
                    {
                        Morphology = new ParseWordEvidence(
                            ParseMorphEvidence.Schema, 0, "typed-only", 3, false, false, false, [analysis], []),
                        Readings = [new ParserReading([new ParserReadingMorph(
                            "typed-only", "gloss", "n", null, false, null)])],
                        ReadingGrades = ["no-opinion"],
                    },
                ],
            };

            await using var fixture = await OpenCapturedSelectionAsync(fake, context,
                new TextWordsResponse([], [], true), assessment, ["typed-only"]);
            context.Assess.Result = assessment;
            await context.EvidencePublication;
            await texts.ResultsInText.SelectionRefresh;
            Assert.Null(context.SelectionReads.Refusal);
            texts.ResultsInText.ShowReader();
            context.OpenWord("typed-only");
            await texts.ResultsInText.SelectionRefresh;

            Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
            Assert.True(texts.ResultsInText.HasSelectedToken);
            await texts.ResultsInText.SelectionRefresh;
            var selected = Assert.IsType<ResultsTokenViewModel>(texts.ResultsInText.SelectedToken);
            Assert.Equal("typed-only", selected.Form);
            Assert.Null(texts.ResultsInText.LinePages);
            Assert.Empty(texts.ResultsInText.VisibleHeaders);
            var standaloneReference = Assert.IsType<WeakReference<object>>(Assert.Single(
                Assert.IsAssignableFrom<IEnumerable<object>>(texts.ResultsInText.DisplayedLines)));
            Assert.True(standaloneReference.TryGetTarget(out var standaloneModel));
            var standalone = Assert.IsType<ResultsLineViewModel>(standaloneModel);
            Assert.True(standalone.IsStandalone);
            Assert.Equal(Guid.Empty, standalone.TextId);
            Assert.Null(selected.Occurrence);
            Assert.Same(selected, Assert.Single(standalone.Tokens));
            var reading = Assert.Single(selected.Readings);
            Assert.Equal(analysis, reading.Analysis);
            Assert.False(texts.ResultsInText.AddChangeCommand.CanExecute(ChangeKinds.Approve));
            selected.SelectedReading = reading;
            Assert.False(texts.ResultsInText.AddChangeCommand.CanExecute(ChangeKinds.Approve));
            Assert.False(texts.ResultsInText.AddChangeCommand.CanExecute(ChangeKinds.Reject));
            Assert.False(texts.ResultsInText.AddChangeCommand.CanExecute(ChangeKinds.Candidate));
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void WordTraceSessionSharesTheWorkspaceTraceWithoutOpeningAPage()
    {
        var context = NewContext();
        var page = context.CurrentPage;
        var session = context.WordTraceSession;

        session.Trace.WordToTry = "dogs";

        Assert.Same(context.Assess.Trace, session.Trace);
        Assert.Equal("dogs", session.Trace.WordToTry);
        Assert.Equal(page, context.CurrentPage);
    }

    [Fact]
    public void AnalyzeTextsCanHandOffOnlyTheTickedWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContextWithFake();
            var texts = new TextsPageModel(context);
            var handoff = new SettingsPageModel(context).Handoff;
            var captured = new TextWordsResponse(
                [new TextWord("dogs", null,
                        [new WordOccurrence(TextId, "Alpha", 1, "dogs", "unanalysed", null)], [], []),
                    new TextWord("cats", null,
                        [new WordOccurrence(TextId, "Alpha", 2, "cats", "unanalysed", null)], [], [])],
                [new TextLines(TextId, "Alpha", [new TextLine(1, [new TextToken("dogs", "dogs", null, null)]),
                    new TextLine(2, [new TextToken("cats", "cats", null, null)])])],
                HasBaseline: true, OccurrenceCount: 2);
            await using var fixture = await OpenCapturedSelectionAsync(fake, context, captured);
            texts.Words.Rows.Single(row => row.Form == "cats").IsChecked = true;

            Assert.True(texts.Words.HandOffCheckedWordsCommand.CanExecute(null));
            texts.Words.HandOffCheckedWordsCommand.Execute(null);

            Assert.Equal(["cats"], handoff.Handoff.ChosenWords);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ReaderAndWordsShowTheSameSelection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContextWithFake();
            var texts = new TextsPageModel(context);
            var captured = new TextWordsResponse(
                [new TextWord("kitabu", null,
                    [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "unanalysed", null)], [], [])],
                [new TextLines(TextId, "Alpha", [new TextLine(1, [new TextToken("kitabu", "kitabu", null, null)])])],
                HasBaseline: true, OccurrenceCount: 1);
            var assessment = Assessment() with { Words = [ApprovedUnparsed("kitabu")] };
            await using var fixture = await OpenCapturedSelectionAsync(fake, context, captured, assessment);
            context.Assess.Result = assessment;
            await context.EvidencePublication;
            await texts.ResultsInText.SelectionRefresh;

            Assert.Equal(["kitabu"], texts.Words.Rows.Select(row => row.Form));
            texts.ResultsInText.ShowReader();
            var occurrence = Assert.Single(context.SelectionReads.Summary!.SourcePositions);
            Assert.Equal("kitabu", (await texts.ResultsInText.ReadOccurrenceAsync(occurrence.Location.Anchor))?.Form);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AnAnalyzeChangeShowsNotAppliedYetInTheReader()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContextWithFake();
            var texts = new TextsPageModel(context);
            var wordformId = Guid.NewGuid();
            var captured = new TextWordsResponse(
                [new TextWord("kitabu", wordformId.ToString("D"),
                    [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "unanalysed", null)], [], [])],
                [new TextLines(TextId, "Alpha", [new TextLine(1, [new TextToken("kitabu", "kitabu", null, null) { WordformId = wordformId }])])],
                HasBaseline: true, OccurrenceCount: 1);
            var assessment = Assessment() with { Words = [ApprovedUnparsed("kitabu")] };
            await using var fixture = await OpenCapturedSelectionAsync(fake, context, captured, assessment);
            context.Assess.Result = assessment;
            await context.EvidencePublication;
            await texts.ResultsInText.SelectionRefresh;
            await texts.ResultsInText.SelectionRefresh;
            Assert.Null(context.SelectionReads.Refusal);
            texts.ResultsInText.ShowReader();
            var token = Assert.IsType<ResultsTokenViewModel>(await texts.ResultsInText.ReadOccurrenceAsync(
                Assert.Single(context.SelectionReads.Summary!.SourcePositions).Location.Anchor));
            texts.ResultsInText.SelectToken(token);

            await texts.ResultsInText.AddChangeCommand.ExecuteAsync(ChangeKinds.IncorrectSpelling);

            Assert.Equal("Not applied yet", token.PendingChangeStatus);

            context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "kitabu", "",
                fit: new ChangeFit("stale-change", false, ["The project changed."]),
                wordformId: CanonicalId.FromGuid(wordformId).Value));

            Assert.Equal(PendingChangeState.NoLongerFits, token.PendingState);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AnalyzeOpinionSendsTheChosenAnalysisAndReadingIndex()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, DateTimeOffset.UtcNow, false));
            var selection = new SelectionViewModel(fake);
            var changes = new ChangesViewModel(fake);
            var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection), changes, fake,
                new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(fake));
            var texts = new TextsPageModel(context);
            var first = new ParseAnalysis([new ParseMorph("typed-only", "bbbbbbbb-0000-0000-0000-000000000001", null, null)]);
            var second = new ParseAnalysis([new ParseMorph("typed-only", "bbbbbbbb-0000-0000-0000-000000000002", null, null)]);
            var assessment = Assessment() with
            {
                Words =
                [
                    new AssessmentWordResult("typed-only", "analysed", false, "Search completed", 3, null)
                    {
                        Morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, "typed-only", 3,
                            false, false, false, [first, second], []),
                        Readings =
                        [
                            new ParserReading([new ParserReadingMorph("typed-only", "first", "n", null, false, null)]),
                            new ParserReading([new ParserReadingMorph("typed-only", "second", "n", null, false, null)]),
                        ],
                        ReadingGrades = ["no-opinion", "no-opinion"],
                    },
                ],
            };
            var wordformId = Guid.NewGuid();
            var captured = new TextWordsResponse([], [new TextLines(TextId, "Alpha", [new TextLine(1,
                [new TextToken("typed-only", "typed-only", null, null) { WordformId = wordformId }])])], true);
            await using var fixture = await OpenCapturedSelectionAsync(fake, context, captured, assessment);
            context.Assess.Result = assessment;
            await context.EvidencePublication;
            await texts.ResultsInText.SelectionRefresh;
            Assert.Null(context.SelectionReads.Refusal);
            texts.ResultsInText.ShowReader();
            texts.ResultsInText.SelectWord("typed-only");
            await texts.ResultsInText.SelectionRefresh;
            var token = Assert.IsType<ResultsTokenViewModel>(texts.ResultsInText.SelectedToken);
            token.SelectedReading = token.Readings[1];

            await texts.ResultsInText.AddChangeCommand.ExecuteAsync(ChangeKinds.Approve);

            var change = Assert.Single(fake.PendingPutRequests).Change;
            Assert.Equal(CanonicalId.FromGuid(wordformId).Value, change.WordformId);
            Assert.Equal(second.Morphs, change.Reading!.Morphs);
            Assert.Equal(1, change.ReadingIndex);
        }, TimeSpan.FromSeconds(60));
    }

    internal static async Task<CapturedSelectionOwner> OpenCapturedSelectionAsync(FakeCommandClient fake,
        WorkspaceContext context, TextWordsResponse records, AssessCommandResponse? assessment = null,
        IReadOnlyList<string>? addedWords = null, Action<StoredSelectionFixture>? prepare = null)
    {
        var fixture = StoredSelectionFixture.FromDisplayRecords(records, assessment?.Baseline.Token ?? Token);
        prepare?.Invoke(fixture);
        if (assessment is not null) fixture.RecordAssessment(assessment);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(assessment?.Baseline.Token ?? Token,
            DateTimeOffset.UtcNow, false));
        var opens = new List<OpenSelectionReaderRequest>();
        fake.SelectionReaderHandler = (request, token) =>
        {
            opens.Add(request);
            return fixture.OpenAsync(request, token);
        };
        fake.ListTextsCompletesWith(new TextInventoryResponse(records.Texts
            .Select(text => new TextChoiceSummary(text.TextId, text.Title)).ToArray(), true));
        await context.OpenProjectAsync(fixture.ProjectPath);
        context.Setup?.SkipCommand.Execute(null);

        foreach (var text in context.Selection.Texts) text.IsChecked = true;
        if (addedWords is not null) context.Selection.PastedWords = string.Join("\n", addedWords);
        if (assessment is not null)
        {
            var evidence = new WorkspaceEvidence(assessment, DateTimeOffset.UtcNow, false);
            context.Assess.Restore(evidence);
            context.PublishEvidence(evidence);
        }
        context.OpenPage(WorkspacePage.Texts);
        await context.EvidencePublication;
        Assert.Null(context.SelectionReads.Refusal);
        return new CapturedSelectionOwner(context, fixture, opens);
    }

    internal sealed class CapturedSelectionOwner(WorkspaceContext context, StoredSelectionFixture fixture,
        IReadOnlyList<OpenSelectionReaderRequest> opens) : IAsyncDisposable
    {
        public StoredSelectionFixture Fixture => fixture;

        /// <summary>Every request the window made to open its Selection, in order.</summary>
        public IReadOnlyList<OpenSelectionReaderRequest> Opens => opens;

        public async ValueTask DisposeAsync()
        {
            await context.StopProjectWorkAsync();
            fixture.Dispose();
        }
    }

    [Fact]
    public void SetupSelectionStepRequestsCapturedCountsWithoutRealizingDisplayModels()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var wordform = Guid.NewGuid();
            using var fixture = new StoredSelectionFixture(new SIL.Motif.Host.Texts.TextWordsProjection([
                new SIL.Motif.Host.Texts.TextWordsProjectedText(TextId, "Alpha", [
                    new SIL.Motif.Host.Texts.TextWordsProjectedLine(1, "dogs", [
                        new SIL.Motif.Host.Texts.TextWordsProjectedToken("dogs", [new WritingSystemText("dogs", "en")],
                            wordform, "unanalysed", null, null, null, null, 0, null),
                    ], Guid.NewGuid(), Guid.NewGuid(), false),
                ], []),
            ], [new SIL.Motif.Host.Texts.TextWordsProjectedWordform(wordform, [], [], 0, false, [])]));
            var fake = new FakeCommandClient();
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, DateTimeOffset.UtcNow, false));
            fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
            fake.SelectionReaderHandler = fixture.OpenAsync;
            var context = NewContext(fake);
            var page = new TextsPageModel(context);
            var setup = new SetupViewModel(context, page.Words);
            context.AttachSetup(setup);
            try
            {
                await context.OpenProjectAsync(fixture.ProjectPath);
                context.Selection.Texts.Single().IsChecked = true;
                await context.EvidencePublication;
                setup.IsOpen = true;
                Assert.Null(context.SelectionReads.Reader);
                setup.NextCommand.Execute(null);
                await context.EvidencePublication;
                Assert.Null(context.SelectionReads.Refusal);
                Assert.Equal(WorkspacePage.Progress, context.CurrentPage);
                Assert.Equal(1, page.Words.WordCount);
                Assert.Equal(1, page.Words.OccurrenceCount);
                Assert.Equal(page.Words.SummaryText, setup.RunSummary);
                var reader = Assert.IsType<SelectionReader>(context.SelectionReads.Reader);
                Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
                Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized);
                Assert.Equal(0, reader.Diagnostics.LiveRowModels);
                Assert.Equal(0, reader.Diagnostics.LiveLineModels);
                Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
            }
            finally { await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void UnparsedOverviewDefersSourceReadsAndOccurrenceNavigationWaitsForItsReader()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var paragraph = Guid.NewGuid();
            var segment = Guid.NewGuid();
            var wordform = Guid.NewGuid();
            using var fixture = new StoredSelectionFixture(new SIL.Motif.Host.Texts.TextWordsProjection([
                new SIL.Motif.Host.Texts.TextWordsProjectedText(TextId, "Alpha", [
                    new SIL.Motif.Host.Texts.TextWordsProjectedLine(1, "dogs", [
                        new SIL.Motif.Host.Texts.TextWordsProjectedToken("dogs", [new WritingSystemText("dogs", "en")],
                            wordform, "unanalysed", null, null, null, null, 0, null),
                    ], paragraph, segment, false),
                ], []),
            ], [new SIL.Motif.Host.Texts.TextWordsProjectedWordform(wordform, [], [], 0, false, [])]));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var navigated = new TaskCompletionSource<OccurrenceAnchor>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            var fake = new FakeCommandClient();
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, DateTimeOffset.UtcNow, false));
            fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
            fake.SelectionReaderHandler = async (request, cancellation) =>
            {
                reads++;
                entered.TrySetResult();
                await resume.Task.WaitAsync(cancellation);
                return await fixture.OpenAsync(request, cancellation);
            };
            var context = NewContext(fake);
            var page = new TextsPageModel(context);
            page.ResultsInText.OccurrenceRequested += anchor => navigated.TrySetResult(anchor);
            try
            {
                await context.OpenProjectAsync(fixture.ProjectPath);
                context.Selection.Texts.Single().IsChecked = true;
                await context.EvidencePublication;
                Assert.Equal(WorkspacePage.Progress, context.CurrentPage);
                Assert.Null(context.SelectionReads.Reader);
                Assert.Equal(0, reads);
                var anchor = new OccurrenceAnchor(TextId, paragraph, segment, 0);
                context.OpenOccurrence(anchor, "dogs", wordform.ToString("D"));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(navigated.Task.IsCompleted);
                resume.SetResult();
                Assert.Equal(anchor, await navigated.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(TextId, page.ResultsInText.SelectedText!.TextId);
                Assert.Equal(1, reads);

            }
            finally
            {
                resume.TrySetResult();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void HandingOffWordsThroughTheContextOpensSettingsOnThoseWords()
    {
        var context = NewContext();
        var settings = new SettingsPageModel(context);

        context.HandOff(["dogs", "cats"]);

        Assert.Equal(WorkspacePage.Settings, context.CurrentPage);
        Assert.Equal(["dogs", "cats"], settings.Handoff.Handoff.ChosenWords);
        Assert.Equal(1, settings.HandoffRequestVersion);
    }

    [Fact]
    public void UnavailableDestinationsDoNotReceiveRequests()
    {
        var context = NewContext();
        var page = new ElsewherePageModel(context);
        var initial = context.CurrentPage;

        context.Open(new ElsewhereRequest("not delivered"));
        context.OpenPage((WorkspacePage)int.MaxValue);

        Assert.Equal(initial, context.CurrentPage);
        Assert.Null(page.Received);
        Assert.False(context.CanOpenPage((WorkspacePage)int.MaxValue));
    }

    [Fact]
    public async Task APageDefinedOutsideTheAppRunsItsOwnQueryAndIsOpenedThroughARegistryEntryAndTheContextAlone()
    {
        var (fake, context) = NewContextWithFake();
        context.Diagnostics = true;
        fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, DateTimeOffset.UtcNow)]);
        var entry = PageEntry.Of(WorkspacePage.Diagnostics, "Elsewhere", "M0 0h1", c => new ElsewherePageModel(c),
            _ => new Avalonia.Controls.Border());
        var model = (ElsewherePageModel)entry.CreateModel(context);

        await context.OpenProjectAsync(ProjectPath);
        Assert.Equal(1, model.Queried);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        Assert.Equal("invocation/one", model.Shown);

        context.Open(new ElsewhereRequest("note"));
        Assert.Equal(WorkspacePage.Diagnostics, context.CurrentPage);
        Assert.Equal("note", model.Received);
    }

    [Fact]
    public async Task OpeningAProjectAndCapturingABaselineAwaitEveryPagesOwnLoad()
    {
        var context = NewContext();
        var page = new LoadingPageModel(context);

        await context.OpenProjectAsync(ProjectPath);
        Assert.Equal(ProjectPath, page.Opened);
        Assert.Equal(ProjectPath, context.ProjectPath);

        await context.PublishBaselineCapturedAsync();
        Assert.Equal(1, page.Captures);
    }

    [Fact]
    public void NoPageModelIsNamedByTheWorkspaceTheContextOrAnotherPage()
    {
        var pageModels = typeof(PageModel).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(PageModel)))
            .ToHashSet();
        Assert.Equal(PageRegistry.Entries.Count, pageModels.Count);

        foreach (var holder in new[] { typeof(WorkspaceShellViewModel), typeof(WorkspaceContext) })
            Assert.DoesNotContain(NamedTypes(holder), pageModels.Contains);

        foreach (var page in pageModels)
        {
            var named = NamedTypes(page).ToList();
            Assert.DoesNotContain(typeof(WorkspaceShellViewModel), named);
            Assert.DoesNotContain(named, type => type != page && pageModels.Contains(type));
        }
    }

    [Fact]
    public void EveryRegisteredPageBuildsItsModelFromTheContext()
    {
        var context = NewContext();
        foreach (var entry in PageRegistry.Entries)
            Assert.Same(context, entry.CreateModel(context).Context);
    }

    // Every type a class names through its fields, properties, and constructor and method parameters.
    private static IEnumerable<Type> NamedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return type.GetFields(all).Select(field => field.FieldType)
            .Concat(type.GetProperties(all).Select(property => property.PropertyType))
            .Concat(type.GetConstructors(all).SelectMany(ctor => ctor.GetParameters()).Select(p => p.ParameterType))
            .Concat(type.GetMethods(all).SelectMany(method => method.GetParameters().Select(p => p.ParameterType)
                .Append(method.ReturnType)));
    }

    internal static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(Token, ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
        Measurements = [new ProducedAssessmentReference("assessment-1", "ObjectTiming", "invocation/one"),
            new ProducedAssessmentReference("assessment-parse", "ParseTime", "invocation/one")],
    };

    private static AssessmentWordResult ApprovedUnparsed(string form) =>
        new(form, "no-analysis", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            OccurrenceCount = 1,
        };

    private static AssessmentRecord WithRows(AssessmentRecord record, IReadOnlyList<AssessedWord> rows) =>
        record with { Details = ((AssessmentRecordDetails.Words)record.Details) with { Rows = rows } };

    private static AssessmentRecord StoredAssessment() => new AssessmentRecord(
        AssessmentId: "assessment-1",
        ProposalId: null,
        ProposalIntentDigest: null,
        Assessor: "pangloss",
        Kind: AssessmentKind.ParseTime.ToStoredKind(),
        ScopeJson: "{}",
        ScopeDigest: "sha256:scope",
        Subject: new AssessmentSubject.Words(Selection.Create("Default", ["dogs"]), "whitespace", "1"),
        BaselineToken: "{}",
        OutcomeDigest: null,
        SemanticDigest: null,
        GrammarSourceSha256: "sha256:grammar",
        ModelFingerprint: null,
        Pipeline: null,
        DiagnosticCount: null,
        SavedUtc: "2026-09-24T12:00:00.0000000+00:00",
        Details: new AssessmentRecordDetails.Words([], null, null, [])
    );

    private static OverviewResponse Overview() => new(
        "one", DateTimeOffset.UtcNow, null, 0, 0, 0, 0, 0, 0, 0, null, null, null, null, null,
        new OverviewTextCoverage(0, 0, 0, 0, 0, 0), new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
        new OverviewTiming(null, null, [], 0), null);

    [Fact]
    public async Task OpenProposalPublishesItsTrialThroughASeparateSelectionOwner()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(Trial());
        var todayOwner = context.SelectionReads;
        var proposalOwner = context.ProposalSelectionReads;

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.True(context.EvidenceLens.InProposalMode);
        Assert.Same(context.Evidence, context.EvidenceLens.Today);
        Assert.Equal(EvidenceOriginKind.Trial, Assert.Single(context.EvidenceLens.Sets).Origin.Kind);
        Assert.Same(todayOwner, context.SelectionReads);
        Assert.Same(proposalOwner, context.ProposalSelectionReads);
        Assert.Equal(EvidenceOriginKind.Trial,
            Assert.Single(fake.OpenSelectionReaderRequests).ShownAssessment!.Origin.Kind);

        var todayGeneration = todayOwner.Generation;
        var proposalGeneration = proposalOwner.Generation;
        await context.LeaveProposalAsync();
        await context.EvidencePublication;

        Assert.Null(context.Lens);
        Assert.False(context.EvidenceLens.InProposalMode);
        Assert.Equal(todayGeneration, todayOwner.Generation);
        Assert.True(proposalOwner.Generation > proposalGeneration);
    }

    [Fact]
    public async Task OpeningAProposalPointsStagingAtItsDraftBeforeTheLensShows()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(Trial());
        string? targetWhenLensShown = null;
        context.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceContext.Lens) && context.Lens is not null)
                targetWhenLensShown = context.Changes.TargetProposalId;
        };

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("proposal/one", targetWhenLensShown);
        Assert.Equal("proposal/one", fake.PendingLoadRequests.Last().ProposalId);

        await context.LeaveProposalAsync();

        Assert.Null(context.Lens);
        Assert.Null(context.Changes.TargetProposalId);
        Assert.Null(fake.PendingLoadRequests.Last().ProposalId);
    }

    [Fact]
    public async Task ProposalWithoutRootAssessmentStaysOnTheLensButPublishesNoAfterEvidence()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        var incomplete = Trial() with
        {
            AssessmentIds = new Dictionary<string, string>(),
            Status = JobStatus.Running,
            CompletedUtc = null,
            Freshness = TrialFreshness.Incomplete,
        };
        fake.ReadTrialCompletesWith(incomplete);

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.True(context.EvidenceLens.InProposalMode);
        Assert.Same(incomplete, context.Lens?.Trial);
        Assert.Null(context.EvidenceLens.Proposal);
        Assert.Empty(context.EvidenceLens.Sets);
    }

    [Fact]
    public async Task OpenProposalSurfacesAReadRefusalWithoutThrowing()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialRefusesWith(new Refusal("trial.scratch-gone", FailureReason.NotFound,
            "The Trial scratch is gone."));

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.True(context.EvidenceLens.InProposalMode);
        Assert.Null(context.Lens?.Trial);
        Assert.Equal("trial.scratch-gone", context.Lens?.ReadRefusal?.Code);
    }

    [Fact]
    public async Task ATrialReadThatFinishesAfterAProjectSwitchDoesNotReplaceTheNewLens()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        var pending = new TaskCompletionSource<CommandOutcome<TrialRecord?>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnReadTrial((_, _) => pending.Task);

        var opening = context.OpenProposalAsync("proposal/one", CancellationToken.None);
        var secondProject = Path.Combine(Path.GetTempPath(), "motif-other-project.fwdata");
        await context.OpenProjectAsync(secondProject);
        pending.SetResult(CommandOutcome<TrialRecord?>.Success(Trial()));
        await opening;

        Assert.Equal(secondProject, context.ProjectPath);
        Assert.Null(context.Lens);
        Assert.False(context.EvidenceLens.InProposalMode);
    }

    [Fact]
    public async Task ARefusedTrialRefreshClearsStaleEvidenceAndSurfacesTheRefusal()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        fake.ReadTrialRefusesWith(new Refusal("trial.scratch-gone", FailureReason.NotFound,
            "The Trial scratch is gone."));

        await context.PublishBaselineCapturedAsync(openFirstRunSetup: false);

        Assert.Null(context.Lens?.Trial);
        Assert.Equal("trial.scratch-gone", context.Lens?.ReadRefusal?.Code);
        Assert.Null(context.EvidenceLens.Proposal);
    }

    [Fact]
    public async Task ARefusedTryAgainQueueIsShownWithoutKeepingTheOldTrialAsCurrent()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        fake.EnqueueTrialRefusesWith(new Refusal("trial.queue-busy", FailureReason.Refused,
            "The Trial could not be queued."));

        var outcome = await context.TryAgainAsync("proposal/one", CancellationToken.None);

        Assert.Equal("trial.queue-busy", outcome.Refusal?.Code);
        Assert.Null(context.Lens?.Trial);
        Assert.Equal("trial.queue-busy", context.Lens?.ReadRefusal?.Code);
        Assert.Null(context.EvidenceLens.Proposal);
        Assert.Equal("trial.queue-busy", context.TrialRun.StateFor("proposal/one").ShownRefusal?.Code);
        Assert.Empty(fake.WaitForJobRequests);
    }

    [Fact]
    public async Task RefreshInProposalModeRefreshesFreshnessWithoutQueueingAnotherTrial()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        fake.ReadTrialCompletesWith(Trial() with { Freshness = TrialFreshness.BaselineMoved });

        await context.PublishBaselineCapturedAsync(openFirstRunSetup: false);

        Assert.Equal(TrialFreshness.BaselineMoved, context.Lens?.Trial?.Freshness);
        Assert.Empty(fake.EnqueueTrialRequests);
        Assert.Equal(2, fake.ReadTrialRequests.Count);
    }

    [Fact]
    public async Task TryAgainQueuesOnlyWhenExplicitlyRequested()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        await context.TryAgainAsync("proposal/one", CancellationToken.None);

        var queued = Assert.Single(fake.EnqueueTrialRequests);
        Assert.Equal("proposal/one", queued.ProposalId);
        Assert.Null(queued.Words);
        Assert.False(queued.AllWords);
        Assert.Equal(2, fake.ReadTrialRequests.Count);
    }

    [Fact]
    public async Task TryAgainWithoutAProjectIsARefusalNotAnException()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);

        var outcome = await context.TryAgainAsync("proposal/one", CancellationToken.None);

        Assert.Equal(WindowRefusal.NoProjectCode, outcome.Refusal?.Code);
        Assert.Equal("Open a project first.", WindowRefusal.From(outcome.Refusal!).Sentence);
        Assert.Empty(fake.EnqueueTrialRequests);
    }

    [Fact]
    public async Task TryAgainSaysTryingUntilTheTrialEndsThenShowsItsResult()
    {
        // The window publishes Trial states through the context it was made on, which a pool thread is not.
        await SingleThreadPump.RunAsync(async () =>
        {
            var fake = new FakeCommandClient();
            var context = NewContext(fake);
            await context.OpenProjectAsync(ProjectPath);
            fake.ReadTrialCompletesWith(Trial() with { Freshness = TrialFreshness.ProposalChanged });
            await context.OpenProposalAsync("proposal/one", CancellationToken.None);
            Assert.Equal(TrialStanding.NeedsAnotherTry(TrialStaleReason.ProposalChanged), context.ProposalStanding);
            var waiting = new TaskCompletionSource<CommandOutcome<JobStatusResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            IProgress<JobStatusResponse>? reported = null;
            fake.OnWaitForJob((_, progress, _) =>
            {
                reported = progress;
                return waiting.Task;
            });

            var trying = context.TryAgainAsync("proposal/one", CancellationToken.None);
            reported!.Report(Status(JobStatus.Running) with { TrialProgress = new TrialWordProgress(1, 4, "dogs") });

            Assert.Equal(TrialStanding.Trying, context.ProposalStanding);
            Assert.True(context.TrialRun.IsActive);
            Assert.Equal(1, context.TrialRun.StateFor("proposal/one").Progress?.Completed);
            Assert.Equal(Timeout.InfiniteTimeSpan, Assert.Single(fake.WaitForJobRequests).Timeout);
            fake.ReadTrialCompletesWith(Trial() with { JobId = "job/new" });
            waiting.SetResult(CommandOutcome<JobStatusResponse>.Success(Status(JobStatus.Completed)));
            var outcome = await trying;

            Assert.Equal("job/new", outcome.Value?.JobId);
            Assert.Equal("job/new", context.Lens?.Trial?.JobId);
            Assert.Equal("job/new", context.EvidenceLens.Proposal?.Trial.JobId);
            Assert.Equal(TrialStanding.Current, context.ProposalStanding);
            Assert.False(context.TrialRun.IsActive);
            Assert.Null(context.TrialRun.StateFor("proposal/one").Refusal);
        });
    }

    [Fact]
    public async Task AFailedRetryAfterAGoodTrialIsRefusedAndKeepsTheGoodTrial()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        fake.OnWaitForJob((_, _, _) =>
            Task.FromResult(CommandOutcome<JobStatusResponse>.Success(Status(JobStatus.Failed))));

        var outcome = await context.TryAgainAsync("proposal/one", CancellationToken.None);

        Assert.Equal(RefusalCodes.TrialMeasurementIncomplete, outcome.Refusal?.Code);
        Assert.Equal("job/trial", context.Lens?.Trial?.JobId);
        Assert.Equal(TrialStanding.Current, context.ProposalStanding);
        Assert.Equal("The check did not finish. Try it again.",
            context.TrialRun.StateFor("proposal/one").ShownRefusal?.Sentence);
    }

    [Fact]
    public async Task ASecondTryAgainOfARunningProposalJoinsItAndAnotherProposalLeavesTheOpenOneAlone()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(null);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        Assert.Equal(TrialStanding.NotTried, context.ProposalStanding);
        var waiting = new TaskCompletionSource<CommandOutcome<JobStatusResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnWaitForJob((request, _, _) => request.JobId == "job/test"
            ? waiting.Task
            : Task.FromResult(CommandOutcome<JobStatusResponse>.Success(Status(JobStatus.Completed))));

        var first = context.TryAgainAsync("proposal/two", CancellationToken.None);
        var second = context.TryAgainAsync("proposal/two", CancellationToken.None);

        Assert.Single(fake.EnqueueTrialRequests);
        Assert.True(context.TrialRun.IsRunning("proposal/two"));
        Assert.Equal(TrialStanding.NotTried, context.ProposalStanding);
        Assert.Equal(TrialStanding.Trying, context.StandingOf("proposal/two", null));
        waiting.SetResult(CommandOutcome<JobStatusResponse>.Success(Status(JobStatus.Completed)));
        Assert.Same(await first, await second);
        Assert.Equal("proposal/one", context.Lens?.ProposalId);
    }

    [Fact]
    public async Task LeavingTheProjectStopsFollowingItsTrials()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        CancellationToken waited = default;
        fake.OnWaitForJob((_, _, cancellationToken) =>
        {
            waited = cancellationToken;
            return new TaskCompletionSource<CommandOutcome<JobStatusResponse>>().Task;
        });

        _ = context.TryAgainAsync("proposal/one", CancellationToken.None);
        await context.OpenProjectAsync(OtherProjectPath);

        Assert.True(waited.IsCancellationRequested);
        Assert.False(context.TrialRun.IsActive);
        Assert.False(context.TrialRun.IsRunning("proposal/one"));
    }

    private static JobStatusResponse Status(JobStatus status) =>
        new("job/test", "project/test", true, "trial", status, 1, null, false, null, 1);

    private static TrialRecord Trial() => new("job/trial", "proposal/one", "sha256:intent", Token, null,
        new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["dogs"], false, null,
        SIL.Motif.Contract.Jobs.JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current);

    internal static WorkspaceContext NewContext() => NewContextWithFake().Context;

    internal static WorkspaceContext NewContext(
        ICommandClient commands,
        IAdvancedAiModePreferenceStore? advancedAiModePreferences = null,
        IUriLauncher? uriLauncher = null)
    {
        if (commands is FakeCommandClient fake)
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, DateTimeOffset.UtcNow, false));
        var selection = new SelectionViewModel(commands);
        return new WorkspaceContext(
            selection,
            new AssessViewModel(commands, selection),
            new ChangesViewModel(commands),
            commands,
            new NoFolderPicker(),
            new NoDragSource(),
            new BaselineViewModel(commands),
            advancedAiModePreferences: advancedAiModePreferences,
            uriLauncher: uriLauncher);
    }

    internal static (FakeCommandClient Fake, WorkspaceContext Context) NewContextWithFake()
    {
        var fake = new FakeCommandClient();
        return (fake, NewContext(fake));
    }

    private sealed record ElsewhereRequest(string Note) : PageRequest(WorkspacePage.Diagnostics);

    private sealed class ElsewherePageModel(WorkspaceContext context) : PageModel(context)
    {
        public string? Received { get; private set; }

        public int? Queried { get; private set; }

        public string? Shown { get; private set; }

        protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
            Queried = (await Context.Commands.ListKnownProjectsAsync(cancellationToken)).Count;

        protected override Task OnEvidencePublishedAsync(
            EvidenceLens lens, CancellationToken cancellationToken)
        {
            Shown = lens.Today.Assessment?.Assessment.InvocationId;
            return Task.CompletedTask;
        }

        protected override void OnRequested(PageRequest request)
        {
            if (request is ElsewhereRequest elsewhere) Received = elsewhere.Note;
        }
    }

    private sealed class LoadingPageModel(WorkspaceContext context) : PageModel(context)
    {
        public string? Opened { get; private set; }

        public int Captures { get; private set; }

        protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Opened = projectPath;
        }

        protected override async Task OnBaselineCapturedAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            Captures++;
        }
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
