using System.Reflection;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that a page is built from a <see cref="WorkspaceContext"/> alone: a page model reacts to the project and
/// evidence the context publishes, pages open one another only through the context's navigation actions, and no
/// page model is known to the workspace, the context, or another page, so adding a page touches only its own files
/// and its registry entry.
/// </summary>
public sealed class WorkspaceContextTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public void ATimingPageModelBuiltFromAContextAloneTakesTheEvidenceTheContextPublishes()
    {
        var context = NewContext();
        var timing = new TimingPageModel(context);
        Assert.False(timing.Context.HasEvidence);

        context.PublishProjectLoaded(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.True(timing.Context.HasEvidence);
        Assert.Equal(ProjectPath, timing.Statistics.ProjectPath);
        Assert.Equal("summary", timing.Statistics.SummaryMarkdown);
        Assert.Equal("assessment-1", timing.Statistics.AssessmentId);
    }

    [Fact]
    public void AnAiHandoffPageModelBuiltFromAContextAloneCoversThePublishedAssessmentAndForgetsItOnClear()
    {
        var context = NewContext();
        var handoff = new AiHandoffPageModel(context);
        Assert.Equal("Write the AI Handoff", handoff.HandoffActionText);

        context.PublishProjectLoaded(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        Assert.Equal("invocation/one", handoff.Handoff.InvocationId);
        Assert.StartsWith("Covers the Assessment of ", handoff.Handoff.CoverageText);

        context.ClearProject();
        Assert.Null(handoff.Handoff.InvocationId);
        Assert.False(context.HasEvidence);
    }

    [Fact]
    public void AReviewPageModelCountsThePendingChangesInItsBadgeAndClearsThemWithTheProject()
    {
        var context = NewContext();
        var review = new ReviewPageModel(context);
        Assert.Equal(string.Empty, review.Badge);

        context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "dogz", "Approved", string.Empty));
        Assert.Equal("1", review.Badge);

        context.ClearProject();
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
    public void OpeningTimingThroughTheContextShowsTheTimingPageFocusedOnTheRule()
    {
        var context = NewContext();
        var timing = new TimingPageModel(context);

        context.OpenTiming(["dogs"], "Plural");

        Assert.Equal(WorkspacePage.Timing, context.CurrentPage);
        Assert.Equal(["dogs"], timing.Focus!.Words);
        Assert.Equal("object", timing.Statistics.SelectedGroup);
        Assert.Equal("Plural", timing.Statistics.FilterText);
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
    public void APageDefinedOutsideTheAppIsBuiltAndOpenedThroughARegistryEntryAndTheContextAlone()
    {
        var context = NewContext();
        var entry = PageEntry.Of(WorkspacePage.Warnings, "Elsewhere", "M0 0h1", c => new ElsewherePageModel(c),
            _ => new Avalonia.Controls.Border());
        var model = (ElsewherePageModel)entry.CreateModel(context);

        context.Open(new ElsewhereRequest("note"));

        Assert.Equal(WorkspacePage.Warnings, context.CurrentPage);
        Assert.Equal("note", model.Received);
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

    internal static WorkspaceContext NewContext()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        return new WorkspaceContext(
            new ProjectViewModel(fake, new NoProjectPicker()),
            new ProjectHistoryViewModel(fake),
            new BaselineViewModel(fake),
            new GrammarViewModel(fake),
            selection,
            words,
            new AssessViewModel(fake, selection),
            new StatisticsViewModel(fake),
            new HandoffViewModel(fake, selection, new NoFolderPicker(), new NoDragSource()),
            new ChangesViewModel(),
            fake);
    }

    private sealed record ElsewhereRequest(string Note) : PageRequest(WorkspacePage.Warnings);

    private sealed class ElsewherePageModel(WorkspaceContext context) : PageModel(context)
    {
        public string? Received { get; private set; }

        protected override void OnRequested(PageRequest request)
        {
            if (request is ElsewhereRequest elsewhere) Received = elsewhere.Note;
        }
    }

    private sealed class NoProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
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
