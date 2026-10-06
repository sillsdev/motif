using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Tests.TestFixtures;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class WalkthroughReplayTests(PristineProjectFixture pristine)
{
    [Fact]
    public void WalkthroughFontCollectionContainsAndikaTextFaces()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            WalkthroughFonts.Register();
            var collection = WalkthroughFonts.CreateCollection();
            foreach (var (weight, style) in new[]
                     {
                         (FontWeight.Normal, FontStyle.Normal),
                         (FontWeight.Bold, FontStyle.Normal),
                         (FontWeight.SemiBold, FontStyle.Normal),
                         (FontWeight.Normal, FontStyle.Italic),
                     })
            {
                var found = collection.TryGetGlyphTypeface("Andika", style, weight,
                    FontStretch.Normal, out var glyphTypeface);
                Assert.True(found, $"Andika {style} {weight} was not available in {collection.Key}.");
                Assert.StartsWith("Andika", glyphTypeface!.FamilyName, StringComparison.OrdinalIgnoreCase);
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void LookingUpAProjectMenuItemDoesNotOpenItsFlyout()
    {
        var managedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.Walkthrough", "lookup",
            Guid.NewGuid().ToString("N"));
        using var project = new WalkthroughProject(pristine, managedRoot);

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            WalkthroughFonts.Register();
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            walkthrough.Show();
            var selectProject = walkthrough.FindByAutomationId(AutomationIds.SelectNewProject);

            var flyout = (Flyout)typeof(WalkthroughWindow).GetProperty("ProjectMenuFlyout",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(walkthrough)!;
            Assert.False(flyout.IsOpen);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReplayTypesAWordAndShowsTheRecordedTraceResult()
    {
        var managedRoot = WalkthroughTestFiles.EngineRoot("try-word-replay");
        WalkthroughTestFiles.DeleteDirectory(managedRoot);
        using var project = new WalkthroughProject(pristine, managedRoot);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var clock = new FixedClock(WalkthroughReplayTestSupport.CaptureTime, TimeZoneInfo.Utc);
        var projectContext = new WalkthroughProjectContext(
            project.ManagedRoot, project.FwDataPath, project.TextId, null);
        await WalkthroughFixtureSeeder.SeedAsync("try-word-ready", projectContext, clock, parserPath);
        FakeParser.BehaveBesideExecutable(parserPath,
            FakeParser.TraceBehavior("motifa", "motifa-trace", "SeededRule"));
        var script = new WalkthroughScript("try-word-replay",
        [
            new WalkthroughStep("open-project-menu", WalkthroughStepKind.Click,
                AutomationId: AutomationIds.ProjectMenu),
            new WalkthroughStep("select-project", WalkthroughStepKind.Click,
                AutomationId: AutomationIds.SelectNewProject),
            new WalkthroughStep("wait-for-project", WalkthroughStepKind.WaitFor,
                AutomationId: AutomationIds.OverviewSelectionWordCount, Condition: "text", ExpectedText: "2",
                TimeoutMs: 30_000),
            new WalkthroughStep("open-try-word", WalkthroughStepKind.Click,
                AutomationId: AutomationIds.ForPage(WorkspacePage.TryAWord)),
            new WalkthroughStep("type-word", WalkthroughStepKind.Type,
                AutomationId: AutomationIds.TryWordInput, Text: "motifa"),
            new WalkthroughStep("wait-for-run", WalkthroughStepKind.WaitFor,
                AutomationId: AutomationIds.TryWordRun, Condition: "enabled", TimeoutMs: 30_000),
            new WalkthroughStep("run-trace", WalkthroughStepKind.Click,
                AutomationId: AutomationIds.TryWordRun),
            new WalkthroughStep("show-result", WalkthroughStepKind.WaitFor,
                AutomationId: AutomationIds.TryWordResult, Condition: "visible", TimeoutMs: 30_000),
        ]);
        var help = new WalkthroughHelpContent("en", "Try a word", "",
            new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>());
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            WalkthroughFonts.Register();
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath, timeProvider: clock);
            walkthrough.Show();
            WalkthroughReplay.Run(walkthrough, script, help, clock, [], [], deadline,
                WalkthroughReplayTestSupport.FindRepositoryRoot());

            Assert.Equal("motifa", walkthrough.TextByAutomationId(AutomationIds.TryWordInput));
            Assert.Equal("motifa", walkthrough.TextByAutomationId(AutomationIds.TryWordResult));
            var trace = walkthrough.Workspace.PageModel<TryWordPageModel>().Trace;
            var traceResponse = Assert.IsType<SIL.Motif.Contract.Responses.WordTraceResponse>(trace.Result);
            Assert.Equal("Parsed", trace.AnswerText);
            var displayedAnalysis = walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>()
                .SingleOrDefault(control => control.Text == "Analysis 1");
            Assert.NotNull(displayedAnalysis);
            Assert.True(displayedAnalysis.IsEffectivelyVisible);
            var displayedGloss = walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>()
                .SingleOrDefault(control => control.Text == "seeded gloss");
            Assert.NotNull(displayedGloss);
            Assert.True(displayedGloss.IsEffectivelyVisible);
            Assert.False(trace.IsExpert);
            var page = walkthrough.Window.GetLogicalDescendants().OfType<TryWordPanel>().Single();
            var diagnosticHost = page.FindControl<ContentControl>("RichDiagnosticHost")!;
            Assert.True(diagnosticHost.IsVisible);
            Assert.False(diagnosticHost.GetLogicalDescendants().OfType<Expander>().Single(expander =>
                Avalonia.Automation.AutomationProperties.GetName(expander) == "Full derivation tree").IsEffectivelyVisible);
            Assert.Contains("motifa-trace", traceResponse.DiagnosticJson);
            Assert.Equal(1, FakeParser.Invocations(parserPath).Count(command => command == "parse"));
            Assert.Contains("parse", FakeParser.Invocations(parserPath));
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class AuthoredWalkthroughIdAttribute(string scriptId) : Attribute
{
    public string ScriptId { get; } = scriptId;
}

[Collection(LcmCacheTestCollection.Name)]
[AuthoredWalkthroughId("annotate-control")]
public sealed class AnnotateControlWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
[AuthoredWalkthroughId("explained-word-card")]
public sealed class ExplainedWordCardWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[AuthoredWalkthroughId("first-run-setup-parse")]
public sealed class FirstRunSetupParseWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[AuthoredWalkthroughId("handoff-cancel-retry")]
public sealed class HandoffCancelRetryWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[AuthoredWalkthroughId("open-project-overview")]
public sealed class OpenProjectOverviewWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[AuthoredWalkthroughId("review-apply-refresh-parse")]
public sealed class ReviewApplyRefreshParseWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
[AuthoredWalkthroughId("right-to-left-text")]
public sealed class RightToLeftTextWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[AuthoredWalkthroughId("try-word-typing")]
public sealed class TryWordTypingWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[AuthoredWalkthroughId("no-longer-fits")]
public sealed class NoLongerFitsWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

[Collection(LcmCacheTestCollection.Name)]
[AuthoredWalkthroughId("warnings-filter")]
public sealed class WarningsFilterWalkthroughReplayTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task ReplaysAuthoredWalkthrough() =>
        WalkthroughReplayTestRunner.RunAsync(GetType(), pristine, output);
}

internal static class WalkthroughReplayTestSupport
{
    internal static readonly DateTimeOffset CaptureTime = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
    }
}

internal static class WalkthroughReplayTestRunner
{
    public static async Task RunAsync(
        Type wrapperType, PristineProjectFixture pristine, ITestOutputHelper output)
    {
        var registration = wrapperType.GetCustomAttribute<AuthoredWalkthroughIdAttribute>(inherit: false)
            ?? throw new InvalidOperationException($"{wrapperType.FullName} has no authored walkthrough ID.");
        var root = WalkthroughReplayTestSupport.FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "walkthroughs", $"{registration.ScriptId}.walkthrough.json");
        var script = WalkthroughScriptLoader.Load(scriptPath);
        var help = WalkthroughHelpContent.Load(root, script.Id, "en");
        var managedRoot = WalkthroughTestFiles.EngineRoot(script.Id);
        WalkthroughTestFiles.DeleteDirectory(managedRoot);
        var clock = new FixedClock(WalkthroughReplayTestSupport.CaptureTime, TimeZoneInfo.Utc);
        var (projectLifetime, project) = await CreateProjectAsync(pristine, script.Fixture, managedRoot);
        using (projectLifetime)
        {
            var parserPath = project.ParserPath ??
                FakeParser.CopyRecordingInvocations(Path.Combine(project.ManagedRoot, "walkthrough-parser"));
            var preparation = await WalkthroughFixtureSeeder.SeedAsync(script.Fixture, project, clock, parserPath);
            var captures = new List<WalkthroughCapture>();
            var clipSegments = new List<WalkthroughClipSegment>();
            var deadline = Stopwatch.GetTimestamp() + (long)(WalkthroughReplay.DeadlineBudget(script).TotalSeconds * Stopwatch.Frequency);

            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                var previousCulture = CultureInfo.CurrentCulture;
                var previousUiCulture = CultureInfo.CurrentUICulture;
                var previousTheme = Application.Current!.RequestedThemeVariant;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                    if (script.Fixture == "explained-word-card")
                        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                    WalkthroughFonts.Register();
                    using var walkthrough = new WalkthroughWindow(
                        project.ManagedRoot, project.FwDataPath, Path.Combine(project.ManagedRoot, "handoff-output"),
                        parserPath: parserPath, timeProvider: clock);
                    if (script.Id == "right-to-left-text")
                        walkthrough.Workspace.Context.WritingSystemTextStyles.SetFallbackFamilies(
                            ["fonts:MotifWalkthrough#DejaVu Sans"]);
                    walkthrough.Window.Width = WalkthroughArtifacts.Width;
                    walkthrough.Window.Height = script.Id == "explained-word-card"
                        ? 800
                        : WalkthroughArtifacts.Height;
                    walkthrough.Window.SetValue(TextElement.FontFamilyProperty, new FontFamily("fonts:MotifWalkthrough#Andika"));
                    walkthrough.Show();
                    Assert.Equal(1d, walkthrough.Window.RenderScaling);
                    if (script.Id == "explained-word-card")
                    {
                        WalkthroughReplay.SuppressTooltipsForCapture(walkthrough.Window);
                        using var frame = SKBitmap.Decode(WalkthroughArtifacts.CaptureFrame(walkthrough.Window, 2.5));
                        Assert.NotNull(frame);
                        Assert.Equal((3200, 2000), (frame!.Width, frame.Height));
                    }
                    WalkthroughReplay.Run(walkthrough, script, help, clock, captures, clipSegments,
                        deadline, root, preparation);
                    WalkthroughReplay.AssertFixtureOutcome(walkthrough, script, parserPath, deadline);
                    if (script.Id == "explained-word-card")
                    {
                        AssertExplainedWordCard(walkthrough);
                        LayoutAssertions.AssertMorphemeGlyphsFitAnalysisRows(walkthrough.Window);
                    }
                    if (script.Id == "right-to-left-text") AssertRightToLeftText(walkthrough);
                    return Task.CompletedTask;
                }
                finally
                {
                    Application.Current.RequestedThemeVariant = previousTheme;
                    CultureInfo.CurrentCulture = previousCulture;
                    CultureInfo.CurrentUICulture = previousUiCulture;
                }
            }, WalkthroughSteps.Remaining(deadline));

            if (script.Id == "explained-word-card")
                Assert.Equal(["describe", "batch"], FakeParser.Invocations(parserPath));
            Assert.NotEmpty(captures);
            Assert.NotEmpty(clipSegments);
            Assert.Equal(0, clipSegments[0].StartMs);
            for (var index = 1; index < clipSegments.Count; index++)
                Assert.Equal(clipSegments[index - 1].StartMs + clipSegments[index - 1].DurationMs,
                    clipSegments[index].StartMs);
            Assert.Equal(script.Steps.Count(step => step.Kind == WalkthroughStepKind.Click),
                clipSegments.Count(segment => segment.Kind == WalkthroughClipSegmentKind.Click));
            Assert.Equal(script.Steps.Where(step => step.Kind == WalkthroughStepKind.Click)
                    .Select(step => step.AutomationId),
                clipSegments.Where(segment => segment.Kind == WalkthroughClipSegmentKind.Click)
                    .Select(segment => segment.ClickTarget));
            Assert.Equal(script.Steps.Count(step => step.Kind == WalkthroughStepKind.Hold),
                clipSegments.Count(segment => segment.Kind == WalkthroughClipSegmentKind.Hold));
            Assert.Equal(script.Steps.Count(step => step.Kind == WalkthroughStepKind.Capture), captures.Count);
            Assert.Equal(captures.Count,
                clipSegments.Count(segment => segment.Kind == WalkthroughClipSegmentKind.Capture));
            WalkthroughArtifacts.Write(root, script, help, captures, clipSegments, output.WriteLine,
                strictBaselineComparison: script.Id == "explained-word-card" && OperatingSystem.IsWindows());
        }
    }

    private static async Task<(IDisposable Lifetime, WalkthroughProjectContext Project)> CreateProjectAsync(
        PristineProjectFixture pristine, string fixture, string managedRoot)
    {
        if (fixture == "explained-word-card")
        {
            var project = await ExplainedWordCardWalkthroughProject.CreateAsync(managedRoot);
            return (project, new WalkthroughProjectContext(
                project.ManagedRoot, project.FwDataPath, project.TextId, project.ParserPath));
        }

        if (fixture == "right-to-left-text")
        {
            var rightToLeft = new WalkthroughProject(pristine, managedRoot,
                new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc), RightToLeftWalkthroughFixture.Configure);
            return (rightToLeft, new WalkthroughProjectContext(
                rightToLeft.ManagedRoot, rightToLeft.FwDataPath, rightToLeft.TextId, null));
        }

        var ordinary = new WalkthroughProject(pristine, managedRoot,
            new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc));
        return (ordinary, new WalkthroughProjectContext(
            ordinary.ManagedRoot, ordinary.FwDataPath, ordinary.TextId, null));
    }

    private static void AssertRightToLeftText(WalkthroughWindow walkthrough)
    {
        var page = walkthrough.Workspace.PageModel<TextsPageModel>();
        Assert.Contains(page.ResultsInText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens),
            token => token.Form == WritingSystemTestData.Form &&
                token.FormWritingSystem == SeededProject.RightToLeftTag);
        var panel = Assert.Single(walkthrough.Window.GetLogicalDescendants().OfType<ResultsInTextPanel>());
        var word = Assert.Single(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
            text.IsEffectivelyVisible && text.Text == WritingSystemTestData.Form &&
            text.Classes.Contains("stripWord"));
        Assert.Equal(walkthrough.Workspace.Context.WritingSystemTextStyles
            .Resolve(SeededProject.RightToLeftTag, "Normal").FontSize, word.FontSize);
        Assert.Equal(FlowDirection.RightToLeft, word.FlowDirection);
        Assert.DoesNotContain("Andika", word.FontFamily!.Name, StringComparison.OrdinalIgnoreCase);
        var lineBody = Assert.Single(word.GetVisualAncestors()
            .OfType<SIL.Motif.App.Controls.RunningTextPanel>(), runningText =>
            runningText.Classes.Contains("resultsLineBody"));
        Assert.Equal(FlowDirection.RightToLeft, lineBody.TextDirection);
        SIL.Motif.Tests.App.LayoutAssertions.AssertWalkthroughCurrent(walkthrough.Window);
    }

    private static void AssertExplainedWordCard(WalkthroughWindow walkthrough)
    {
        var tokens = walkthrough.Workspace.PageModel<TextsPageModel>().ResultsInText.Texts
            .SelectMany(text => text.Lines)
            .SelectMany(line => line.Tokens)
            .Where(token => token.IsWord)
            .ToArray();
        var forms = tokens.Select(token => token.Form.Normalize(NormalizationForm.FormC)).ToArray();
        Assert.Equal(["geldi", "evler", "kediye", "adamlarında", "günler", "okullarında"], forms);
        var byForm = tokens.ToDictionary(token => token.Form.Normalize(NormalizationForm.FormC),
            StringComparer.Ordinal);
        var bundledLanguageFont = walkthrough.Workspace.Context.WritingSystemTextStyles
            .Resolve(byForm["geldi"].FormWritingSystem, "Normal");
        Assert.Equal(WalkthroughFonts.DejaVuSansFamily, bundledLanguageFont.RequestedFontFamily);
        Assert.True(bundledLanguageFont.RequestedFontInstalled);
        foreach (var form in new[] { "geldi", "evler", "kediye", "adamlarında", "okullarında" })
        {
            Assert.NotEmpty(byForm[form].Readings);
            Assert.All(byForm[form].Readings, reading => Assert.False(
                reading.Text.Contains("?", StringComparison.Ordinal),
                $"{form} reading {reading.Index}: {string.Join("-", reading.Morphs.Select(morph =>
                    $"{morph.Form}/{morph.Gloss}/{morph.Category}"))}"));
        }

        Assert.Equal(AnalysisMarkingClass.Same, byForm["geldi"].Marking.PanGlossClass);
        Assert.Equal(ReadingGrade.Approved, Assert.Single(byForm["geldi"].Marking.FieldWorksAnalyses).Opinion);
        Assert.True(byForm["geldi"].Marking.IsUnread);

        Assert.Equal(AnalysisMarkingClass.Same, byForm["evler"].Marking.PanGlossClass);
        Assert.Equal(ReadingGrade.Candidate, Assert.Single(byForm["evler"].Marking.FieldWorksAnalyses).Opinion);
        Assert.Contains(byForm["evler"].Marking.StagedTransitions,
            transition => transition.Now == "Unknown" && transition.AfterApply == "Approved");

        Assert.Equal(AnalysisMarkingClass.Conflict, byForm["kediye"].Marking.PanGlossClass);
        Assert.Equal(ReadingGrade.Disapproved,
            Assert.Single(byForm["kediye"].Marking.FieldWorksAnalyses).Opinion);

        Assert.Equal(AnalysisMarkingClass.Different, byForm["adamlarında"].Marking.PanGlossClass);
        Assert.Empty(byForm["adamlarında"].Marking.FieldWorksAnalyses);
        Assert.Contains(byForm["adamlarında"].Marking.PanGlossReadings, reading => reading.IsParserOnly);

        Assert.Equal(AnalysisMarkingClass.None, byForm["günler"].Marking.PanGlossClass);
        Assert.Empty(byForm["günler"].Marking.FieldWorksAnalyses);
        Assert.Equal(OccurrenceVerdict.NoParse, byForm["günler"].Verdict);
        Assert.Contains("No parse", walkthrough.VisibleTextUnderAutomationId(byForm["günler"].PanGlossAutomationId));
        Assert.Contains(walkthrough.FindByAutomationId(byForm["günler"].PanGlossAutomationId)
            .GetLogicalDescendants().OfType<MarkGlyph>(), glyph => glyph.Mark == Mark.NoParse);
        Assert.Contains("Nothing in FieldWorks",
            walkthrough.VisibleTextUnderAutomationId(byForm["günler"].FieldWorksAutomationId));

        Assert.Equal(AnalysisMarkingClass.Capped, byForm["okullarında"].Marking.PanGlossClass);
        Assert.Equal(ReadingGrade.Approved,
            Assert.Single(byForm["okullarında"].Marking.FieldWorksAnalyses).Opinion);
    }
}

