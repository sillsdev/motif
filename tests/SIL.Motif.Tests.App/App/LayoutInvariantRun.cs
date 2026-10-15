using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless;
using SIL.Motif.App.Views;
using SIL.Motif.App.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

internal sealed class LayoutInvariantRun(string tier, ITestOutputHelper output) : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<LayoutFinding> _findings = [];
    private readonly HashSet<LayoutFindingContext> _inspected = [];
    private readonly List<LayoutFinding> _reportOnly = [];
    private readonly List<object> _timings = [];
    private readonly LayoutTextMeasurements _measurements = new();

    internal void Setup(string name, double milliseconds) => _timings.Add(new { Setup = name, Milliseconds = milliseconds });

    internal void Inspect(MainWindow window, string scene, LayoutCell cell, bool quick, WorkspaceShellViewModel workspace, Action? prepare = null)
    {
        var clock = Stopwatch.StartNew();
        LayoutInvariantFixtures.Apply(window, cell);
        prepare?.Invoke();
        if (prepare is not null) LayoutInvariantEngineTests.Settle(window);
        LayoutInvariantFixtures.RequirePage(window, workspace, workspace.CurrentPage);
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var context = new LayoutFindingContext(scene, cell.Name, cell.Zoom, "light");
        Assert.True(_inspected.Add(context), $"Duplicate invariant cell {context}.");
        _findings.AddRange(LayoutInvariantFixtures.RequiredActions(window, workspace, context));
        _findings.AddRange(quick ? ReachabilityAssertions.InspectQuick(window, context, _reportOnly) :
            ReachabilityAssertions.Inspect(window, context, _measurements, _reportOnly));
        _timings.Add(new { Scene = scene, Cell = cell, Milliseconds = clock.Elapsed.TotalMilliseconds });
    }

    internal void AssertFindings() => LayoutInvariantWorkList.AssertNoUnexpected(
        _findings, LayoutInvariantWorkList.For(tier), _inspected);

    public void Dispose()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "test-results");
        Directory.CreateDirectory(folder);
        var artifact = Path.Combine(folder, $"layout-invariants-{tier}-{Environment.ProcessId}.json");
        File.WriteAllText(artifact, JsonSerializer.Serialize(new
        {
            Tier = tier, Seconds = _clock.Elapsed.TotalSeconds, Cells = _inspected.Count,
            Timings = _timings, Findings = _findings, ReportOnlyFindings = _reportOnly,
            TextWidths = new { Computed = _measurements.ComputedWidths, Reused = _measurements.ReusedWidths },
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var reported in _reportOnly)
            output.WriteLine($"REPORT-ONLY {reported.Check}: {reported.Scene}/{reported.Size}/{reported.Zoom}/" +
                $"{reported.Theme} {reported.Control.Type} name='{reported.Control.Name}' " +
                $"id='{reported.Control.AutomationId}' ancestor='{reported.Control.NamedAncestor}'; {reported.Detail}");
        output.WriteLine($"{tier}: {_inspected.Count} cells in {_clock.Elapsed.TotalSeconds:0.00}s; {artifact}");
    }
}
