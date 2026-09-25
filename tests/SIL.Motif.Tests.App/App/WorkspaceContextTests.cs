using System.Reflection;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
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
        var request = Assert.Single(fake.TimingRequests);
        Assert.Equal("assessment-1", request.AssessmentId);
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
        var request = Assert.Single(fake.TimingRequests);
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
        Measurements = [new ProducedAssessmentReference("assessment-1", "ObjectTiming", "invocation/one")],
    };

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