internal static class WalkthroughFonts
{
    public const string DejaVuSansFamily = "fonts:MotifWalkthrough#DejaVu Sans";
    private const string CollectionKey = "fonts:MotifWalkthrough";
    private const string Assets = "avares://SIL.Motif.Tests.App/Assets/Fonts";
    private static bool _registered;

    public static EmbeddedFontCollection CreateCollection() =>
        new(new Uri(CollectionKey), new Uri(Assets));

    public static void Register()
    {
        if (_registered) return;
        FontManager.Current.AddFontCollection(CreateCollection());
        _registered = true;
    }
}

internal static class WalkthroughReplay
{
    private const int ClickDurationMs = 250;

    internal static TimeSpan DeadlineBudget(WalkthroughScript script)
    {
        var budget = TimeSpan.FromSeconds(15);
        foreach (var step in script.Steps)
        {
            if (step.TimeoutMs is { } timeout) budget += TimeSpan.FromMilliseconds(timeout);
            if (step.DurationMs is { } duration) budget += TimeSpan.FromMilliseconds(duration);
            if (step.Kind == WalkthroughStepKind.Click) budget += TimeSpan.FromMilliseconds(ClickDurationMs);
        }
        return budget;
    }

    public static void Run(
        WalkthroughWindow window, WalkthroughScript script, WalkthroughHelpContent help, FixedClock clock,
        List<WalkthroughCapture> captures, List<WalkthroughClipSegment> clipSegments, long deadline,
        string repositoryRoot,
        WalkthroughFixturePreparation? preparation = null)
    {
        var highlighted = new HashSet<string>(StringComparer.Ordinal);
        var elapsedMs = 0;
        string? lastTarget = null;
        foreach (var step in script.Steps)
        {
            switch (step.Kind)
            {
                case WalkthroughStepKind.Click:
                    var clickId = step.AutomationId!;
                    var clickBounds = window.BoundsByAutomationId(clickId);
                    window.ClickAutomationId(clickId);
                    if (preparation?.ReadyMarkers.TryGetValue(clickId, out var readyMarker) == true)
                        window.WaitUntil(() => File.Exists(readyMarker), WalkthroughSteps.Remaining(deadline),
                            $"the prepared parser phase for '{step.Id}' did not start");
                    SuppressTooltipsForCapture(window.Window);
                    clipSegments.Add(new WalkthroughClipSegment(elapsedMs, ClickDurationMs,
                        WalkthroughArtifacts.CaptureFrame(window.Window), clickBounds,
                        WalkthroughClipSegmentKind.Click, clickId));
                    elapsedMs += ClickDurationMs;
                    lastTarget = clickId;
                    break;
                case WalkthroughStepKind.Type:
                    window.TypeAutomationId(step.AutomationId!, step.Text!);
                    break;
                case WalkthroughStepKind.WaitFor:
                    var stepTimeout = TimeSpan.FromMilliseconds(step.TimeoutMs!.Value);
                    var remaining = WalkthroughSteps.Remaining(deadline);
                    window.WaitUntil(() => Satisfies(window, step),
                        remaining < stepTimeout ? remaining : stepTimeout, $"step '{step.Id}' timed out",
                        () => window.DescribeAutomationId(step.AutomationId!));
                    break;
                case WalkthroughStepKind.Highlight:
                    window.ScrollIntoView(step.AutomationId!);
                    _ = window.BoundsByAutomationId(step.AutomationId!);
                    highlighted.Add(step.AutomationId!);
                    lastTarget = step.AutomationId;
                    break;
                case WalkthroughStepKind.Hold:
                    Avalonia.Rect? holdBounds = lastTarget is null ? null : window.BoundsByAutomationId(lastTarget);
                    SuppressTooltipsForCapture(window.Window);
                    clipSegments.Add(new WalkthroughClipSegment(elapsedMs, step.DurationMs!.Value,
                        WalkthroughArtifacts.CaptureFrame(window.Window), holdBounds,
                        WalkthroughClipSegmentKind.Hold, null));
                    clock.Advance(TimeSpan.FromMilliseconds(step.DurationMs!.Value));
                    elapsedMs += step.DurationMs.Value;
                    break;
                case WalkthroughStepKind.Capture:
                    if (script.Id == "explained-word-card")
                    {
                        var wordId = step.Callouts!.Single(callout =>
                            callout.AutomationId.EndsWith("-word", StringComparison.Ordinal)).AutomationId;
                        window.ScrollIntoView(wordId[..^5] + "-strip");
                    }
                    if (script.Id == "explained-word-card")
                    {
                        var calloutIds = step.Callouts!.Select(callout => callout.AutomationId).ToArray();
                        window.WaitUntil(() => CardIsQuiet(window),
                            TimeSpan.FromSeconds(10), $"capture '{step.Id}' evidence publication or word staging did not finish");
                        window.WaitUntil(() => calloutIds.All(window.HasVisibleTextOrMark),
                            TimeSpan.FromSeconds(10), $"capture '{step.Id}' callout target content did not appear",
                            () => string.Join(", ", calloutIds.Where(id => !window.HasVisibleTextOrMark(id))));
                        // Under load a target can still be re-measuring after its text appears, which widens the crop.
                        string? previousBounds = null;
                        var settledPasses = 0;
                        // A settle that times out must say whether a callout kept moving or the passes were starved.
                        var boundsHistory = new List<string>();
                        var passes = 0;
                        var settleClock = Stopwatch.StartNew();
                        var lastPassMs = 0L;
                        var longestGapMs = 0L;
                        window.WaitUntil(() =>
                        {
                            passes++;
                            longestGapMs = Math.Max(longestGapMs, settleClock.ElapsedMilliseconds - lastPassMs);
                            lastPassMs = settleClock.ElapsedMilliseconds;
                            window.Window.UpdateLayout();
                            var currentBounds = string.Join(";",
                                calloutIds.Select(id => $"{id}={window.BoundsByAutomationId(id)}"));
                            if (currentBounds != previousBounds)
                                boundsHistory.Add($"@{settleClock.ElapsedMilliseconds}ms {currentBounds}");
                            settledPasses = currentBounds == previousBounds ? settledPasses + 1 : 0;
                            previousBounds = currentBounds;
                            return settledPasses >= 2;
                        }, TimeSpan.FromSeconds(10), $"capture '{step.Id}' callout bounds did not settle",
                            () => $"{passes} passes, longest gap {longestGapMs} ms, " +
                                $"{boundsHistory.Count} distinct readings, last: " +
                                string.Join(" | ", boundsHistory.TakeLast(4)));
                    }
                    if (script.Id == "review-apply-refresh-parse")
                        Assert.Null(window.Workspace.Context.Changes.ShownRefusal);
                    var callouts = step.Callouts!.Select(callout =>
                    {
                        Assert.Contains(callout.AutomationId, highlighted);
                        var bounds = WalkthroughArtifacts.PadSmallHighlightTarget(
                            window.BoundsByAutomationId(callout.AutomationId));
                        return new WalkthroughCaptureCallout(
                            callout.AutomationId, help.CalloutCaption(step.Id, callout.AutomationId),
                            bounds);
                    }).ToArray();
                    var stagedTooltip = OpenStagedFitTooltipForCaptureRegression(window, script, step);
                    SuppressTooltipsForCapture(window.Window);
                    if (stagedTooltip is not null)
                        Assert.False(ToolTip.GetIsOpen(stagedTooltip),
                            "The staged fit tooltip remained open as the unknown-staged frame was captured.");
                    var capture = WalkthroughArtifacts.Capture(step.Id, elapsedMs, step.DurationMs!.Value,
                        window.Window, callouts, step.CropPadding, step.Scale);
                    if (script.Id == "explained-word-card" && step.Id == "approved-agrees")
                    {
                        var fixId = callouts.Single(callout =>
                            callout.AutomationId.EndsWith("-fix", StringComparison.Ordinal)).AutomationId;
                        var fix = (Button)window.FindByAutomationId(fixId);
                        var fallbackWidths = new List<int>();
                        var chevron = Assert.Single(fix.GetLogicalDescendants().OfType<PathIcon>());
                        foreach (var fallbackFamily in new[] { "serif", "monospace" })
                        {
                            chevron.SetValue(TextElement.FontFamilyProperty, new FontFamily(fallbackFamily));
                            window.Window.UpdateLayout();
                            fallbackWidths.Add(CaptureWidth());
                        }
                        chevron.ClearValue(TextElement.FontFamilyProperty);
                        window.Window.UpdateLayout();
                        Assert.Equal(fallbackWidths[0], fallbackWidths[1]);
                        using var productionBitmap = SKBitmap.Decode(capture.Png);
                        Assert.Equal(fallbackWidths[0], productionBitmap!.Width);

                        int CaptureWidth()
                        {
                            var changedCallouts = callouts.Select(callout => callout.AutomationId == fixId
                                ? callout with
                                {
                                    Bounds = WalkthroughArtifacts.PadSmallHighlightTarget(
                                        window.BoundsByAutomationId(fixId)),
                                }
                                : callout).ToArray();
                            var changedCapture = WalkthroughArtifacts.Capture(step.Id, elapsedMs,
                                step.DurationMs!.Value, window.Window, changedCallouts, step.CropPadding, step.Scale);
                            using var changedBitmap = SKBitmap.Decode(changedCapture.Png);
                            return changedBitmap!.Width;
                        }
                    }
                    if (script.Id == "explained-word-card")
                    {
                        WalkthroughArtifacts.ValidateCaptionColumnLayout(repositoryRoot, capture);
                        Assert.True(capture.Scale >= 2);
                        Assert.NotNull(capture.SourceFrameCropBounds);
                        var captureBounds = new Size(capture.SourceFrameCropBounds.Value.Width,
                            capture.SourceFrameCropBounds.Value.Height);
                        Assert.All(capture.Callouts, callout => Assert.True(
                            WalkthroughWindow.FitsViewport(callout.Bounds, captureBounds),
                            $"Capture '{step.Id}' crops callout target '{callout.AutomationId}'."));
                        var selectedWordId = callouts.Single(callout =>
                            callout.AutomationId.EndsWith("-word", StringComparison.Ordinal)).AutomationId;
                        var selectedWordStrip = selectedWordId[..^5] + "-strip";
                        foreach (var (automationId, bounds) in window.VisibleWordStripBounds())
                        {
                            if (automationId == selectedWordStrip) continue;
                            var scaledBounds = new Rect(bounds.X * capture.Scale, bounds.Y * capture.Scale,
                                bounds.Width * capture.Scale, bounds.Height * capture.Scale);
                            Assert.False(capture.SourceFrameCropBounds!.Value.Intersects(scaledBounds),
                                $"Capture '{step.Id}' includes neighboring word strip '{automationId}'.");
                        }
                    }
                    captures.Add(capture);
                    clipSegments.Add(new WalkthroughClipSegment(elapsedMs, step.DurationMs!.Value,
                        WalkthroughArtifacts.CaptureFrame(window.Window), callouts.FirstOrDefault()?.Bounds,
                        WalkthroughClipSegmentKind.Capture, null));
                    clock.Advance(TimeSpan.FromMilliseconds(step.DurationMs.Value));
                    elapsedMs += step.DurationMs.Value;
                    break;
                default:
                    throw new InvalidDataException($"Walkthrough step '{step.Id}' has unsupported kind.");
            }
        }
    }

