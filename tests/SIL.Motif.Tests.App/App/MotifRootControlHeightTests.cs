using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.App.Composition;
using Avalonia.LogicalTree;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Each real Motif window, and a flyout opened inside it, gives a button Motif's 28 px height.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MotifRootControlHeightTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void MainWindowResolvesAndMeasuresTheCompactButtonHeight() => avalonia.Invoke(() =>
    {
        var window = new MainWindow();
        try
        {
            var button = new Button { Content = "Probe", VerticalAlignment = VerticalAlignment.Top };
            Probes(window).Children.Add(button);
            Show(window);
            AssertCompact(button);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void AButtonInsideAFlyoutOpenedInMainWindowMeasuresTheCompactHeight() => avalonia.Invoke(() =>
    {
        var window = new MainWindow();
        try
        {
            var inner = new Button { Content = "Inside" };
            var opener = new Button
            {
                Content = "Open",
                VerticalAlignment = VerticalAlignment.Top,
                Flyout = new Flyout { Content = inner },
            };
            Probes(window).Children.Add(opener);
            Show(window);
            opener.Flyout!.ShowAt(opener);
            Dispatcher.UIThread.RunJobs();
            inner.UpdateLayout();

            Assert.True(((Flyout)opener.Flyout).IsOpen);
            AssertCompact(inner);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void DiagnosticWindowResolvesTheCompactButtonHeight() => avalonia.Invoke(() =>
    {
        var files = new ScriptedDiagnosticFiles();
        var tools = new DiagnosticToolsViewModel(new TraceWordViewModel(), new RecordingClipboard(), files, files);
        var window = new DiagnosticWindow(new OpenedDiagnostic(new TraceWordViewModel(), null), tools);
        try { AssertResolves(window); }
        finally { window.Close(); }
    });

    [Fact]
    public void ProblemReportPreviewWindowResolvesAndMeasuresTheCompactButtonHeight() => avalonia.Invoke(() =>
    {
        var refusal = new Refusal("project.operation-io", FailureReason.Refused, "Could not read.",
            new Dictionary<string, string> { ["projectPath"] = "/p.fwdata" });
        var window = new ProblemReportPreviewWindow(
            ProblemReport.FromRefusal(WindowRefusal.From(refusal)), new ProblemReportWindowServices());
        try
        {
            Show(window);
            AssertCompact(window.GetLogicalDescendants().OfType<Button>().First());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void CrashWindowResolvesAndMeasuresTheCompactButtonHeight() => avalonia.Invoke(() =>
    {
        var report = new CrashReport(new InvalidOperationException("Failed."), DateTimeOffset.UtcNow, "0.1.0", "OS", ".NET");
        var window = new CrashWindow(report, new CrashWindowServices());
        try
        {
            Show(window);
            AssertCompact(window.GetLogicalDescendants().OfType<Button>().First());
        }
        finally { window.Close(); }
    });

    private static Panel Probes(MainWindow window) =>
        (Panel)((LayoutTransformControl)window.Content!).Child!;

    private static void Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void AssertResolves(Control control)
    {
        Assert.True(control.TryFindResource("ButtonDefaultHeight", control.ActualThemeVariant, out var height));
        Assert.Equal(28d, Assert.IsType<double>(height));
    }

    private static void AssertCompact(Button button)
    {
        AssertResolves(button);
        Assert.Equal(28d, button.Bounds.Height);
    }
}
