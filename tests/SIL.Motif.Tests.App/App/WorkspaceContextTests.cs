using System.Reflection;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Worker.Store;
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

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public async Task ATimingPageModelBuiltFromAContextAloneTakesTheEvidenceTheContextPublishes()
    {
        var context = NewContext();
        var timing = new TimingPageModel(context);
        Assert.False(timing.Context.HasEvidence);

        await context.PublishProjectOpenedAsync(ProjectPath);
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
        await context.PublishProjectOpenedAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.Same(response, timing.KindTiming);
        Assert.Contains(fake.TimingRequests, request => request.AssessmentId == "assessment-parse" &&
            request.WordSet == "all" && request.By == "kind");
        Assert.Empty(fake.AssessRequests);
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

        await context.PublishProjectOpenedAsync(ProjectPath);

        Assert.Equal(ProjectPath, Assert.Single(fake.OverviewRequests).ProjectPath);
        Assert.Equal(ProjectPath, Assert.Single(fake.CurrentEvidenceRequests));
        Assert.Same(current, context.CurrentEvidence);
        Assert.Same(overview, overviewPage.Overview);
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

        await context.PublishProjectOpenedAsync(ProjectPath);

        var request = Assert.Single(fake.TimingRequests.Where(item => item.By == "kind"));
        Assert.Equal(ProjectPath, request.ProjectPath);
        Assert.Equal("assessment-1", request.AssessmentId);
        Assert.Equal("all", request.WordSet);
        Assert.Equal("kind", request.By);
        Assert.Same(response, timing.StoredTiming);
        Assert.True(timing.ShowStoredTiming);
        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.StatsRequests);
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
        await context.PublishProjectOpenedAsync(ProjectPath);
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

    [Fact]
    public async Task TimingPageShowsTheCommandsKindAndRuleAggregatesUnchanged()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var kindRows = new[] { new TimingAggregateRow("morph_rule", 12, 1, 30, 2) };
        var ruleRows = new[] { new TimingAggregateRow("Verb template", 10, 10d / 12, 26, 2)
            { Kind = "morph_rule" } };
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-1", request.WordSet, request.By, 2, 5, 8, [],
                request.By == "kind" ? kindRows : ruleRows, []))));
        await context.PublishProjectOpenedAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        await timing.SelectWordSetCommand.ExecuteAsync("step-limit");

        Assert.Equal(kindRows, timing.KindTiming!.Aggregates);
        Assert.Equal(ruleRows, timing.RuleTiming!.Aggregates);
        Assert.Equal("morph_rule", timing.SelectedRuleRow!.Kind);
        Assert.Equal("83% of these words' time · 26 attempts · 2 words touched", timing.RuleSummary);
    }

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
                request.By == "rule" ? [new TimingAggregateRow("Verb template", 10, 1, 20, 6)] : [],
                request.Rule is null ? [] : costliest))));
        await context.PublishProjectOpenedAsync(ProjectPath);

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
            Words = [new TimingWordRow("dogs", 5, 2, "Finished"),
                new TimingWordRow("cats", 8, 4, "Step limit")],
        });
        await context.PublishProjectOpenedAsync(ProjectPath);
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
        await context.PublishProjectOpenedAsync(ProjectPath);
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
        await context.PublishProjectOpenedAsync(ProjectPath);
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
        await context.PublishProjectOpenedAsync(ProjectPath);
        context.Assess.Compare.Load([
            new AssessWordRowViewModel(new AssessmentWordResult("dogs", "analysed", false, "Finished", 5, null)),
            new AssessWordRowViewModel(new AssessmentWordResult("cats", "no-parse", false, "Finished", 8, null)),
        ]);
        timing.SelectedTextsList = context.Assess.Compare.Presets.First(preset => preset.Count > 0);
        var expected = context.Assess.Compare.Words.Where(word =>
            word.Family == timing.SelectedTextsList.Family).Select(word => word.Word).ToArray();
        Assert.NotEmpty(expected);
        context.Assess.Compare.SearchText = "no matching words";
        Assert.Empty(context.Assess.Compare.Words);

        await timing.UseTextsListCommand.ExecuteAsync(null);

        Assert.Equal(expected, fake.TimingRequests[0].ExplicitWords);
    }

    [Fact]
    public async Task EmptyTimingSelectionExplainsTheAbsenceOfPercentilesAndCannotHandOffWords()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "step-limit", "kind", 0, null, null,
            [], [], []));
        await context.PublishProjectOpenedAsync(ProjectPath);

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
            Words = [new TimingWordRow("dogs", 5, 2, "Step limit"),
                new TimingWordRow("cats", 8, 4, "Finished")],
        });
        fake.AssessCompletesWith(Assessment());
        await context.PublishProjectOpenedAsync(ProjectPath);
        context.Assess.ProjectPath = ProjectPath;
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        timing.PickedWords = "dogs\ncats";
        await timing.UsePickedWordsCommand.ExecuteAsync(null);
        timing.RerunSeconds = 45;
        timing.RerunSteps = 1000000;
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
        Assert.All(fake.AssessRequests, request =>
        {
            Assert.Equal(45000, request.PerWordLimitMs);
            Assert.Equal(new SIL.Motif.Contract.Assess.StepCap(1000000), request.Selection!.PerWordStepLimit);
        });
    }

    [Fact]
    public async Task TimingCancelStopsTheCurrentWordAndDoesNotStartTheNextOne()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "all", "kind", 2, 5, 8, [], [], [])
        {
            Words = [new TimingWordRow("dogs", 5, 2, "Step limit"),
                new TimingWordRow("cats", 8, 4, "Finished")],
        });
        fake.AssessBlocksUntilCancelled(new Refusal("assessment.cancelled", FailureReason.Cancelled, "Cancelled."));
        await context.PublishProjectOpenedAsync(ProjectPath);
        context.Assess.ProjectPath = ProjectPath;
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        timing.PickedWords = "dogs\ncats";
        await timing.UsePickedWordsCommand.ExecuteAsync(null);

        var running = timing.RerunWordsCommand.ExecuteAsync(null);
        Assert.Equal("dogs", timing.RerunWord);
        timing.CancelRerunCommand.Execute(null);
        await running;

        Assert.Equal("Re-run cancelled.", timing.RerunMessage);
        Assert.Equal(0, timing.RerunCompleted);
        Assert.Single(fake.AssessRequests);
        Assert.Equal(RunState.Cancelled, context.Assess.State);
    }

    [Fact]
    public async Task AnAiHandoffPageModelBuiltFromAContextAloneCoversThePublishedAssessmentAndForgetsItOnClear()
    {
        var context = NewContext();
        var handoff = new AiHandoffPageModel(context);
        Assert.Equal("Write the AI Handoff", handoff.HandoffActionText);

        await context.PublishProjectOpenedAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        Assert.Equal("invocation/one", handoff.Handoff.InvocationId);
        Assert.StartsWith("Covers the Assessment of ", handoff.Handoff.CoverageText);

        context.ClearProject();
        Assert.Null(handoff.Handoff.InvocationId);
        Assert.False(context.HasEvidence);
    }

    [Fact]
    public async Task PendingChangesFollowAProjectSwitchOnlyThroughTheirSlotAndTheReviewBadgeCountsThem()
    {
        var context = NewContext();
        var review = new ReviewPageModel(context);
        await context.PublishProjectOpenedAsync(ProjectPath);
        Assert.Equal(ProjectPath, context.Changes.ProjectPath);
        Assert.Equal(string.Empty, review.Badge);

        context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "dogz", "Approved", string.Empty));
        Assert.Equal("1", review.Badge);

        context.ClearProject();
        Assert.True(context.Changes.HasItems);

        await context.PublishProjectOpenedAsync(OtherProjectPath);
        Assert.Equal(OtherProjectPath, context.Changes.ProjectPath);
        Assert.False(context.Changes.HasItems);
        Assert.Equal(string.Empty, review.Badge);
    }

    [Fact]
    public void OpeningAWordThroughTheContextShowsTheTextsPageOnItsWordsTab()
    {
        var context = NewContext();
        var texts = new TextsPageModel(context);

        context.OpenWord("dogs");

        Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
        Assert.Equal(TextsTab.Words, texts.Tab);
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
            [new TimingAggregateRow("Affix template", 5, 1, 2, 1)], []);
        fake.TimingCompletesWith(result);
        await context.PublishProjectOpenedAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        context.OpenTiming(["dogs"], null);
        await timing.LoadFocusedTimingCommand.ExecutionTask!;

        Assert.Equal(WorkspacePage.Timing, context.CurrentPage);
        var request = Assert.Single(fake.TimingRequests.Where(item => item.By == "kind" &&
            item.ExplicitWords is { Count: > 0 }));
        Assert.Equal("assessment-parse", request.AssessmentId);
        Assert.Equal("kind", request.By);
        Assert.Equal(["dogs"], request.ExplicitWords);
        Assert.Same(result, timing.FocusedTiming);
        Assert.Empty(fake.StatsRequests);
    }

    [Fact]
    public async Task OpeningTimingOnARuleQueriesThatRuleForTheChosenWords()
    {
        var (fake, context) = NewContextWithFake();
        var timing = new TimingPageModel(context);
        var result = new TimingResponse("assessment-1", "all", "rule", 1, 5, 5, [],
            [new TimingAggregateRow("Plural", 5, 1, 2, 1)], [new WordRuleTiming("dogs", 5, 2)]);
        fake.TimingCompletesWith(result);
        await context.PublishProjectOpenedAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        context.OpenTiming(["dogs"], "Plural");
        await timing.LoadFocusedTimingCommand.ExecutionTask!;

        Assert.Equal(["dogs"], timing.Focus!.Words);
        var request = Assert.Single(fake.TimingRequests.Where(item => item.Rule == "Plural" &&
            item.ExplicitWords is { Count: > 0 }));
        Assert.Equal("rule", request.By);
        Assert.Equal("Plural", request.Rule);
        Assert.Equal(["dogs"], request.ExplicitWords);
        Assert.Same(result, timing.FocusedTiming);
        Assert.Empty(fake.StatsRequests);
    }

    [Fact]
    public void ALinkOnOnePageOpensAnotherThroughTheContextWithoutTheWorkspace()
    {
        var context = NewContext();
        var texts = new TextsPageModel(context);
        var timing = new TimingPageModel(context);

        timing.Statistics.OpenTimeLimit!.Invoke();
        Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
        Assert.Equal(TextsTab.Texts, texts.Tab);

        texts.ShowPageCommand.Execute(WorkspacePage.Review);
        Assert.Equal(WorkspacePage.Review, context.CurrentPage);
    }

    [Fact]
    public async Task APageDefinedOutsideTheAppRunsItsOwnQueryAndIsOpenedThroughARegistryEntryAndTheContextAlone()
    {
        var (fake, context) = NewContextWithFake();
        fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, DateTimeOffset.UtcNow)]);
        var entry = PageEntry.Of(WorkspacePage.Warnings, "Elsewhere", "M0 0h1", c => new ElsewherePageModel(c),
            _ => new Avalonia.Controls.Border());
        var model = (ElsewherePageModel)entry.CreateModel(context);

        await context.PublishProjectOpenedAsync(ProjectPath);
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

        await context.PublishProjectOpenedAsync(ProjectPath);
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

        foreach (var holder in new[] { typeof(HandoffWorkspaceViewModel), typeof(WorkspaceContext) })
            Assert.Empty(NamedTypes(holder).Where(pageModels.Contains));

        foreach (var page in pageModels)
        {
            var named = NamedTypes(page).ToList();
            Assert.DoesNotContain(typeof(HandoffWorkspaceViewModel), named);
            Assert.Empty(named.Where(type => type != page && pageModels.Contains(type)));
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

    private static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(Token, ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
        Measurements = [new ProducedAssessmentReference("assessment-1", "ObjectTiming", "invocation/one"),
            new ProducedAssessmentReference("assessment-parse", "ParseTime", "invocation/one")],
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

    private static (FakeCommandClient Fake, WorkspaceContext Context) NewContextWithFake()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        return (fake, new WorkspaceContext(
            selection,
            new AssessViewModel(fake, selection),
            new ChangesViewModel(),
            fake,
            new NoFolderPicker(),
            new NoDragSource()));
    }

    private sealed record ElsewhereRequest(string Note) : PageRequest(WorkspacePage.Warnings);

    private sealed class ElsewherePageModel(WorkspaceContext context) : PageModel(context)
    {
        public string? Received { get; private set; }

        public int? Queried { get; private set; }

        public string? Shown { get; private set; }

        protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
            Queried = (await Context.Commands.ListKnownProjectsAsync(cancellationToken)).Count;

        protected override void OnEvidencePublished(WorkspaceEvidence evidence) =>
            Shown = evidence.Assessment.InvocationId;

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