    /// <summary>Whether nothing that can still move the Analyze texts word strips is running.</summary>
    /// <remarks>
    /// Evidence still publishing can hold the card in a stable but earlier layout under load. So can a word's
    /// staging: its Staged note shows as soon as the change is stored, but the word is marked Read only after
    /// that, and its Unread dot then leaves the strip and moves the word; pinned by
    /// `TheStagedCardIsNotQuietUntilApprovingTheWordHasMarkedItRead`.
    /// </remarks>
    internal static bool CardIsQuiet(WalkthroughWindow window) =>
        window.Workspace.Context.EvidencePublication.IsCompleted &&
        !window.Workspace.Assess.IsActive && !window.Workspace.RefreshCommand.IsRunning &&
        !window.Workspace.PageModel<TextsPageModel>().ResultsInText
            .StagePrimaryMarkingActionForTokenCommand.IsRunning;

    private static Border? OpenStagedFitTooltipForCaptureRegression(
        WalkthroughWindow window, WalkthroughScript script, WalkthroughStep step)
    {
        if (script.Id != "explained-word-card" || step.Id != "unknown-staged") return null;
        var stagedStrip = Assert.Single(window.Window.GetLogicalDescendants().OfType<Border>(),
            border => border.Classes.Contains("stagedStrip"));
        var wordStrip = Assert.Single(stagedStrip.GetVisualAncestors().OfType<Border>(), border =>
            Avalonia.Automation.AutomationProperties.GetAutomationId(border)?.EndsWith("-strip",
                StringComparison.Ordinal) == true);
        var stagedOrigin = stagedStrip.TranslatePoint(default, wordStrip);
        Assert.True(stagedOrigin is { X: > 0 }, "The staged note should sit beside the word strip.");
        Assert.Equal("Still fits the project.", ToolTip.GetTip(stagedStrip));
        var center = stagedStrip.TranslatePoint(
            new Point(stagedStrip.Bounds.Width / 2, stagedStrip.Bounds.Height / 2), window.Window);
        Assert.NotNull(center);
        window.Window.MouseMove(center!.Value);
        Dispatcher.UIThread.RunJobs();
        ToolTip.SetIsOpen(stagedStrip, true);
        Assert.True(ToolTip.GetIsOpen(stagedStrip));
        return stagedStrip;
    }

