using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using SIL.Motif.App;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class WalkthroughReplayTests(PristineProjectFixture pristine)
{
    private static readonly DateTimeOffset CaptureTime = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Scripts => WalkthroughScriptLoader.Discover(FindRepositoryRoot())
        .Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(Scripts))]
    public async Task EveryWalkthroughRunsAgainstTheComposedWindow(string scriptPath)
    {
        var root = FindRepositoryRoot();
        var script = WalkthroughScriptLoader.Load(scriptPath);
        var help = WalkthroughHelpContent.Load(root, script.Id, "en");
        var managedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.Walkthrough", "engine", script.Id);
        WalkthroughTestFiles.DeleteDirectory(managedRoot);
        var clock = new FixedClock(CaptureTime, TimeZoneInfo.Utc);
        using var project = new WalkthroughProject(pristine, managedRoot,
            new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc));
        if (script.Id == "open-project-overview")
        {
            var client = RealCommandClient.Create(project.ManagedRoot, timeProvider: clock);
            var baseline = await client.CaptureBaselineAsync(
                new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            Assert.Equal(CaptureTime,
                DateTimeOffset.Parse(baseline.Value!.Token.CapturedUtc, CultureInfo.InvariantCulture));
            var selection = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, "Default", [project.TextId], []), CancellationToken.None);
            Assert.True(selection.Succeeded, selection.Refusal?.Message);
            var skipped = await client.SkipSetupAsync(
                new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
        }

        var captures = new List<WalkthroughCapture>();
        var deadline = Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency;

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
                WalkthroughReplay.Run(walkthrough, script, clock, captures, deadline);
                if (script.Id == "open-project-overview")
                {
                    Assert.Equal("Captured Thu 2 Apr, 12:00 PM", walkthrough.Workspace.Baseline.CapturedAtText);
                    Assert.Equal("Thursday, April 2, 2026 12:00 PM", walkthrough.Workspace.Baseline.CapturedTimeText);
                    Assert.Equal(CaptureTime,
                        walkthrough.Workspace.PageModel<OverviewPageModel>().Overview!.MotifStoreCreatedUtc);
                }
                return Task.CompletedTask;
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }, WalkthroughSteps.Remaining(deadline));

        Assert.NotEmpty(captures);
        WalkthroughArtifacts.Write(root, script, help, captures);
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
    public static void Run(
        WalkthroughWindow window, WalkthroughScript script, FixedClock clock,
        List<WalkthroughCapture> captures, long deadline)
    {
        var highlighted = new HashSet<string>(StringComparer.Ordinal);
        var elapsedMs = 0;
        foreach (var step in script.Steps)
        {
            switch (step.Kind)
            {
                case WalkthroughStepKind.Click:
                    if (step.AutomationId is { } id) window.ClickAutomationId(id);
                    else window.ClickAutomationName(step.AutomationName!);
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
                    break;
                case WalkthroughStepKind.Hold:
                    clock.Advance(TimeSpan.FromMilliseconds(step.DurationMs!.Value));
                    elapsedMs += step.DurationMs.Value;
                    break;
                case WalkthroughStepKind.Capture:
                    var callouts = step.Callouts!.Select(callout =>
                    {
                        Assert.Contains(callout.AutomationId, highlighted);
                        return new WalkthroughCaptureCallout(
                            callout.AutomationId, callout.Caption, window.BoundsByAutomationId(callout.AutomationId));
                    }).ToArray();
                    captures.Add(WalkthroughArtifacts.Capture(step.Id, elapsedMs, step.DurationMs!.Value,
                        window.Window, callouts));
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
