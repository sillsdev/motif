using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Controls;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Runs only when <c>MOTIF_PERF</c> is <c>1</c>, so the default gate never measures.</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public const string EnableVariable = "MOTIF_PERF";

    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
            Skip = $"Set {EnableVariable}=1 to measure page and view switches.";
    }
}

/// <summary>
/// Measures the first activation of every page and Texts tab, then warm tours at two window sizes, with the
/// "Motif.Ui" trace on. Initial Overview construction has a separate trace sample. The trace goes to
/// <c>MOTIF_PERF_TRACE</c> (default: a file in the temp folder); <c>tools/Summarize-MotifTrace.ps1</c> turns it into
/// the baseline table. The window renders with Skia software drawing and the frame is driven by hand, so the
/// first-frame figure is the render pass and not a GPU present.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("Category", "Perf")]
[Trait("MotifTestLevel", "System")]
public sealed class ViewSwitchPerfTests
{
    private static readonly (int Width, int Height, int Zoom)[] Sizes = [(1280, 800, 100), (640, 600, 150)];
    private static readonly string[] ColdTargets =
    [
        "page:Texts", "tab:AnalyzeTexts", "tab:Lists", "tab:WhatChanged", "page:TryAWord", "page:Timing",
        "page:Warnings", "page:Review", "page:AiHandoff", "page:AiAssistants", "page:Proposals", "page:Settings",
    ];
    private static readonly string[] WarmTargets =
    [
        "page:Overview", "page:Texts", "tab:AnalyzeTexts", "tab:Lists", "tab:WhatChanged", "page:TryAWord",
        "page:Timing", "page:Warnings", "page:Review", "page:AiHandoff", "page:AiAssistants", "page:Proposals",
        "page:Settings",
    ];