    internal static void SuppressTooltipsForCapture(Window window)
    {
        // Keep hover states for reveal controls while closing tooltips before the frame is rendered.
        var controls = window.GetVisualDescendants().OfType<Control>().Prepend(window).ToArray();
        foreach (var control in controls)
        {
            ToolTip.SetServiceEnabled(control, false);
            if (ToolTip.GetIsOpen(control)) ToolTip.SetIsOpen(control, false);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(controls, ToolTip.GetIsOpen);
    }

    private static bool Satisfies(WalkthroughWindow window, WalkthroughStep step)
    {
        var control = step.Kind == WalkthroughStepKind.WaitFor
            ? window.FindOptionalByAutomationId(step.AutomationId!)
            : window.FindByAutomationId(step.AutomationId!);
        if (control is null) return false;
        return step.Condition switch
        {
            "visible" => control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0,
            "hidden" => !control.IsEffectivelyVisible,
            "enabled" => control.IsEffectivelyEnabled,
            "text" => string.Equals(window.TextByAutomationId(step.AutomationId!), step.ExpectedText,
                StringComparison.Ordinal),
            "assessmentPublished" => AssessmentIsPublished(window, control),
            "parseProgressVisibleOrCompleted" => ParseProgressVisibleOrCompleted(window, control),
            _ => false,
        };
    }

    internal static bool ParseProgressVisibleOrCompleted(bool progressVisible, RunState assessmentState) =>
        progressVisible || assessmentState == RunState.Completed;

    private static bool ParseProgressVisibleOrCompleted(WalkthroughWindow window, Control progress) =>
        ParseProgressVisibleOrCompleted(
            progress.IsEffectivelyVisible && progress.Bounds.Width > 0 && progress.Bounds.Height > 0,
            window.Workspace.Assess.State);

    private static bool AssessmentIsPublished(WalkthroughWindow window, Control coverage)
    {
        var assessment = window.FindByAutomationId(AutomationIds.OverviewSpeed);
        var overview = window.Workspace.PageModel<OverviewPageModel>().Overview;
        return coverage.IsEffectivelyVisible && coverage.Bounds.Width > 0 && coverage.Bounds.Height > 0 &&
            assessment.IsEffectivelyVisible && assessment.Bounds.Width > 0 && assessment.Bounds.Height > 0 &&
            window.Workspace.Assess.State == RunState.Completed &&
            window.Workspace.Context.EvidencePublication.IsCompleted &&
            !window.Workspace.Context.NeedsAssessment &&
            overview is { AssessmentId: not null, AssessedUtc: not null };
    }

    internal static void AssertFixtureOutcome(
        WalkthroughWindow window, WalkthroughScript script, string parserPath, long deadline)
    {
        switch (script.Id)
        {
            case "first-run-setup-parse":
                window.WaitUntil(() => window.Workspace.Assess.State == RunState.Completed &&
                    window.Workspace.Context.EvidencePublication.IsCompleted,
                    WalkthroughSteps.Remaining(deadline), "the explicit Parse all words run did not finish");
                var firstRun = Assert.IsType<AssessCommandResponse>(window.Workspace.Assess.Result);
                Assert.Equal("first-run-parse", Assert.Single(firstRun.Words,
                    word => word.Word == SeededProject.FirstForm).RawSignature);
                Assert.Equal(2, FakeParser.Invocations(parserPath).Count(command => command == "batch"));
                break;
            case "try-word-typing":
                var trace = window.Workspace.PageModel<TryWordPageModel>().Trace;
                var traceResponse = Assert.IsType<SIL.Motif.Contract.Responses.WordTraceResponse>(trace.Result);
                Assert.Equal("motifa", window.TextByAutomationId(AutomationIds.TryWordInput));
                Assert.Equal("motifa", window.TextByAutomationId(AutomationIds.TryWordResult));
                Assert.True(traceResponse.Parsed && traceResponse.Complete);
                Assert.Contains("motifa-trace", traceResponse.DiagnosticJson);
                Assert.Equal(1, FakeParser.Invocations(parserPath).Count(command => command == "parse"));
                break;
            case "review-apply-refresh-parse":
                window.WaitUntil(() => window.Workspace.Assess.State == RunState.Completed &&
                    window.Workspace.Context.EvidencePublication.IsCompleted,
                    WalkthroughSteps.Remaining(deadline), "the explicit post-Refresh Parse did not finish");
                var refreshed = Assert.IsType<AssessCommandResponse>(window.Workspace.Assess.Result);
                Assert.Equal("after-refresh", Assert.Single(refreshed.Words,
                    word => word.Word == SeededProject.AnalysedWordForm).RawSignature);
                Assert.Equal(3, FakeParser.Invocations(parserPath).Count(command => command == "batch"));
                Assert.False(window.Workspace.Context.NeedsAssessment);
                break;
            case "no-longer-fits":
                var review = window.Workspace.PageModel<ReviewPageModel>();
                window.WaitUntil(() => review.ReviewGroups.Any(group => group.IsNoLongerFits),
                    WalkthroughSteps.Remaining(deadline), "the changed word did not appear in No longer fits");
                Assert.NotEmpty(review.ReviewGroups.Single(group => group.IsNoLongerFits).Items);
                break;
            case "handoff-cancel-retry":
                var handoff = window.Workspace.PageModel<AiHandoffPageModel>().Handoff;
                window.WaitUntil(() => handoff.State == RunState.Completed && handoff.Files.Count > 0,
                    WalkthroughSteps.Remaining(deadline), "the retried AI Handoff did not finish");
                Assert.Equal(2, FakeParser.Invocations(parserPath).Count(command => command == "import"));
                break;
        }
    }
}

internal sealed record WalkthroughFixturePreparation(IReadOnlyDictionary<string, string> ReadyMarkers)
{
    internal static WalkthroughFixturePreparation Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.Ordinal));
}

