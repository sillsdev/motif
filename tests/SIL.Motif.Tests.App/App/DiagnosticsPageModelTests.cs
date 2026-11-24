using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class DiagnosticsPageModelTests
{
    [Fact]
    public void RouteSelectsTheRequestedTabOnlyWhenDiagnosticsIsEnabled()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var page = new DiagnosticsPageModel(context);
        context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
        Assert.Equal(WorkspacePage.Progress, context.CurrentPage);
        Assert.Equal(0, page.SelectedTabIndex);

        context.Diagnostics = true;
        context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);

        Assert.Equal(WorkspacePage.Diagnostics, context.CurrentPage);
        Assert.Equal(1, page.SelectedTabIndex);
        context.OpenDiagnostics(DiagnosticsTab.Warnings);
        Assert.Equal(0, page.SelectedTabIndex);
    }

    [UiThreadFact]
    public async Task SpeedByRuleRetainsUnattributedRecordedWork()
    {
        var fake = new FakeCommandClient();
        var rows = new[]
        {
            new TimingWorkAggregate("rule", "Recorded rule", "morph_rule", 4, false),
            new TimingWorkAggregate("unattributed", "Not attributed", "unattributed", 6, true),
        };
        fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
        {
            ParserWorkAggregates = rows,
        });
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var assessment = WorkspaceContextTests.Assessment() with { TimingOverrideAssessmentIds = ["assessment/replacement"] };
        context.PublishEvidence(new WorkspaceEvidence(assessment, DateTimeOffset.Now, false));
        var speed = new SpeedByRuleViewModel(context);

        await speed.ReadAsync("project.fwdata", CancellationToken.None);

        Assert.Equal(rows, speed.Rows);
        var request = Assert.Single(fake.TimingRequests, request => request.By == "rule");
        Assert.Equal(["assessment/replacement"], request.OverrideAssessmentIds);
        Assert.Empty(fake.AssessRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task AReplacementAssessmentHidesAnEarlierTimingRead()
    {
        var fake = new FakeCommandClient();
        var result = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<TimingResponse>>();
        fake.OnTiming((_, _) => result.Task);
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var assessment = WorkspaceContextTests.Assessment();
        context.PublishEvidence(new WorkspaceEvidence(assessment, DateTimeOffset.Now, false));
        var speed = new SpeedByRuleViewModel(context);
        var read = speed.ReadAsync("project.fwdata", CancellationToken.None);

        context.PublishEvidence(new WorkspaceEvidence(assessment with
        {
            TimingOverrideAssessmentIds = ["assessment/replacement"],
        }, DateTimeOffset.Now, false));
        result.SetResult(SIL.Motif.Contract.Commands.CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
            {
                ParserWorkAggregates = [new TimingWorkAggregate("old", "Old rule", "morph_rule", 4, false)],
            }));
        await read;

        Assert.Empty(speed.Rows);
        await context.StopProjectWorkAsync();
    }

    [UiThreadTheory]
    [InlineData("stale")]
    [InlineData("saved-since")]
    [InlineData("partial-totals")]
    [InlineData("other-assessment")]
    [InlineData("other-grouping")]
    public async Task SpeedByRuleDoesNotPresentIncompleteOrOutdatedRows(string condition)
    {
        var fake = new FakeCommandClient();
        var response = new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
        {
            ParserWorkAggregates = [new TimingWorkAggregate("rule", "Recorded rule", "morph_rule", 4, false)],
        };
        response = condition switch
        {
            "stale" => response with { IsStale = true },
            "saved-since" => response with { CurrentProjectIsStale = true },
            "partial-totals" => response with { ObjectTimingAggregatesAvailable = false },
            "other-assessment" => response with { AssessmentId = "assessment/other" },
            _ => response with { By = "kind" },
        };
        fake.TimingCompletesWith(response);
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.Now, false));
        var speed = new SpeedByRuleViewModel(context);

        await speed.ReadAsync("project.fwdata", CancellationToken.None);

        Assert.Empty(speed.Rows);
        Assert.Empty(fake.AssessRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SpeedByRuleSaysWhyWhenTheStoredAssessmentSkippedPerObjectTiming(bool storedNumbers)
    {
        var fake = new FakeCommandClient();
        fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
        {
            ObjectTimingDetail = new ObjectTimingDetail(false)
            {
                StatisticsUnavailable = true,
                SkippedEstimatedRows = 1_234_567,
                SkippedRowBudget = storedNumbers ? 250_000 : null,
            },
            ObjectTimingAggregatesAvailable = false,
        });
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.Now, false));
        var speed = new SpeedByRuleViewModel(context);

        await speed.ReadAsync("project.fwdata", CancellationToken.None);

        Assert.Empty(speed.Rows);
        Assert.Equal(storedNumbers
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                "Speed by Rule was not recorded: this Parse would have needed {0} timing rows, more than the {1} limit.",
                1_234_567.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
                250_000.ToString("N0", System.Globalization.CultureInfo.CurrentCulture))
            : "Speed by Rule was not recorded: this Parse would have needed more timing rows than the limit allows.",
            speed.UnavailableText);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task SpeedByRuleExplainsKnownParserWorkAbsence()
    {
        var text = await ReadParserWorkUnavailableText(ParserWorkAbsence.SumsDoNotMatch);

        Assert.Equal("The rule totals do not add up to the measured words' Parser Steps.", text);
    }

    [UiThreadTheory]
    [InlineData("future-reason")]
    [InlineData(null)]
    public async Task SpeedByRuleUsesGenericSentenceForUnknownOrMissingParserWorkAbsence(string? reason)
    {
        var text = await ReadParserWorkUnavailableText(reason);

        Assert.Equal("Recorded Parser Steps are unavailable.", text);
    }

    private static async Task<string> ReadParserWorkUnavailableText(string? reason)
    {
        var fake = new FakeCommandClient();
        fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
        {
            ParserWorkByObjectAbsentReason = reason,
        });
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.Now, false));
        var speed = new SpeedByRuleViewModel(context);

        await speed.ReadAsync("project.fwdata", CancellationToken.None);
        var text = speed.UnavailableText;
        await context.StopProjectWorkAsync();
        return text;
    }

    [UiThreadFact]
    public async Task SpeedByRuleUsesCompleteTotalsWhenPerWordDetailIsIncomplete()
    {
        var fake = new FakeCommandClient();
        fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
        {
            ObjectTimingDetail = new ObjectTimingDetail(false),
            ObjectTimingAggregatesAvailable = true,
            ParserWorkAggregates = [new TimingWorkAggregate("rule", "Recorded rule", "morph_rule", 4, false)],
        });
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.Now, false));
        var speed = new SpeedByRuleViewModel(context);

        await speed.ReadAsync("project.fwdata", CancellationToken.None);

        Assert.Equal("Recorded rule", Assert.Single(speed.Rows).Name);
        Assert.Empty(fake.AssessRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ReadsRecordedWarningsWithoutStartingAGrammarCheck()
    {
        var fake = new FakeCommandClient();
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Recorded", [], [], "A recorded finding");
        fake.StoredGrammarCheckIs(new GrammarCheckResponse([warning], HasBaseline: true));
        var context = WorkspaceContextTests.NewContext(fake);
        context.Diagnostics = true;
        var page = new DiagnosticsPageModel(context);

        await context.OpenProjectAsync("first.fwdata");

        Assert.Equal([warning], page.Warnings.Findings);
        Assert.Same(warning, Assert.Single(page.Warnings.Rows).Warning);
        Assert.NotEmpty(fake.StoredGrammarCheckRequests);
        Assert.Empty(fake.CheckGrammarRequests);
        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.TimingRequests);

        fake.StoredGrammarCheckIs(null);
        await context.OpenProjectAsync("second.fwdata");
        Assert.Empty(page.Warnings.Findings);
        await context.StopProjectWorkAsync();
    }
    [UiThreadFact]
    public async Task FindingsWithoutABaselineAreNotPresentedAsCurrent()
    {
        var fake = new FakeCommandClient();
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Unusable", [], [], "An unavailable finding");
        fake.StoredGrammarCheckIs(new GrammarCheckResponse([warning], HasBaseline: false));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new DiagnosticsPageModel(context);

        await context.OpenProjectAsync("project.fwdata");
        var checks = fake.CheckGrammarRequests.Count;
        context.Diagnostics = true;

        Assert.Empty(page.Warnings.Findings);
        Assert.Equal(checks, fake.CheckGrammarRequests.Count);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ProjectChangesHideStoredFindingsUntilFreshEvidenceIsRead()
    {
        var fake = new FakeCommandClient();
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Recorded", [], [], "A recorded finding");
        fake.StoredGrammarCheckIs(new GrammarCheckResponse([warning], HasBaseline: true));
        var context = WorkspaceContextTests.NewContext(fake);
        context.Diagnostics = true;
        var page = new DiagnosticsPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        Assert.Single(page.Warnings.Findings);

        context.RecordApplied();

        Assert.Empty(page.Warnings.Findings);
        Assert.Empty(fake.CheckGrammarRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task TurningDiagnosticsOffCancelsAReadAndHidesItsLateResult()
    {
        var fake = new FakeCommandClient();
        var entered = new TaskCompletionSource<CancellationToken>();
        var result = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<StoredGrammarCheckResponse>>();
        fake.StoredGrammarCheckHandler = (_, token) =>
        {
            entered.SetResult(token);
            return result.Task;
        };
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new DiagnosticsPageModel(context);
        context.ProjectPath = "project.fwdata";
        context.Diagnostics = true;
        var token = await entered.Task;

        context.Diagnostics = false;
        Assert.True(token.IsCancellationRequested);
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Late", [], [], "A late finding");
        result.SetResult(SIL.Motif.Contract.Commands.CommandOutcome<StoredGrammarCheckResponse>.Success(
            new StoredGrammarCheckResponse(new GrammarCheckResponse([warning], HasBaseline: true))));
        Assert.Empty(page.Warnings.Findings);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task TurningDiagnosticsOffCancelsTimingAndRejectsItsLateRows()
    {
        var fake = new FakeCommandClient();
        var entered = new TaskCompletionSource<CancellationToken>();
        var result = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<TimingResponse>>();
        fake.OnTiming((_, token) =>
        {
            entered.TrySetResult(token);
            return result.Task;
        });
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.UtcNow, false));
        await context.EvidencePublication;
        var page = new DiagnosticsPageModel(context);
        context.Diagnostics = true;
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        context.Diagnostics = false;
        Assert.True(token.IsCancellationRequested);
        result.SetResult(SIL.Motif.Contract.Commands.CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
            {
                ParserWorkAggregates = [new TimingWorkAggregate("late", "Late rule", "morph_rule", 3, false)],
            }));
        await context.StopProjectWorkAsync();
        Assert.Empty(page.SpeedByRule.Rows);
    }

    [UiThreadFact]
    public async Task ANewWarningReadHidesTheOldRowsAndRejectsItsLatePredecessor()
    {
        var fake = new FakeCommandClient();
        var oldRead = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<StoredGrammarCheckResponse>>();
        var newRead = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<StoredGrammarCheckResponse>>();
        var count = 0;
        var stale = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Stale", [], [], "Stale finding");
        fake.StoredGrammarCheckHandler = (_, _) => ++count switch
        {
            1 => Task.FromResult(SIL.Motif.Contract.Commands.CommandOutcome<StoredGrammarCheckResponse>.Success(
                new StoredGrammarCheckResponse(new GrammarCheckResponse([stale], HasBaseline: true)))),
            2 => oldRead.Task,
            _ => newRead.Task,
        };
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        var warnings = new WarningsDiagnosticsViewModel(context);
        await warnings.ReadAsync(context.ProjectPath, CancellationToken.None);
        Assert.Single(warnings.Findings);
        var first = warnings.ReadAsync(context.ProjectPath, CancellationToken.None);
        Assert.Empty(warnings.Findings);
        var second = warnings.ReadAsync(context.ProjectPath, CancellationToken.None);
        var current = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Current", [], [], "Current finding");
        newRead.SetResult(SIL.Motif.Contract.Commands.CommandOutcome<StoredGrammarCheckResponse>.Success(
            new StoredGrammarCheckResponse(new GrammarCheckResponse([current], HasBaseline: true))));
        await second;
        oldRead.SetResult(SIL.Motif.Contract.Commands.CommandOutcome<StoredGrammarCheckResponse>.Success(
            new StoredGrammarCheckResponse(new GrammarCheckResponse([stale], HasBaseline: true))));
        await first;

        Assert.Equal([current], warnings.Findings);
    }

}