    [PerfFact]
    public void EveryPageAndViewSwitchesColdThenWarmAndTheTraceIsWritten()
    {
        var path = Environment.GetEnvironmentVariable("MOTIF_PERF_TRACE")
            ?? Path.Combine(Path.GetTempPath(), "motif-perf-trace.jsonl");
        File.Delete(path);
        var warmPasses = int.TryParse(Environment.GetEnvironmentVariable("MOTIF_PERF_N"), out var n) && n > 0 ? n : 5;
        var initialViewSamples = new List<InitialViewSample>();

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var sink = MotifTrace.StartListening(path);
            var application = Application.Current!;
            foreach (var (width, height, zoom) in Sizes)
            {
                var size = $"{width}x{height}@{zoom}";
                var preferences = new MemoryUserPreferencesStore();
                preferences.Update(current => current with { ZoomPercent = zoom });
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                    activateTextsPage: false,
                    preferencesStore: preferences,
                    configureWindow: initialWindow =>
                    {
                        initialWindow.Width = width;
                        initialWindow.Height = height;
                    },
                    recordSetup: (stage, milliseconds) =>
                    {
                        if (stage == "compose-and-show")
                            initialViewSamples.Add(new InitialViewSample($"initial {size}",
                                DateTimeOffset.UtcNow.AddMilliseconds(-milliseconds), milliseconds));
                    });
                try
                {
                    PageScreenshots.Settle(window);
                    await Quiet(window);
                    Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
                    MatrixCaptureState.Assert(window, workspace, WorkspacePage.Overview, null);
                    Assert.Equal((double)width, window.Width);
                    Assert.Equal((double)height, window.Height);
                    Assert.Equal(zoom, preferences.Current.ZoomPercent);
                    Assert.Equal(zoom, workspace.PageModel<SettingsPageModel>().Settings!.ZoomPercent);
                    Assert.Equal(zoom / 100d, WindowZoomPolicy.Transform.ScaleX);
                    Assert.Equal(zoom / 100d, WindowZoomPolicy.Transform.ScaleY);
                    var zoomRoot = window.FindControl<LayoutTransformControl>("RootLayoutTransform");
                    Assert.NotNull(zoomRoot);
                    Assert.Same(WindowZoomPolicy.Transform, zoomRoot!.LayoutTransform);

                    for (var pass = 0; pass <= warmPasses; pass++)
                    {
                        if (pass == 0)
                        {
                            MotifTrace.RunLabel = $"cold {size}";
                            foreach (var (name, page, tab) in PageScreenshots.Views().Skip(1))
                            {
                                await Switch(workspace, window, name, page, tab);
                                if (name != "2b-texts-analyze") continue;
                                MatrixCaptureState.Assert(window, workspace, WorkspacePage.Texts,
                                    TextsTab.AnalyzeTexts);
                                MotifTrace.RunLabel = $"seed {size}";
                                await PageScreenshots.SeedSampleReaderTokenAsync(workspace);
                                MotifTrace.RunLabel = $"cold {size}";
                            }

                            MotifTrace.RunLabel = $"seed {size}";
                            await PageScreenshots.TryTheSampleWord(workspace);
                            await WaitForFirstFrame(window, "Try a Word after sample seeding");
                            await Quiet(window);
                            continue;
                        }

                        MotifTrace.RunLabel = $"prepare {size}";
                        workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
                        MotifTrace.RunLabel = $"warm {size}";
                        foreach (var (name, page, tab) in PageScreenshots.Views())
                            await Switch(workspace, window, name, page, tab);
                    }
                }
                finally
                {
                    MotifTrace.RunLabel = null;
                    WindowZoomPolicy.Apply(application, 100);
                    window.Close();
                }
            }
        }, TimeSpan.FromMinutes(10));

        Assert.True(File.Exists(path) && File.ReadLines(path).Any(line => line.Contains("\"view.switch\"")),
            "The trace holds no switch spans.");
        AppendInitialViewMeasurements(path, initialViewSamples);
        Assert.Equal(Sizes.Length, initialViewSamples.Count);
        foreach (var (width, height, zoom) in Sizes)
        {
            var size = $"{width}x{height}@{zoom}";
            AssertSwitchTargets(path, $"cold {size}", ColdTargets);
            AssertSwitchTargets(path, $"warm {size}", Enumerable.Range(0, warmPasses).SelectMany(_ => WarmTargets));
            Assert.Equal(1, CountSpans(path, "harness.initial-view-construction", $"initial {size}"));
        }
    }

    private sealed record InitialViewSample(string Label, DateTimeOffset StartedAt, double Milliseconds);

    private static void AppendInitialViewMeasurements(string path, IEnumerable<InitialViewSample> samples)
    {
        var lines = samples.Select(sample => JsonSerializer.Serialize(new
        {
            name = "harness.initial-view-construction",
            id = Guid.NewGuid().ToString("N")[..16],
            parent = (string?)null,
            start = sample.StartedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ms = Math.Round(sample.Milliseconds, 3),
            label = sample.Label,
            kind = "initial",
            to = WorkspacePage.Overview.ToString(),
        }));
        File.AppendAllLines(path, lines);
    }

    private static void AssertSwitchTargets(string path, string label, IEnumerable<string> expectedTargets)
    {
        var expected = expectedTargets.ToArray();
        var actual = File.ReadLines(path).Select(line => SwitchTarget(line, label))
            .Where(target => target is not null).Select(target => target!).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.OrderBy(target => target, StringComparer.Ordinal),
            actual.OrderBy(target => target, StringComparer.Ordinal));
    }

    private static int CountSpans(string path, string spanName, string label) => File.ReadLines(path).Count(line =>
    {
        using var document = JsonDocument.Parse(line);
        var span = document.RootElement;
        return span.GetProperty("name").GetString() == spanName &&
            span.TryGetProperty("label", out var spanLabel) && spanLabel.GetString() == label;
    });

    private static string? SwitchTarget(string line, string label)
    {
        using var document = JsonDocument.Parse(line);
        var span = document.RootElement;
        if (span.GetProperty("name").GetString() != "view.switch" ||
            !span.TryGetProperty("label", out var spanLabel) || spanLabel.GetString() != label) return null;
        return $"{span.GetProperty("kind").GetString()}:{span.GetProperty("to").GetString()}";
    }

    private static async Task Switch(WorkspaceShellViewModel workspace, Avalonia.Controls.Window window, string name,
        WorkspacePage page, TextsTab tab)
    {
        var texts = workspace.PageModel<TextsPageModel>();
        using var wall = MotifTrace.Begin("harness.click-to-frame");
        wall?.SetTag("view", name);
        if (page == WorkspacePage.Texts && workspace.CurrentPage == WorkspacePage.Texts) texts.Tab = tab;
        else
        {
            if (page == WorkspacePage.Texts) texts.Tab = tab;
            workspace.SelectedPage = workspace.PageOf(page);
        }

        await WaitForFirstFrame(window, name);
        wall?.Dispose();
        await Quiet(window);
    }

    private static Task WaitForFirstFrame(Avalonia.Controls.Window window, string name)
    {
        var clock = Stopwatch.StartNew();
        while (MotifTrace.CurrentSwitch is not null && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(MotifTrace.CurrentSwitch is null, $"The switch to '{name}' never reached a first frame.");
        return Task.CompletedTask;
    }

    /// <summary>Lets background work from a switch finish before the next one is measured.</summary>
    private static async Task Quiet(Avalonia.Controls.Window window)
    {
        await Task.Delay(60);
        PageScreenshots.Settle(window);
    }
}