internal static class WalkthroughFixtureSeeder
{
    private static readonly IReadOnlyDictionary<string,
        Func<WalkthroughProjectContext, FixedClock, string, Task<WalkthroughFixturePreparation>>> Seeders =
        new Dictionary<string, Func<WalkthroughProjectContext, FixedClock, string,
            Task<WalkthroughFixturePreparation>>>(StringComparer.Ordinal)
        {
            ["fresh-project"] = (_, _, _) => Task.FromResult(WalkthroughFixturePreparation.Empty),
            ["first-run-ready"] = SeedFirstRunReadyAsync,
            ["overview-ready"] = SeedOverviewReadyAsync,
            ["explained-word-card"] = SeedOverviewReadyAsync,
            ["right-to-left-text"] = SeedOverviewReadyAsync,
            ["try-word-ready"] = SeedTryWordReadyAsync,
            ["apply-refresh-ready"] = SeedApplyRefreshReadyAsync,
            ["handoff-cancel-ready"] = SeedHandoffCancelReadyAsync,
            ["no-longer-fits-ready"] = SeedNoLongerFitsReadyAsync,
            ["warnings-ready"] = SeedWarningsReadyAsync,
        };

    public static Task<WalkthroughFixturePreparation> SeedAsync(
        string fixture, WalkthroughProjectContext project, FixedClock clock, string parserPath) =>
        Seeders.TryGetValue(fixture, out var seed)
            ? seed(project, clock, parserPath)
            : throw new InvalidDataException($"Unknown walkthrough fixture '{fixture}'.");

