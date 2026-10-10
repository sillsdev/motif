using System.Globalization;
using System.Reflection;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Assistants;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Contract.Ids;
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
    public async Task ATimingPageModelBuiltFromAContextAloneTakesTheEvidenceTheContextPublishes()
    {
        var context = NewContext();
        var timing = new TimingPageModel(context);
        Assert.False(timing.Context.HasEvidence);

        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.True(timing.Context.HasEvidence);
        Assert.Equal(ProjectPath, timing.Statistics.ProjectPath);
        Assert.Equal("summary", timing.Statistics.SummaryMarkdown);
        Assert.Equal("assessment-1", timing.Statistics.AssessmentId);
    }

    [Fact]
    public async Task CompletingAnAssessmentRefreshesTheTimingPageFromStoredRows()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var response = new TimingResponse("assessment-parse", "all", "kind", 1, 5, 8, [], [], []);
        fake.TimingCompletesWith(response);
        await context.OpenProjectAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.Same(response, timing.KindTiming);
        Assert.Contains(fake.TimingRequests, request => request.AssessmentId == "assessment-parse" &&
            request.WordSet == "all" && request.By == "kind");
        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.UsageEntries);
    }

    [Fact]
    public async Task RerunRefreshesTheSelectedTimingFromTheStoredOverrides()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var calls = 0;
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse(request.AssessmentId!, request.WordSet, request.By, 1, ++calls, calls, [],
                request.By == "rule" ? [new TimingAggregateRow("Fresh rule", "Fresh rule", 1, 1, 1) { Kind = "morph_rule" }] : [], [])
            {
                Words = [new TimingWordRow("dogs", 1, calls > 3 ? "Finished" : "Search limit")],
            })));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        var before = timing.KindTiming;

        context.PublishEvidence(new WorkspaceEvidence(Assessment() with
        {
            TimingOverrideAssessmentIds = ["rerun-parse"],
        }, DateTimeOffset.Now, WasRerun: true));

        Assert.NotSame(before, timing.KindTiming);
        Assert.Equal("Finished", Assert.Single(timing.KindTiming!.Words).Completion);
        Assert.Contains(fake.TimingRequests, request => request.AssessmentId == "assessment-parse" &&
            request.OverrideAssessmentIds is ["rerun-parse"]);
    }

    [Fact]
    public async Task RefreshSelectsARuleThatExistsInTheNewAssessment()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var refreshed = false;
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-parse", request.WordSet, request.By, 1, 1, 1, [],
                request.By == "rule" ? [new TimingAggregateRow(refreshed ? "New rule" : "Old rule", refreshed ? "New rule" : "Old rule", 1, 1, 1) { Kind = "morph_rule" }] : [], []))));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        Assert.Equal("Old rule", timing.SelectedRule?.Key);

        refreshed = true;
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.Equal("New rule", timing.SelectedRule?.Key);
        Assert.Equal("New rule", fake.TimingRequests.Last().Rule?.Key);
    }

    [Fact]
    public async Task OpeningAProjectReadsItsStoredOverviewThroughThePageContext()
    {
        var (fake, context) = NewContextWithFake();
        var overviewPage = new OverviewPageModel(context);
        var current = new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow, null,
            EvidenceFreshness.NoBaseline, null, null, null, null, null);
        var overview = Overview();
        fake.OverviewCompletesWith(overview);
        fake.ReadCurrentEvidenceCompletesWith(current);

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal(ProjectPath, Assert.Single(fake.OverviewRequests).ProjectPath);
        Assert.Equal(ProjectPath, Assert.Single(fake.CurrentEvidenceRequests));
        Assert.Same(current, context.Evidence.Stored);
        Assert.Same(overview, overviewPage.Overview);
    }

    [Fact]
    public async Task CheckingGrammarReloadsOverviewWarningsFromTheCommand()
    {
        var (fake, context) = NewContextWithFake();
        var overviewPage = new OverviewPageModel(context);
        var warningsPage = new WarningsPageModel(context);
        fake.OverviewCompletesWith(Overview());
        fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
            null, EvidenceFreshness.NoBaseline, null, null, null, null, null));
        await context.OpenProjectAsync(ProjectPath);
        Assert.False(overviewPage.HasWarningSummary);
        fake.OverviewCompletesWith(Overview() with
        {
            Warnings = new OverviewWarningsSummary(0, 0, null, null),
        });

        await warningsPage.CheckGrammarCommand.ExecuteAsync(null);

        Assert.True(overviewPage.HasWarningSummary);
        Assert.Equal("0 warnings", overviewPage.WarningsCount);
        Assert.Equal(2, fake.OverviewRequests.Count);
    }

    [Fact]
    public async Task AStoredGrammarReadThatFinishesAfterACheckStartsLeavesTheCheckRunning()
    {
        var (fake, context) = NewContextWithFake();
        var warningsPage = new WarningsPageModel(context);
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
        var checking = warningsPage.CheckGrammarCommand.ExecuteAsync(null);
        Assert.True(warningsPage.Grammar.IsLoading);
        storedRead.SetResult(CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(null)));
        await publication;

        Assert.True(warningsPage.Grammar.IsLoading);
        check.SetResult(CommandOutcome<GrammarCheckResponse>.Success(new GrammarCheckResponse([], true)));
        await checking;
        Assert.False(warningsPage.Grammar.IsLoading);
        Assert.True(warningsPage.Grammar.HasChecked);
    }

    [Fact]
    public async Task OpeningAProjectReadsItsStoredTimingThroughThePageContext()
    {
        var (fake, context) = NewContextWithFake();
        _ = new OverviewPageModel(context);
        var timing = new TimingPageModel(context);
        var current = new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow, null,
            EvidenceFreshness.Current, null, null, null, null, StoredAssessment());
        var response = new TimingResponse("assessment-1", "all", "kind", 1, 5, 8, [], [], []);
        fake.ReadCurrentEvidenceCompletesWith(current);
        fake.OverviewCompletesWith(Overview());
        fake.TimingCompletesWith(response);

        await context.OpenProjectAsync(ProjectPath);

        var request = Assert.Single(fake.TimingRequests, item => item.By == "kind");
        Assert.Equal(ProjectPath, request.ProjectPath);
        Assert.Equal("assessment-1", request.AssessmentId);
        Assert.Equal("all", request.WordSet);
        Assert.Equal("kind", request.By);
        Assert.Same(response, timing.StoredTiming);
        Assert.True(timing.ShowStoredTiming);
        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.StatsRequests);
    }

    [Fact]
    public async Task OpeningAProjectRestoresTheStoredMatrixAndHandoff()
    {
        var (fake, context) = NewContextWithFake();
        _ = new OverviewPageModel(context);
        var texts = new TextsPageModel(context);
        var handoff = new AiHandoffPageModel(context);
        Assert.Same(context.Changes, texts.Assess.Compare.Changes);
        var baseline = new BaselineRecord("project-1", Token, "root", ProjectPath,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var assessment = StoredAssessment() with
        {
            Words = [new AssessedWord("dogs", "no-analysis", [], 10)
            {
                ProjectStanding = ProjectStanding.Approved,
                OccurrenceCount = 2,
            }],
            Invocation = new BatchInvocationEvidence("invocation/one", "source", "digest", "digest",
                "words", "digest", "tsv", "digest", "stderr", "digest", 1000,
                StepCap.Default, 1, false),
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
        Assert.False(texts.ShowEmptyResults);
        Assert.NotNull(handoff.Handoff.LatestAssessmentAt);
        Assert.Equal("invocation/one", handoff.Handoff.InvocationId);
    }

    [Fact]
    public async Task RestoredTimedOutWordRemainsIncomplete()
    {
        var (fake, context) = NewContextWithFake();
        _ = new OverviewPageModel(context);
        _ = new TextsPageModel(context);
        var baseline = new BaselineRecord("project-1", Token, "root", ProjectPath,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
            null, EvidenceFreshness.Current, baseline, null, null, null,
            StoredAssessment() with { Words = [new AssessedWord("dogs", "timed-out", [])] }));
        fake.OverviewCompletesWith(Overview());

        await context.OpenProjectAsync(ProjectPath);

        Assert.True(Assert.Single(context.Assess.Words.AllRows).IsIncomplete);
        Assert.Contains("1 incomplete", context.Assess.Result!.CompletionSummary);
    }

    [Theory]
    [InlineData("step-limit", 10)]
    [InlineData("slowest", 20)]
    [InlineData("all", 10)]
    [InlineData("cell:approved:no-parse", 10)]
    public async Task TimingWordSetsUseTheStoredCommandWithoutStartingAParser(string wordSet, int top)
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", wordSet, "kind", 1, 5, 8, [], [], []));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        timing.SlowestCount = 20;
        await timing.SelectWordSetCommand.ExecuteAsync(wordSet);

        Assert.Contains(fake.TimingRequests, request => request.WordSet == wordSet && request.By == "kind" &&
            request.Top == top);
        Assert.Contains(fake.TimingRequests, request => request.WordSet == wordSet && request.By == "rule" &&
            request.Top == top);
        Assert.Equal(wordSet == "step-limit", timing.IsStepLimitSelected);
        Assert.Equal(wordSet == "slowest", timing.IsSlowestSelected);
        Assert.Equal(wordSet == "all", timing.IsAllSelected);
        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.StatsRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectingATimingWordSetRecordsOneActionForSuccessAndRefusal(bool refuses)
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) =>
        {
            if (refuses && request.WordSet == "step-limit" && request.By == "kind")
                return Task.FromResult(CommandOutcome<TimingResponse>.Refused(new Refusal(
                    "timing.refused", FailureReason.Refused, "Timing is unavailable.")));
            IReadOnlyList<TimingAggregateRow> aggregates = request.By == "rule"
                ? [new TimingAggregateRow("Verb template", "Verb template", 1, 1, 1) { Kind = "morph_rule" }] : [];
            return Task.FromResult(CommandOutcome<TimingResponse>.Success(new TimingResponse(
                "assessment-1", request.WordSet, request.By, 1, 1, 1, [], aggregates, [])));
        });
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.Empty(fake.UsageEntries);
        await timing.SelectWordSetCommand.ExecuteAsync("step-limit");

        var entry = Assert.Single(fake.UsageEntries);
        Assert.Equal("timing", entry.Command);
        Assert.Equal(["wordSet:text"], entry.ArgumentShape);
        Assert.Equal(refuses, timing.HasTimingRefusal);
    }

    [Theory]
    [InlineData("picked", false)]
    [InlineData("picked", true)]
    [InlineData("texts", false)]
    [InlineData("texts", true)]
    [InlineData("checked", false)]
    [InlineData("checked", true)]
    [InlineData("matrix", false)]
    [InlineData("matrix", true)]
    public async Task ExplicitTimingSourceActionsRecordOnceForSuccessAndRefusal(string source, bool refuses)
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) =>
        {
            if (refuses && request.By == "kind")
                return Task.FromResult(CommandOutcome<TimingResponse>.Refused(new Refusal(
                    "timing.refused", FailureReason.Refused, "Timing is unavailable.")));
            return Task.FromResult(CommandOutcome<TimingResponse>.Success(new TimingResponse(
                "assessment-1", request.WordSet, request.By, 1, 5, 8, [], [], [])));
        });
        await context.OpenProjectAsync(ProjectPath);

        Func<Task> execute;
        string shape;
        switch (source)
        {
            case "picked":
                timing.PickedWords = "dogs\ncats";
                execute = () => timing.UsePickedWordsCommand.ExecuteAsync(null);
                shape = "words:list(2)";
                break;
            case "texts":
                context.Assess.Compare.Load([
                    new AssessWordRowViewModel(new AssessmentWordResult("dogs", "analysed", false, "Finished", 5, null)),
                    new AssessWordRowViewModel(new AssessmentWordResult("cats", "no-parse", false, "Finished", 8, null)),
                ]);
                var list = context.Assess.Compare.Presets.First(preset => preset.Count > 0);
                timing.SelectedTextsList = list;
                shape = $"words:list({context.Assess.Compare.WordsInPreset(list).Count})";
                execute = () => timing.UseTextsListCommand.ExecuteAsync(null);
                break;
            case "checked":
                context.Assess.Compare.Load([
                    new AssessWordRowViewModel(new AssessmentWordResult("dogs", "analysed", false, "Finished", 5, null)),
                ]);
                context.Assess.Compare.Words.Single(word => word.Word == "dogs").IsChecked = true;
                execute = () => timing.UseCheckedWordsCommand.ExecuteAsync(null);
                shape = "words:list(1)";
                break;
            case "matrix":
                timing.SelectedMatrixCell = new CompareCellViewModel(WordProjectStatus.Approved,
                    CompareColumnKind.NoParse);
                execute = () => timing.UseMatrixCellCommand.ExecuteAsync(null);
                shape = "wordSet:text";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }

        Assert.Empty(fake.UsageEntries);
        await execute();

        var entry = Assert.Single(fake.UsageEntries);
        Assert.Equal("timing", entry.Command);
        Assert.Equal([shape], entry.ArgumentShape);
        Assert.Equal(refuses, timing.HasTimingRefusal);
        Assert.Equal(!refuses, timing.KindTiming is not null);
        Assert.Contains(fake.TimingRequests, request => request.By == "kind");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChoosingATimingRuleRecordsOneActionForSuccessAndRefusal(bool refuses)
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var refuseChosenRule = false;
        fake.OnTiming((request, _) =>
        {
            if (refuseChosenRule && request.Rule?.Key == "Verb template")
                return Task.FromResult(CommandOutcome<TimingResponse>.Refused(new Refusal(
                    "timing.refused", FailureReason.Refused, "Timing is unavailable.")));
            IReadOnlyList<TimingAggregateRow> aggregates = request.By == "rule"
                ? [new TimingAggregateRow("Verb template", "Verb template", 1, 1, 1) { Kind = "morph_rule" }] : [];
            return Task.FromResult(CommandOutcome<TimingResponse>.Success(new TimingResponse(
                "assessment-1", request.WordSet, request.By, 1, 1, 1, [], aggregates, [])));
        });
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.Empty(fake.UsageEntries);
        refuseChosenRule = refuses;
        await timing.ChooseRuleCommand.ExecuteAsync(Assert.Single(timing.RuleTiming!.Aggregates));

        var entry = Assert.Single(fake.UsageEntries);
        Assert.Equal("timing", entry.Command);
        Assert.Equal(["rule:text"], entry.ArgumentShape);
        Assert.Equal(refuses, timing.HasTimingRefusal);
    }

    [Fact]
    public async Task TimingPageShowsTheCommandsKindAndRuleAggregatesUnchanged()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var kindRows = new[] { new TimingAggregateRow("morph_rule", "morph_rule", 12, 1, 2) { Kind = "morph_rule" } };
        var ruleRows = new[] { new TimingAggregateRow("Verb template", "Verb template", 10, 10d / 12, 2)
            { Kind = "morph_rule" },
            new TimingAggregateRow("Other rule", "Other rule", 2, 2d / 12, 2) { Kind = "morph_rule" } };
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-1", request.WordSet, request.By, 2, 6, 6, [],
                request.By == "kind" ? kindRows : ruleRows, [])
            {
                Words = [new TimingWordRow("word-1", 6, TimingCompletion.StepLimit),
                    new TimingWordRow("word-2", 6, TimingCompletion.StepLimit)],
                Attribution = new WordTimeAttribution(2, 12, 12, 0, 0, 0, false),
            })));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        await timing.SelectWordSetCommand.ExecuteAsync("step-limit");

        Assert.Equal(kindRows, timing.KindTiming!.Aggregates);
        Assert.Equal(ruleRows, timing.RuleTiming!.Aggregates);
        Assert.Equal("morph_rule", timing.SelectedRuleRow!.Kind);
        Assert.Equal("10 ms · 83% of these 2 words' 12 ms · recorded in 2 words", timing.RuleSummary);
    }

    // The chosen row needs its own look; hover's grey alone could not tell it from the row under the pointer.
    [Fact]
    public async Task ChoosingATimingRuleMarksOnlyThatRowChosen()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) =>
        {
            IReadOnlyList<TimingAggregateRow> aggregates = request.By == "rule"
                ? [new TimingAggregateRow("Subject agreement", "Subject agreement", 3, 0.6, 2) { Kind = "morph_rule" }, new TimingAggregateRow("Past tense li-", "Past tense li-", 2, 0.4, 1) { Kind = "morph_rule" }]
                : [];
            return Task.FromResult(CommandOutcome<TimingResponse>.Success(new TimingResponse(
                "assessment-1", request.WordSet, request.By, 1, 1, 1, [], aggregates, [])));
        });
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        Assert.Equal([true, false], timing.RuleRows.Select(row => row.IsChosen));

        await timing.ChooseRuleCommand.ExecuteAsync(timing.RuleRows[1].Row);

        Assert.Equal([false, true], timing.RuleRows.Select(row => row.IsChosen));
    }

    [Theory]
    [InlineData("morph_rule", "Morphological rules")]
    [InlineData("phon_rule", "Phonological rules")]
    [InlineData("root_index", "Root lookup")]
    [InlineData("lex_entry", "Lexical entries")]
    public void TimingDisplaysReadableRuleKindLabels(string storedKind, string label) =>
        Assert.Equal(label, TimingShare.KindName(storedKind));

    [Fact]
    public async Task TimingShowsFiveCostliestWordsButHandsOffEveryWordUnderTheRule()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var handoff = new AiHandoffPageModel(context);
        var costliest = Enumerable.Range(1, 6).Select(index =>
            new WordRuleTiming($"word{index}", 10 - index, index)).ToArray();
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-1", request.WordSet, request.By, 6, 5, 8, [],
                request.By == "rule" ? [new TimingAggregateRow("Verb template", "Verb template", 10, 1, 6) { Kind = "morph_rule" }] : [],
                request.Rule is null ? [] : costliest))));
        await context.OpenProjectAsync(ProjectPath);

        await timing.SelectWordSetCommand.ExecuteAsync("all");

        Assert.Equal(costliest.Take(5), timing.CostliestRuleWords);
        timing.HandOffRuleCommand.Execute(null);
        Assert.Equal(costliest.Select(word => word.Word), handoff.Handoff.ChosenWords);
    }

    [Fact]
    public async Task TimingPickedAndTextsWordsUseExactExplicitWordsAndHandOffTheCommandSelectedWords()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var handoff = new AiHandoffPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "all", "kind", 2, 5, 8, [], [], [])
        {
            Words = [new TimingWordRow("dogs", 5, "Finished"),
                new TimingWordRow("cats", 8, "Search limit")],
        });
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        timing.PickedWords = "dogs\ncats";
        await timing.UsePickedWordsCommand.ExecuteAsync(null);
        Assert.Equal(["dogs", "cats"], fake.TimingRequests[^1].ExplicitWords);
        timing.HandOffWordsCommand.Execute(null);
        Assert.Equal(["dogs", "cats"], handoff.Handoff.ChosenWords);
    }

    [Fact]
    public async Task TimingMatrixCellUsesTheCommandsCellWordSet()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "cell:approved:no-parse", "kind",
            1, 5, 8, [], [], []));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        timing.SelectedMatrixCell = new CompareCellViewModel(WordProjectStatus.Approved, CompareColumnKind.NoParse);

        await timing.UseMatrixCellCommand.ExecuteAsync(null);

        Assert.Contains(fake.TimingRequests, request => request.WordSet == "cell:approved:no-parse" &&
            request.By == "kind" && request.ExplicitWords is null);
    }

    [Fact]
    public async Task TimingChosenInTextsIncludesCheckedWordsHiddenByTheTextsFilter()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "all", "kind", 1, 5, 8, [], [], []));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        context.Assess.Compare.Load([
            new AssessWordRowViewModel(new AssessmentWordResult("dogs", "analysed", false, "Finished", 5, null)),
            new AssessWordRowViewModel(new AssessmentWordResult("cats", "analysed", false, "Finished", 8, null)),
        ]);
        context.Assess.Compare.Words.Single(word => word.Word == "dogs").IsChecked = true;
        context.Assess.Compare.SearchText = "cats";

        await timing.UseCheckedWordsCommand.ExecuteAsync(null);

        Assert.Equal(["dogs"], fake.TimingRequests.Last(request => request.By == "kind").ExplicitWords);
    }

    [Fact]
    public async Task TimingListFromTextsUsesThatListsWords()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "all", "kind", 1, 5, 8, [], [], []));
        await context.OpenProjectAsync(ProjectPath);
        context.Assess.Compare.Load([
            new AssessWordRowViewModel(new AssessmentWordResult("dogs", "analysed", false, "Finished", 5, null)),
            new AssessWordRowViewModel(new AssessmentWordResult("cats", "no-parse", false, "Finished", 8, null)),
        ]);
        timing.SelectedTextsList = context.Assess.Compare.Presets.First(preset => preset.Count > 0);
        var expected = context.Assess.Compare.Words.Where(word =>
            timing.SelectedTextsList.Cells.Any(cell => cell.Row == word.Row && cell.Column == word.Column))
            .Select(word => word.Word).ToArray();
        Assert.NotEmpty(expected);
        context.Assess.Compare.SearchText = "no matching words";
        Assert.Empty(context.Assess.Compare.Words);

        await timing.UseTextsListCommand.ExecuteAsync(null);

        Assert.Equal(expected.Order(StringComparer.Ordinal), fake.TimingRequests[0].ExplicitWords!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EmptyTimingSelectionExplainsTheAbsenceOfPercentilesAndCannotHandOffWords()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "step-limit", "kind", 0, null, null,
            [], [], []));
        await context.OpenProjectAsync(ProjectPath);

        await timing.SelectWordSetCommand.ExecuteAsync("step-limit");

        Assert.True(timing.ShowEmptySelection);
        Assert.Equal("No words in this selection have recorded parse time.", timing.PercentileSummary);
        Assert.False(timing.HandOffWordsCommand.CanExecute(null));
        Assert.False(timing.RerunWordsCommand.CanExecute(null));
    }

    [Fact]
    public async Task TimingRerunReportsEachWordAndPassesTimeAndStepLimitsThroughSelection()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "all", "kind", 2, 5, 8, [], [], [])
        {
            Words = [new TimingWordRow("dogs", 5, "Search limit"),
                new TimingWordRow("cats", 8, "Finished")],
        });
        fake.AssessCompletesWith(Assessment());
        await context.OpenProjectAsync(ProjectPath);
        context.Assess.ProjectPath = ProjectPath;
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        timing.PickedWords = "dogs\ncats";
        await timing.UsePickedWordsCommand.ExecuteAsync(null);
        timing.RerunSeconds = 45;
        timing.RerunSteps = 1000000;
        var usageCountBeforeRerun = fake.UsageEntries.Count;
        var progress = new List<string>();
        timing.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(TimingPageModel.RerunWord) && timing.RerunWord is not null)
                progress.Add(timing.RerunProgressText);
        };

        await timing.RerunWordsCommand.ExecuteAsync(null);

        Assert.Equal(["0 of 2 words done · parsing dogs", "1 of 2 words done · parsing cats"], progress);
        Assert.Equal(2, timing.RerunCompleted);
        Assert.Equal("Re-run complete.", timing.RerunMessage);
        Assert.Equal(["dogs", "cats"], fake.AssessRequests.Select(request => Assert.Single(request.Selection!.Words)));
        var rerunEntry = Assert.Single(fake.UsageEntries.Skip(usageCountBeforeRerun));
        Assert.Equal("assess", rerunEntry.Command);
        Assert.Equal(["fwDataPath:text", "words:list(2)", "perWordLimitMs:number", "perWordStepLimit:number"], rerunEntry.ArgumentShape);
        Assert.All(fake.AssessRequests, request =>
        {
            Assert.Equal(45000, request.PerWordLimitMs);
            Assert.Equal(new SIL.Motif.Contract.Assess.StepCap(1000000), request.Selection!.PerWordStepLimit);
        });
    }

    [Fact]
    public async Task InvalidTimingRerunRecordsOneRefusedAction()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "all", "kind", 1, 5, 8, [], [], [])
        {
            Words = [new TimingWordRow("dogs", 5, "Finished")],
        });
        await context.OpenProjectAsync(ProjectPath);
        context.Assess.ProjectPath = ProjectPath;
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        timing.PickedWords = "dogs";
        await timing.UsePickedWordsCommand.ExecuteAsync(null);
        var usageCountBeforeRerun = fake.UsageEntries.Count;
        timing.RerunSeconds = 0;

        await timing.RerunWordsCommand.ExecuteAsync(null);

        Assert.Equal("Enter a positive per-word time and a positive whole-number analysis attempt limit.", timing.RerunMessage);
        Assert.Empty(fake.AssessRequests);
        var rerunEntry = Assert.Single(fake.UsageEntries.Skip(usageCountBeforeRerun));
        Assert.Equal("assess", rerunEntry.Command);
    }

    [Fact]
    public async Task TimingCancelStopsTheCurrentWordAndDoesNotStartTheNextOne()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "all", "kind", 2, 5, 8, [], [], [])
        {
            Words = [new TimingWordRow("dogs", 5, "Search limit"),
                new TimingWordRow("cats", 8, "Finished")],
        });
        fake.AssessBlocksUntilCancelled(new Refusal("assessment.cancelled", FailureReason.Cancelled, "Cancelled."));
        await context.OpenProjectAsync(ProjectPath);
        context.Assess.ProjectPath = ProjectPath;
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        timing.PickedWords = "dogs\ncats";
        await timing.UsePickedWordsCommand.ExecuteAsync(null);

        var usageCountBeforeRerun = fake.UsageEntries.Count;
        var running = timing.RerunWordsCommand.ExecuteAsync(null);
        Assert.Equal("dogs", timing.RerunWord);
        timing.CancelRerunCommand.Execute(null);
        await running;

        Assert.Equal("Re-run cancelled.", timing.RerunMessage);
        Assert.Equal(0, timing.RerunCompleted);
        Assert.Single(fake.AssessRequests);
        Assert.Equal(RunState.Cancelled, context.Assess.State);
        var rerunEntry = Assert.Single(fake.UsageEntries.Skip(usageCountBeforeRerun));
        Assert.Equal("assess", rerunEntry.Command);
    }

    [Fact]
    public async Task AnAiHandoffPageModelBuiltFromAContextAloneCoversThePublishedAssessmentAndForgetsItOnClear()
    {
        var context = NewContext();
        var handoff = new AiHandoffPageModel(context);
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
    public async Task PendingChangesFollowAProjectSwitchOnlyThroughTheirSlotAndTheReviewBadgeCountsThem()
    {
        var context = NewContext();
        var review = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        Assert.Equal(ProjectPath, context.Changes.ProjectPath);
        Assert.Equal(string.Empty, review.Badge);

        context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "dogz", string.Empty));
        Assert.Equal("1", review.Badge);

        context.ClearProject();
        Assert.False(context.Changes.HasItems);
        Assert.Equal(string.Empty, review.Badge);

        await context.OpenProjectAsync(OtherProjectPath);
        Assert.Equal(OtherProjectPath, context.Changes.ProjectPath);
        Assert.False(context.Changes.HasItems);
        Assert.Equal(string.Empty, review.Badge);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling, false, false, false)]
    [InlineData(ChangeKinds.IncorrectSpelling, true, false, true)]
    [InlineData(ChangeKinds.AddCandidate, false, false, true)]
    [InlineData(ChangeKinds.AddCandidate, true, false, false)]
    [InlineData(ChangeKinds.IncorrectSpelling, false, true, false)]
    [InlineData(ChangeKinds.AddCandidate, false, true, true)]
    public void ReviewNavigationPreservesAnOccurrenceFreeHomographsIdentity(
        string kind, bool reverse, bool absent, bool rowRoute)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var ownId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
            var otherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
            var (fake, context) = NewContextWithFake();
            var texts = new TextsPageModel(context);
            var review = new ReviewPageModel(context);
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

            Assert.Single(review.ReviewGroups);
            if (rowRoute) change.Listed!.Row.OpenInTextCommand.Execute(null);
            else review.GoToTextCommand.Execute(change);

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
            texts.AnalyzeView = AnalyzeTextsView.WordList;
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
            Assert.Equal(TextsTab.AnalyzeTexts, texts.Tab);
            Assert.Equal(AnalyzeTextsView.TextReader, texts.AnalyzeView);
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
    public void TryingAWordThroughTheContextOpensTryAWordOnThatWord()
    {
        var context = NewContext();
        var tryWord = new TryWordPageModel(context);

        context.TryWord("dogs");

        Assert.Equal(WorkspacePage.TryAWord, context.CurrentPage);
        Assert.Equal("dogs", tryWord.Trace.WordToTry);
    }

    [Fact]
    public void ListsCanHandOffEveryWordInTheSelectedList()
    {
        var (fake, context) = NewContextWithFake();
        var texts = new TextsPageModel(context);
        var handoff = new AiHandoffPageModel(context);
        context.Assess.Result = Assessment() with
        {
            Words = [ApprovedUnparsed("dogs"), ApprovedUnparsed("cats")],
        };
        texts.TextsLists.SelectListCommand.Execute(texts.TextsLists.Lists.Single(list =>
            list.Name == "Lost"));

        Assert.True(texts.TextsLists.HandOffListCommand.CanExecute(null));
        texts.TextsLists.HandOffListCommand.Execute(null);

        Assert.Equal(["dogs", "cats"], handoff.Handoff.ChosenWords);
    }

    [Fact]
    public void ListsCanHandOffOnlyTheTickedWordsInTheSelectedList()
    {
        var (fake, context) = NewContextWithFake();
        var texts = new TextsPageModel(context);
        var handoff = new AiHandoffPageModel(context);
        context.Assess.Result = Assessment() with
        {
            Words = [ApprovedUnparsed("dogs"), ApprovedUnparsed("cats")],
        };
        texts.TextsLists.SelectListCommand.Execute(texts.TextsLists.Lists.Single(list =>
            list.Name == "Lost"));
        texts.Assess.Compare.Words.Single(word => word.Word == "cats").IsChecked = true;

        Assert.True(texts.TextsLists.HandOffCheckedWordsCommand.CanExecute(null));
        texts.TextsLists.HandOffCheckedWordsCommand.Execute(null);

        Assert.Equal(["cats"], handoff.Handoff.ChosenWords);
    }

    [Fact]
    public void AnalyzeTextsCanHandOffOnlyTheTickedWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContextWithFake();
            var texts = new TextsPageModel(context);
            var handoff = new AiHandoffPageModel(context);
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
    public void MatrixAnalyzeTextsAndListsShowTheSameWords()
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
            var matrixWord = Assert.Single(texts.Assess.Compare.Words);
            var list = Assert.Single(texts.TextsLists.Lists, candidate =>
                candidate.Cells.Contains(new TextsListCell(matrixWord.Row, matrixWord.Column)));
            texts.TextsLists.SelectListCommand.Execute(list);

            Assert.Equal(["kitabu"], texts.Words.Rows.Select(row => row.Form));
            texts.ResultsInText.ShowReader();
            var occurrence = Assert.Single(context.SelectionReads.Summary!.SourcePositions);
            Assert.Equal("kitabu", (await texts.ResultsInText.ReadOccurrenceAsync(occurrence.Location.Anchor))?.Form);
            Assert.Equal(["kitabu"], texts.Assess.Compare.Words.Select(word => word.Word));
            Assert.Equal(["kitabu"], texts.TextsLists.Compare.Words.Select(word => word.Word));
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AnAnalyzeChangeShowsNotAppliedYetInTheMatrixAndItsList()
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
            var matrixWord = Assert.Single(texts.Assess.Compare.Words);
            var list = Assert.Single(texts.TextsLists.Lists, candidate =>
                candidate.Cells.Contains(new TextsListCell(matrixWord.Row, matrixWord.Column)));
            texts.TextsLists.SelectListCommand.Execute(list);
            await texts.ResultsInText.SelectionRefresh;
            Assert.Null(context.SelectionReads.Refusal);
            texts.ResultsInText.ShowReader();
            var token = Assert.IsType<ResultsTokenViewModel>(await texts.ResultsInText.ReadOccurrenceAsync(
                Assert.Single(context.SelectionReads.Summary!.SourcePositions).Location.Anchor));
            texts.ResultsInText.SelectToken(token);

            await texts.ResultsInText.AddChangeCommand.ExecuteAsync(ChangeKinds.IncorrectSpelling);

            Assert.Equal("Not applied yet", token.PendingChangeStatus);
            Assert.Equal("Not applied yet", Assert.Single(texts.Assess.Compare.Words).PendingChangeStatus);
            Assert.Equal("Not applied yet", list.PendingChangeStatus);

            context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "kitabu", "",
                fit: new ChangeFit("stale-change", false, ["The project changed."]),
                wordformId: CanonicalId.FromGuid(wordformId).Value));

            Assert.Equal(PendingChangeState.NoLongerFits, token.PendingState);
            Assert.Equal(PendingChangeState.NoLongerFits, Assert.Single(texts.Assess.Compare.Words).PendingState);
            Assert.Equal(PendingChangeState.NoLongerFits, list.PendingState);
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
        IReadOnlyList<string>? addedWords = null)
    {
        var fixture = StoredSelectionFixture.FromDisplayRecords(records, assessment?.Baseline.Token ?? Token);
        if (assessment is not null) fixture.RecordAssessment(assessment);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(assessment?.Baseline.Token ?? Token,
            DateTimeOffset.UtcNow, false));
        fake.SelectionReaderHandler = fixture.OpenAsync;
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
        return new CapturedSelectionOwner(context, fixture);
    }

    internal sealed class CapturedSelectionOwner(WorkspaceContext context, StoredSelectionFixture fixture) : IAsyncDisposable
    {
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
                Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
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
                Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
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
    public void HandingOffWordsThroughTheContextOpensTheAiHandoffPageOnThoseWords()
    {
        var context = NewContext();
        var handoff = new AiHandoffPageModel(context);

        context.HandOff(["dogs", "cats"]);

        Assert.Equal(WorkspacePage.AiHandoff, context.CurrentPage);
        Assert.Equal(["dogs", "cats"], handoff.Handoff.ChosenWords);
    }

    [Fact]
    public async Task OpeningTimingOnWordsQueriesStoredTimingForOnlyThoseWords()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var result = new TimingResponse("assessment-1", "all", "kind", 1, 5, 5, [],
            [new TimingAggregateRow("Affix template", "Affix template", 5, 1, 1) { Kind = "morph_rule" }], []);
        fake.TimingCompletesWith(result);
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        context.OpenTiming(["dogs"], null);
        await timing.LoadFocusedTimingCommand.ExecutionTask!;

        Assert.Equal(WorkspacePage.Timing, context.CurrentPage);
        var request = Assert.Single(fake.TimingRequests, item => item.By == "kind" &&
            item.ExplicitWords is { Count: > 0 });
        Assert.Equal("assessment-parse", request.AssessmentId);
        Assert.Equal("kind", request.By);
        Assert.Equal(["dogs"], request.ExplicitWords);
        Assert.Same(result, timing.FocusedTiming);
        Assert.Empty(fake.StatsRequests);
    }

    [Fact]
    public async Task OpeningTimingOnARuleQueriesThatRuleForTheChosenWords()
    {
        var ruleKey = new TraceTimingKey("morph_rule", "aaaaaaaa-0000-0000-0000-000000000001");
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var result = new TimingResponse("assessment-1", "all", "rule", 1, 5, 5, [],
            [new TimingAggregateRow(ruleKey.Key, "Plural", 5, 1, 1) { Kind = "morph_rule", IdentityQuality = "authored" }], [new WordRuleTiming("dogs", 5, 2)]);
        fake.TimingCompletesWith(result);
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        context.OpenTiming(["dogs"], ruleKey);
        await timing.LoadFocusedTimingCommand.ExecutionTask!;

        Assert.Equal(["dogs"], timing.Focus!.Words);
        var request = Assert.Single(fake.TimingRequests, item => item.Rule?.Key == ruleKey.Key &&
            item.ExplicitWords is { Count: > 0 });
        Assert.Equal("rule", request.By);
        Assert.Equal(ruleKey, request.Rule);
        Assert.Equal(["dogs"], request.ExplicitWords);
        Assert.Same(result, timing.FocusedTiming);
        Assert.Empty(fake.StatsRequests);
    }

    [Fact]
    public async Task APageDefinedOutsideTheAppRunsItsOwnQueryAndIsOpenedThroughARegistryEntryAndTheContextAlone()
    {
        var (fake, context) = NewContextWithFake();
        fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, DateTimeOffset.UtcNow)]);
        var entry = PageEntry.Of(WorkspacePage.Warnings, "Elsewhere", "M0 0h1", c => new ElsewherePageModel(c),
            _ => new Avalonia.Controls.Border());
        var model = (ElsewherePageModel)entry.CreateModel(context);

        await context.OpenProjectAsync(ProjectPath);
        Assert.Equal(1, model.Queried);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        Assert.Equal("invocation/one", model.Shown);

        context.Open(new ElsewhereRequest("note"));
        Assert.Equal(WorkspacePage.Warnings, context.CurrentPage);
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
    public async Task OpenInspectorShowsTheObjectBesideThePageAndANewProjectClosesIt()
    {
        var context = NewContext();
        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Timing);
        var kat = InspectorSubject.Morpheme(new ParserReadingMorph("kat", "cut", "v", null, false, null)
        {
            AllomorphId = "form-kat", GrammaticalInfoId = "msa-kat",
        })!;
        var changed = new List<string?>();
        context.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        context.OpenInspector(kat);

        Assert.Equal(WorkspacePage.Timing, context.CurrentPage);
        Assert.Equal(new OpenInspectorRequest(kat), context.Inspector);
        Assert.Contains(nameof(WorkspaceContext.Inspector), changed);
        context.CloseInspector();
        Assert.Null(context.Inspector);
        context.OpenInspector(kat);
        await context.OpenProjectAsync(OtherProjectPath);
        Assert.Null(context.Inspector);
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

    private static AssessCommandResponse Assessment() => new(
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

    private static AssessmentRecord StoredAssessment() => new(
        "assessment-1", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
        "whitespace", "1", "{}", Selection.Create("Default", ["dogs"]), null, null,
        "sha256:grammar", null, null, null, "2026-09-24T12:00:00.0000000+00:00", Words: []);

    private static OverviewResponse Overview() => new(
        "one", DateTimeOffset.UtcNow, null, 0, 0, 0, 0, 0, 0, 0, null, null, null, null, null,
        new OverviewTextCoverage(0, 0, 0, 0, 0, 0), new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
        new OverviewTiming(null, null, [], 0), null);

    internal static WorkspaceContext NewContext() => NewContextWithFake().Context;

    internal static WorkspaceContext NewContext(
        ICommandClient commands,
        IAdvancedAiModePreferenceStore? advancedAiModePreferences = null,
        IAssistantConnectionService? assistantConnections = null,
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
            assistantConnections: assistantConnections,
            uriLauncher: uriLauncher);
    }

    private static (FakeCommandClient Fake, WorkspaceContext Context) NewContextWithFake()
    {
        var fake = new FakeCommandClient();
        return (fake, NewContext(fake));
    }

    private sealed record ElsewhereRequest(string Note) : PageRequest(WorkspacePage.Warnings);

    private sealed class ElsewherePageModel(WorkspaceContext context) : PageModel(context)
    {
        public string? Received { get; private set; }

        public int? Queried { get; private set; }

        public string? Shown { get; private set; }

        protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
            Queried = (await Context.Commands.ListKnownProjectsAsync(cancellationToken)).Count;

        protected override Task OnEvidencePublishedAsync(
            ProjectEvidence evidence, CancellationToken cancellationToken)
        {
            Shown = evidence.Assessment?.Assessment.InvocationId;
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
