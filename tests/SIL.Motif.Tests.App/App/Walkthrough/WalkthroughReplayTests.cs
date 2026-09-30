using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Styling;
using SIL.Motif.App;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class WalkthroughReplayTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly DateTimeOffset CaptureTime = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Scripts => WalkthroughScriptLoader.Discover(FindRepositoryRoot())
        .Select(path => new object[] { path });

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

    [Theory]
    [MemberData(nameof(Scripts))]
    public async Task EveryWalkthroughRunsAgainstTheComposedWindow(string scriptPath)
    {
        var root = FindRepositoryRoot();
        var script = WalkthroughScriptLoader.Load(scriptPath);
        var help = WalkthroughHelpContent.Load(root, script.Id, "en");
        var managedRoot = WalkthroughTestFiles.EngineRoot(script.Id);
        WalkthroughTestFiles.DeleteDirectory(managedRoot);
        var clock = new FixedClock(CaptureTime, TimeZoneInfo.Utc);
        var (projectLifetime, project) = await CreateProjectAsync(script.Fixture, managedRoot);
        using (projectLifetime)
        {
            await WalkthroughFixtureSeeder.SeedAsync(script.Fixture, project, clock);

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
                        project.ManagedRoot, project.FwDataPath, parserPath: project.ParserPath, timeProvider: clock);
                    walkthrough.Window.Width = WalkthroughArtifacts.Width;
                    walkthrough.Window.Height = WalkthroughArtifacts.Height;
                    walkthrough.Window.SetValue(TextElement.FontFamilyProperty, new FontFamily("fonts:MotifWalkthrough#Andika"));
                    walkthrough.Show();
                    Assert.Equal(1d, walkthrough.Window.RenderScaling);
                    WalkthroughReplay.Run(walkthrough, script, help, clock, captures, clipSegments, deadline);
                    if (script.Id == "explained-word-card") AssertExplainedWordCard(walkthrough);
                    return Task.CompletedTask;
                }
                finally
                {
                    Application.Current.RequestedThemeVariant = previousTheme;
                    CultureInfo.CurrentCulture = previousCulture;
                    CultureInfo.CurrentUICulture = previousUiCulture;
                }
            }, WalkthroughSteps.Remaining(deadline));

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
            WalkthroughArtifacts.Write(root, script, help, captures, clipSegments, output.WriteLine);
        }
    }

    private async Task<(IDisposable Lifetime, WalkthroughProjectContext Project)> CreateProjectAsync(
        string fixture, string managedRoot)
    {
        if (fixture == "explained-word-card")
        {
            var project = await ExplainedWordCardWalkthroughProject.CreateAsync(managedRoot);
            return (project, new WalkthroughProjectContext(
                project.ManagedRoot, project.FwDataPath, project.TextId, project.ParserPath));
        }

        var ordinary = new WalkthroughProject(pristine, managedRoot,
            new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc));
        return (ordinary, new WalkthroughProjectContext(
            ordinary.ManagedRoot, ordinary.FwDataPath, ordinary.TextId, null));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
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
        foreach (var form in new[] { "geldi", "evler", "kediye", "adamlarında", "okullarında" })
        {
            Assert.NotEmpty(byForm[form].Readings);
            Assert.All(byForm[form].Readings, reading => Assert.NotEqual("?", reading.Text));
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

        Assert.Equal(AnalysisMarkingClass.Capped, byForm["okullarında"].Marking.PanGlossClass);
        Assert.Equal(ReadingGrade.Approved,
            Assert.Single(byForm["okullarında"].Marking.FieldWorksAnalyses).Opinion);
    }
}

internal static class WalkthroughFonts
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        FontManager.Current.AddFontCollection(new EmbeddedFontCollection(
            new Uri("fonts:MotifWalkthrough"),
            new Uri("avares://SIL.Motif.Tests.App/Assets/Fonts")));
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
        List<WalkthroughCapture> captures, List<WalkthroughClipSegment> clipSegments, long deadline)
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
                    clipSegments.Add(new WalkthroughClipSegment(elapsedMs, step.DurationMs!.Value,
                        WalkthroughArtifacts.CaptureFrame(window.Window), holdBounds,
                        WalkthroughClipSegmentKind.Hold, null));
                    clock.Advance(TimeSpan.FromMilliseconds(step.DurationMs!.Value));
                    elapsedMs += step.DurationMs.Value;
                    break;
                case WalkthroughStepKind.Capture:
                    var callouts = step.Callouts!.Select(callout =>
                    {
                        Assert.Contains(callout.AutomationId, highlighted);
                        var bounds = window.BoundsByAutomationId(callout.AutomationId);
                        if (callout.AutomationId.EndsWith("-unread", StringComparison.Ordinal))
                            bounds = WalkthroughArtifacts.PadUnreadHighlightTarget(bounds);
                        return new WalkthroughCaptureCallout(
                            callout.AutomationId, help.CalloutCaption(step.Id, callout.AutomationId),
                            bounds);
                    }).ToArray();
                    var capture = WalkthroughArtifacts.Capture(step.Id, elapsedMs, step.DurationMs!.Value,
                        window.Window, callouts, step.CropPadding, step.Scale);
                    if (script.Id == "explained-word-card")
                    {
                        Assert.True(capture.Scale >= 2);
                        Assert.NotNull(capture.SourceFrameCropBounds);
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

    private static bool Satisfies(WalkthroughWindow window, WalkthroughStep step)
    {
        var control = step.Kind == WalkthroughStepKind.WaitFor
            ? window.FindOptionalByAutomationId(step.AutomationId!)
            : window.FindByAutomationId(step.AutomationId!);
        if (control is null) return false;
        return step.Condition switch
        {
            "visible" => control.IsEffectivelyVisible,
            "hidden" => !control.IsEffectivelyVisible,
            "enabled" => control.IsEffectivelyEnabled,
            "text" => string.Equals(window.TextByAutomationId(step.AutomationId!), step.ExpectedText,
                StringComparison.Ordinal),
            _ => false,
        };
    }
}

internal static class WalkthroughFixtureSeeder
{
    private static readonly IReadOnlyDictionary<string, Func<WalkthroughProjectContext, FixedClock, Task>> Seeders =
        new Dictionary<string, Func<WalkthroughProjectContext, FixedClock, Task>>(StringComparer.Ordinal)
        {
            ["fresh-project"] = (_, _) => Task.CompletedTask,
            ["overview-ready"] = SeedOverviewReadyAsync,
            ["explained-word-card"] = SeedOverviewReadyAsync,
        };

    public static Task SeedAsync(string fixture, WalkthroughProjectContext project, FixedClock clock) =>
        Seeders.TryGetValue(fixture, out var seed)
            ? seed(project, clock)
            : throw new InvalidDataException($"Unknown walkthrough fixture '{fixture}'.");

    private static async Task SeedOverviewReadyAsync(WalkthroughProjectContext project, FixedClock clock)
    {
        var client = RealCommandClient.Create(project.ManagedRoot, project.ParserPath, timeProvider: clock);
        var baseline = await client.CaptureBaselineAsync(
            new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        Assert.Equal(CaptureTime, DateTimeOffset.Parse(baseline.Value!.Token.CapturedUtc, CultureInfo.InvariantCulture));
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

    private static DateTimeOffset CaptureTime => new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
}

internal sealed record WalkthroughProjectContext(
    string ManagedRoot, string FwDataPath, Guid TextId, string? ParserPath);