    private static Task<WalkthroughFixturePreparation> SeedFirstRunReadyAsync(
        WalkthroughProjectContext project, FixedClock _, string parserPath)
    {
        var heldPath = Path.Combine(project.ManagedRoot, "first-run-held-heartbeat");
        var parseStartedPath = Path.Combine(project.ManagedRoot, "first-run-parse-started");
        FakeParser.BehaveInPhasesBesideExecutable(parserPath, "batch",
            new { heartbeatPath = heldPath },
            new
            {
                startedPath = parseStartedPath,
                delayMilliseconds = 400,
                words = new[]
                {
                    new { word = SeededProject.FirstForm, outcome = "complete", signature = "first-run-parse" },
                },
            });
        return Task.FromResult(new WalkthroughFixturePreparation(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AutomationIds.SetupFinish] = heldPath,
            [AutomationIds.ParseAllWords] = parseStartedPath,
        }));
    }

    private static async Task<WalkthroughFixturePreparation> SeedOverviewReadyAsync(
        WalkthroughProjectContext project, FixedClock clock, string _)
    {
        var client = RealCommandClient.Create(project.ManagedRoot, project.ParserPath, timeProvider: clock);
        var baseline = await client.CaptureBaselineAsync(
            new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        Assert.Equal(CaptureTime, DateTimeOffset.Parse(baseline.Value!.Token.CapturedUtc, CultureInfo.InvariantCulture));
        await SeedSelectionAndSkipSetupAsync(project, client);
        return WalkthroughFixturePreparation.Empty;
    }

    private static async Task<WalkthroughFixturePreparation> SeedWarningsReadyAsync(
        WalkthroughProjectContext project, FixedClock clock, string parserPath)
    {
        await SeedOverviewReadyAsync(project, clock, parserPath);
        var check = GrammarCheckQuery.Query(new GrammarCheckRequest(project.FwDataPath), parserPath);
        Assert.True(check.Succeeded, check.Refusal?.Message);
        Assert.NotEmpty(check.Value!.Findings);
        return WalkthroughFixturePreparation.Empty;
    }

    private static async Task<WalkthroughFixturePreparation> SeedNoLongerFitsReadyAsync(
        WalkthroughProjectContext project, FixedClock clock, string parserPath)
    {
        const string word = "drifted-change-word";
        var change = PendingChangeFixture.AddIncorrectSpelling(
            project.FwDataPath, project.ManagedRoot, word);
        new FieldWorksSimulator(project.FwDataPath).DeleteWordform(change.Word);

        var client = RealCommandClient.Create(project.ManagedRoot, parserPath, timeProvider: clock);
        var baseline = await client.CaptureBaselineAsync(
            new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        await SeedSelectionAndSkipSetupAsync(project, client);
        return WalkthroughFixturePreparation.Empty;
    }

    private static async Task<WalkthroughFixturePreparation> SeedTryWordReadyAsync(
        WalkthroughProjectContext project, FixedClock clock, string parserPath)
    {
        await SeedOverviewReadyAsync(project, clock, parserPath);
        FakeParser.BehaveBesideExecutable(parserPath,
            FakeParser.TraceBehavior("motifa", "motifa-trace", "SeededRule"));
        return WalkthroughFixturePreparation.Empty;
    }

    private static async Task<WalkthroughFixturePreparation> SeedApplyRefreshReadyAsync(
        WalkthroughProjectContext project, FixedClock clock, string parserPath)
    {
        PendingChangeFixture.AddIncorrectSpelling(
            project.FwDataPath, project.ManagedRoot, "walkthrough-apply-word");
        var client = RealCommandClient.Create(project.ManagedRoot, parserPath, timeProvider: clock);
        await SeedSelectionAndSkipSetupAsync(project, client);
        var parseStartedPath = Path.Combine(project.ManagedRoot, "after-refresh-parse-started");
        FakeParser.BehaveInPhasesBesideExecutable(parserPath, "batch",
            AssessmentWords("before-refresh"), AssessmentWords("before-refresh"),
            new
            {
                startedPath = parseStartedPath,
                delayMilliseconds = 400,
                words = new[]
                {
                    new
                    {
                        word = SeededProject.AnalysedWordForm,
                        outcome = "complete",
                        signature = "after-refresh",
                    },
                },
            });
        await AssessAsync(project, client);
        return new WalkthroughFixturePreparation(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AutomationIds.ParseAllWords] = parseStartedPath,
        });
    }

    private static async Task<WalkthroughFixturePreparation> SeedHandoffCancelReadyAsync(
        WalkthroughProjectContext project, FixedClock clock, string parserPath)
    {
        var client = RealCommandClient.Create(project.ManagedRoot, parserPath, timeProvider: clock);
        var baseline = await client.CaptureBaselineAsync(
            new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        await SeedSelectionAndSkipSetupAsync(project, client);
        FakeParser.BehaveBesideExecutable(parserPath, AssessmentWords("handoff-ready"));
        await AssessAsync(project, client);
        var heldPath = Path.Combine(project.ManagedRoot, "handoff-held-heartbeat");
        FakeParser.BehaveInPhasesBesideExecutable(parserPath, "import",
            new { heartbeatPath = heldPath }, new { });
        return new WalkthroughFixturePreparation(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AutomationIds.WriteHandoff] = heldPath,
        });
    }

    private static async Task SeedSelectionAndSkipSetupAsync(
        WalkthroughProjectContext project, CommandClient client)
    {
        var selection = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, "Default", [project.TextId], []), CancellationToken.None);
        Assert.True(selection.Succeeded, selection.Refusal?.Message);
        var skipped = await client.SkipSetupAsync(new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
        if (project.ParserPath is not null)
        {
            var assessed = await client.AssessAsync(new AssessRequest(project.FwDataPath,
                    new SelectionRequest(false, [project.TextId], [], false, null)),
                new Progress<SIL.Motif.Contract.Responses.AssessmentProgress>(), CancellationToken.None);
            Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        }
    }

    private static async Task AssessAsync(WalkthroughProjectContext project, CommandClient client)
    {
        var assessed = await client.AssessAsync(new AssessRequest(project.FwDataPath),
            new Progress<SIL.Motif.Contract.Responses.AssessmentProgress>(), CancellationToken.None);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
    }

    private static object AssessmentWords(string signature) => new
    {
        words = new[]
        {
            new { word = SeededProject.AnalysedWordForm, outcome = "complete", signature },
        },
    };

    private static DateTimeOffset CaptureTime => new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
}

internal sealed record WalkthroughProjectContext(
    string ManagedRoot, string FwDataPath, Guid TextId, string? ParserPath);
