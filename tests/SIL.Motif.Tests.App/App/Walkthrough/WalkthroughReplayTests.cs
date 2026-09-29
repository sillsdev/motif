using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using SIL.Motif.App;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.App.ViewModels;
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
        using var project = new WalkthroughProject(pristine, managedRoot,
            new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc));
        await WalkthroughFixtureSeeder.SeedAsync(script.Fixture, project, clock);

        var captures = new List<WalkthroughCapture>();
        var clipSegments = new List<WalkthroughClipSegment>();
        var deadline = Stopwatch.GetTimestamp() + (long)(WalkthroughReplay.DeadlineBudget(script).TotalSeconds * Stopwatch.Frequency);

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                WalkthroughFonts.Register();
                using var walkthrough = new WalkthroughWindow(
                    project.ManagedRoot, project.FwDataPath, timeProvider: clock);
                walkthrough.Window.Width = WalkthroughArtifacts.Width;
                walkthrough.Window.Height = WalkthroughArtifacts.Height;
                walkthrough.Window.SetValue(TextElement.FontFamilyProperty, new FontFamily("fonts:MotifWalkthrough#Andika"));
                walkthrough.Show();
                Assert.Equal(1d, walkthrough.Window.RenderScaling);
                WalkthroughReplay.Run(walkthrough, script, help, clock, captures, clipSegments, deadline);
                return Task.CompletedTask;
            }
            finally
            {
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
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
                        remaining < stepTimeout ? remaining : stepTimeout, $"step '{step.Id}' timed out");
                    break;
                case WalkthroughStepKind.Highlight:
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
                        return new WalkthroughCaptureCallout(
                            callout.AutomationId, help.CalloutCaption(step.Id, callout.AutomationId),
                            window.BoundsByAutomationId(callout.AutomationId));
                    }).ToArray();
                    captures.Add(WalkthroughArtifacts.Capture(step.Id, elapsedMs, step.DurationMs!.Value,
                        window.Window, callouts, step.CropPadding));
                    clipSegments.Add(new WalkthroughClipSegment(elapsedMs, step.DurationMs!.Value,
                        captures[^1].Png, callouts.FirstOrDefault()?.Bounds,
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
        var control = window.FindByAutomationId(step.AutomationId!);
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
    private static readonly IReadOnlyDictionary<string, Func<WalkthroughProject, FixedClock, Task>> Seeders =
        new Dictionary<string, Func<WalkthroughProject, FixedClock, Task>>(StringComparer.Ordinal)
        {
            ["fresh-project"] = (_, _) => Task.CompletedTask,
            ["overview-ready"] = SeedOverviewReadyAsync,
        };

    public static Task SeedAsync(string fixture, WalkthroughProject project, FixedClock clock) =>
        Seeders.TryGetValue(fixture, out var seed)
            ? seed(project, clock)
            : throw new InvalidDataException($"Unknown walkthrough fixture '{fixture}'.");

    private static async Task SeedOverviewReadyAsync(WalkthroughProject project, FixedClock clock)
    {
        var client = RealCommandClient.Create(project.ManagedRoot, timeProvider: clock);
        var baseline = await client.CaptureBaselineAsync(
            new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        Assert.Equal(CaptureTime, DateTimeOffset.Parse(baseline.Value!.Token.CapturedUtc, CultureInfo.InvariantCulture));
        var selection = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, "Default", [project.TextId], []), CancellationToken.None);
        Assert.True(selection.Succeeded, selection.Refusal?.Message);
        var skipped = await client.SkipSetupAsync(new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
    }

    private static DateTimeOffset CaptureTime => new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
}
