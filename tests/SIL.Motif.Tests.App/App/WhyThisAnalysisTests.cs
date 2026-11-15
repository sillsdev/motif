using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Diagnostics;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Why This Analysis fold: it does nothing until opened, asks for the trace of its own word once, shows
/// recorded evidence in steps with FieldWorks names, and never shows a trace from another word or Baseline.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class WhyThisAnalysisTests
{
    private const string ProjectPath = "/tmp/motif-why/project.fwdata";

    internal sealed class Rig : IDisposable
    {
        public FakeCommandClient Fake { get; } = new();
        public WorkspaceContext Context { get; }
        public RecordingClipboard Clipboard { get; } = new();
        public RecordingLauncher Launcher { get; } = new();

        public Rig(bool diagnostics = true)
        {
            Fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, DateTimeOffset.UtcNow, false));
            var selection = new SelectionViewModel(Fake);
            Context = new WorkspaceContext(selection, new AssessViewModel(Fake, selection), new ChangesViewModel(Fake), Fake,
                new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(Fake), clipboard: Clipboard,
                uriLauncher: Launcher, tryWordSessions: Fake.TryWordSessionsFake);
            Context.Diagnostics = diagnostics;
        }

        public async Task OpenAsync()
        {
            await Context.OpenProjectAsync(ProjectPath);
            await Context.EvidencePublication;
        }

        public void Dispose() { }
    }

    internal static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(
            Avalonia.Input.PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            Avalonia.Input.DragDropEffects allowedEffects) => Task.FromResult(Avalonia.Input.DragDropEffects.None);
    }

    internal sealed class RecordingClipboard : IClipboard
    {
        public List<string> Copied { get; } = [];

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Copied.Add(text);
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingLauncher : IUriLauncher
    {
        public List<Uri> Launched { get; } = [];

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Launched.Add(uri);
            return Task.FromResult(true);
        }
    }

    internal static WordTraceResponse Trace() => WordTraceQuery.LoadDiagnostic(TestTraceFixture.Read(
        Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))).Value!;

    private static Task Settled(WhyThisAnalysisViewModel fold) => Wait.UntilAsync(
        () => fold.State is not (WhyThisAnalysisState.Loading or WhyThisAnalysisState.Collapsed),
        "The fold did not settle.", TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());

    private static BaselineToken OtherToken() => new(
        "project-2", "sha256:" + new string('c', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('d', 64));

    [Fact]
    public void ANewFoldIsCollapsedAndMakesNoParserCall()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig();
            await rig.OpenAsync();
            var response = Trace();
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            Assert.False(fold.IsExpanded);
            Assert.Equal(WhyThisAnalysisState.Collapsed, fold.State);
            Assert.Empty(rig.Fake.TraceWordRequests);
            Assert.Equal(0, rig.Fake.TryWordSessionsFake.ListenerCount);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ExpandingAsksForTheTraceOfItsOwnWordAndShowsRecordedEvidenceInSteps()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig();
            await rig.OpenAsync();
            var response = Trace() with { ParserWork = 1234 };
            rig.Fake.TraceWordCompletesWith(response);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Settled(fold);

            var request = Assert.Single(rig.Fake.TraceWordRequests);
            Assert.Equal(response.Word, request.Word);
            Assert.Equal(ProjectPath, request.ProjectPath);
            Assert.Equal(WhyThisAnalysisState.Ready, fold.State);
            Assert.Contains("1,234", fold.Subtitle);
            Assert.Contains("steps", fold.Subtitle);
            Assert.DoesNotMatch(@"\bms\b", fold.Subtitle);
            Assert.NotEmpty(fold.TraceRows);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ACappedTraceStaysExplicitAndNeverSaysThereWereNoFailedBranches()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig();
            await rig.OpenAsync();
            var response = Trace() with { Complete = false, StopReason = "limit" };
            rig.Fake.TraceWordCompletesWith(response);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Settled(fold);

            Assert.Equal(WhyThisAnalysisState.Ready, fold.State);
            Assert.True(fold.HasFailedNote);
            Assert.Contains("limit", fold.FailedNote);
            Assert.DoesNotContain(MotifAppStrings.WhyThisAnalysisFailedNoneRecorded, fold.FailedHeading);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ATraceOfAnotherWordIsNeverShown()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig();
            await rig.OpenAsync();
            var response = Trace();
            rig.Fake.TraceWordCompletesWith(response with { Word = "other" });
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Settled(fold);

            Assert.Equal(WhyThisAnalysisState.Mismatched, fold.State);
            Assert.Empty(fold.Analyses);
            Assert.Empty(fold.TraceRows);
            Assert.Null(fold.Trace.Result);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ATraceFromAnotherBaselineIsNeverShown()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig();
            await rig.OpenAsync();
            var response = Trace();
            var capture = new TraceHostCapture(null, null, null, null, null, null, [])
            {
                Baseline = new TraceBaselineSource(OtherToken(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "other"),
            };
            rig.Fake.TraceWordCompletesWith(response with { HostCapture = capture });
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Settled(fold);

            if (fold.RequestedBaseline is null) return;
            Assert.Equal(WhyThisAnalysisState.Mismatched, fold.State);
            Assert.Empty(fold.TraceRows);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void RuleRowsCarryTheNameAndAFieldWorksLinkOnly()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig();
            await rig.OpenAsync();
            var response = Trace();
            rig.Fake.TraceWordCompletesWith(response);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Settled(fold);

            foreach (var rule in fold.Rules)
            {
                Assert.False(string.IsNullOrWhiteSpace(rule.Name));
                if (rule.Link is { } ruleLink) Assert.Equal("silfw", ruleLink.Scheme);
            }
            var link = fold.TraceRows.Select(row => row.Link).FirstOrDefault(uri => uri is not null)
                ?? fold.Analyses.SelectMany(row => row.Parts).Select(part => part.Link).FirstOrDefault(uri => uri is not null);
            if (link is null) return;
            await fold.OpenInFieldWorksCommand.ExecuteAsync(link);
            Assert.Equal([link], rig.Launcher.Launched);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void CopyTraceSendsTheRecordedDiagnosticToTheClipboardOnlyOnRequest()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig();
            await rig.OpenAsync();
            var response = Trace();
            rig.Fake.TraceWordCompletesWith(response);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Settled(fold);
            Assert.Empty(rig.Clipboard.Copied);
            await fold.CopyTraceCommand.ExecuteAsync(null);
            Assert.Single(rig.Clipboard.Copied);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ExpandingTheRenderedFoldAsksForTheTraceOnceAndFoldingCancelsIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig();
            await rig.OpenAsync();
            var response = Trace();
            rig.Fake.TraceWordCompletesWith(response);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            var panel = new WhyThisAnalysisPanel(fold);
            var window = new MotifTestWindow { Content = panel, Width = 600, Height = 500 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var expander = panel.GetVisualDescendants().OfType<Expander>().Single();
                Assert.False(expander.IsExpanded);
                Assert.Empty(rig.Fake.TraceWordRequests);

                expander.IsExpanded = true;
                await Settled(fold);
                Dispatcher.UIThread.RunJobs();
                Assert.Single(rig.Fake.TraceWordRequests);
                Assert.True(fold.IsExpanded);

                expander.IsExpanded = false;
                Dispatcher.UIThread.RunJobs();
                Assert.False(fold.IsExpanded);
                Assert.Equal(WhyThisAnalysisState.Collapsed, fold.State);
                Assert.Empty(fold.TraceRows);
                Assert.Equal(0, rig.Fake.TryWordSessionsFake.ListenerCount);
                Assert.Single(rig.Fake.TraceWordRequests);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void DiagnosticsOffBuildsNoFoldAndMakesNoParserCall()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new Rig(diagnostics: false);
            await rig.OpenAsync();
            var page = new TextsPageModel(rig.Context);
            Assert.Null(page.WhyThisAnalysis.Fold);
            Assert.Empty(rig.Fake.TraceWordRequests);
            Assert.DoesNotContain(rig.Fake.TryWordSessionsFake.Calls, call => call.StartsWith("prewarm", StringComparison.Ordinal));
        }, TimeSpan.FromSeconds(60));
    }
}
