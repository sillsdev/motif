using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TryWordPageTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    [Fact]
    public void RegistryBuildsTheTryAWordPageWithNamedLinksAndADeferredReviewAction()
    {
        var context = NewContext(out _);
        var model = new TryWordPageModel(context);
        var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal("TryAWordPage", view.GetType().Name);
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Open in Texts");
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "AI Handoff for this word");
            var review = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Add expected analysis to Review changes");
            Assert.False(review.IsEnabled);
            Assert.NotNull(ToolTip.GetTip(review));
            Assert.Contains(window.GetLogicalDescendants().OfType<Expander>(), expander =>
                Equals(expander.Header, "Aggregate parser effort by category"));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task ATypedWordCanBeTriedWithoutTextDataAndItsRuleRowsUseOnlyStoredPerRuleTiming()
    {
        var (context, fake) = NewContext();
        context.ProjectPath = ProjectPath;
        context.Assess.ProjectPath = ProjectPath;
        var page = new TryWordPageModel(context);
        var step = new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, [])
        {
            OutcomeStatus = "succeeded",
        };
        fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
            [new TraceCandidate([], true, null, "Built the word", [step])],
            new TraceStep("WordAnalysis", null, null, null, null, [])));
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "selected", "rule", 1, 10, 10, [],
            [new TimingAggregateRow("Plural", 4, 0.4, 2, 1)], []));
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

        context.TryWord("dogs");
        await page.Trace.TryCommand.ExecutionTask!;

        var row = Assert.Single(page.RulesOnBestPath);
        Assert.Equal("Plural", row.Rule);
        Assert.Equal("MorphologicalRule", row.Kind);
        Assert.Equal("succeeded", row.Outcome);
        Assert.Equal("40%", row.Share);
        Assert.Contains("dogs", page.RecentWords);
        var request = Assert.Single(fake.TimingRequests);
        Assert.Equal("rule", request.By);
        Assert.Equal("assessment-1", request.AssessmentId);
        Assert.Equal(["dogs"], request.ExplicitWords);
        Assert.Equal("Aggregate parser effort by category", page.AggregateTimingHeading);
    }

    [Fact]
    public async Task AStoredAssessmentSuppliesRuleTimingForANewTrace()
    {
        var (context, fake) = NewContext();
        context.ProjectPath = ProjectPath;
        context.Assess.ProjectPath = ProjectPath;
        var page = new TryWordPageModel(context);
        fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
            [new TraceCandidate([], true, null, "Built the word", [
                new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, [])])],
            new TraceStep("WordAnalysis", null, null, null, null, [])));
        fake.TimingCompletesWith(new TimingResponse("assessment-1", "selected", "rule", 1, 10, 10, [],
            [new TimingAggregateRow("Plural", 4, 0.4, 2, 1)], []));

        await context.PublishCurrentEvidenceAsync(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow, null,
            EvidenceFreshness.Current, null, null, null, null, StoredAssessment()));
        context.TryWord("dogs");
        await page.Trace.TryCommand.ExecutionTask!;

        Assert.Equal("4.0 ms", Assert.Single(page.RulesOnBestPath).StoredTime);
        Assert.Equal("assessment-1", Assert.Single(fake.TimingRequests).AssessmentId);
    }

    [Fact]
    public async Task RuleTimingAndWordLinksRouteThroughTheWorkspaceContext()
    {
        var (context, fake) = NewContext();
        context.ProjectPath = ProjectPath;
        context.Assess.ProjectPath = ProjectPath;
        var page = new TryWordPageModel(context);
        var timing = new TimingPageModel(context);
        var texts = new TextsPageModel(context);
        var handoff = new AiHandoffPageModel(context);
        fake.TraceWordCompletesWith(new WordTraceResponse("typed-only", false, true, null, 1, null, 1,
            [new TraceCandidate([], false, "failure", "Stopped", [
                new TraceStep("MorphologicalRule", "Plural", "typed-only", null, "failure", [])
                { OutcomeStatus = "failed" }])],
            new TraceStep("WordAnalysis", null, null, null, null, [])));

        context.TryWord("typed-only");
        await page.Trace.TryCommand.ExecutionTask!;
        page.OpenRuleTimingCommand.Execute(Assert.Single(page.RulesOnBestPath));
        Assert.Equal(WorkspacePage.Timing, context.CurrentPage);
        Assert.Equal("Plural", timing.Focus!.Rule);
        Assert.Equal(["typed-only"], timing.Focus.Words);

        page.OpenInTextsCommand.Execute(null);
        Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
        Assert.Equal(TextsTab.Words, texts.Tab);

        page.HandOffCommand.Execute(null);
        Assert.Equal(WorkspacePage.AiHandoff, context.CurrentPage);
        Assert.Equal(["typed-only"], handoff.Handoff.ChosenWords);
    }

    [Fact]
    public async Task CategoryTotalsRemainAggregateAndAreNeverCopiedToARuleStep()
    {
        var (context, fake) = NewContext();
        context.ProjectPath = ProjectPath;
        context.Assess.ProjectPath = ProjectPath;
        var page = new TryWordPageModel(context);
        var effort = new TraceEffort("Morphology", 3, 1, 0, 0, 0, 2, 8);
        fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
            [new TraceCandidate([], true, null, "Built the word", [
                new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, [])])],
            new TraceStep("WordAnalysis", null, null, null, null, []))
        {
            Effort = [effort],
        });
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

        context.TryWord("dogs");
        await page.Trace.TryCommand.ExecutionTask!;

        var row = Assert.Single(page.RulesOnBestPath);
        Assert.Equal("Not recorded", row.StoredTime);
        Assert.Equal("8.0 ms", Assert.Single(page.Trace.Effort).Time);
        Assert.Equal("—", row.Attempts);
    }

    private static (WorkspaceContext Context, FakeCommandClient Fake) NewContext()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        return (new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(), fake,
            new NoFolderPicker(), new NoDragSource()), fake);
    }

    private static WorkspaceContext NewContext(out FakeCommandClient fake)
    {
        (var context, fake) = NewContext();
        return context;
    }

    private static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(new BaselineToken("project-1", "sha256:" + new string('a', 64), "1",
            "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64)), ProjectPath, DateTimeOffset.UtcNow,
            false, false), new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
            Measurements = [new ProducedAssessmentReference("assessment-1", "ObjectTiming", "invocation/one")],
        };

    private static AssessmentRecord StoredAssessment() => new(
        "assessment-1", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
        "whitespace", "1", "{}", Selection.Create("Default", ["dogs"]), null, null,
        "sha256:grammar", null, null, null, "2026-09-24T12:00:00.0000000+00:00", Words: []);

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(Avalonia.Input.PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, Avalonia.Input.DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
