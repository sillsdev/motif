using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CrashWindowTests(AvaloniaHeadlessFixture avalonia)
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private readonly RecordingClipboard _clipboard = new();
    private readonly RecordingLauncher _launcher = new();

    [Fact]
    public void TheWindowKeepsFullDetailsLocalAndOffersAReviewedReport()
    {
        var report = Report();
        avalonia.Invoke(() =>
        {
            var window = new CrashWindow(report, Services());
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal("Motif has stopped", window.Title);
                Assert.Equal("Refreshing PRIVATEWORD at /home/private/project.fwdata failed.",
                    Named<TextBlock>(window, "Crash message").Text);
                Assert.Equal(report.Details, Named<SelectableTextBlock>(window, "Crash details text").Text);
                Assert.Equal(["Report a problem", "Close"],
                    window.GetLogicalDescendants().OfType<Button>().Select(button => button.Content as string));
                Assert.Equal(CrashReportViewModel.Summary,
                    Named<TextBlock>(window, "Crash summary").Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OpeningTheReportActionDoesNotOpenTheBrowserBeforeThePreviewIsReviewed()
    {
        RunWithWindow(Report(), window =>
        {
            Click(window, "Report a problem");
            Dispatcher.UIThread.RunJobs();

            var preview = Assert.IsType<ProblemReportPreviewWindow>(window.CurrentProblemReportPreview);
            Assert.DoesNotContain("PRIVATEWORD", preview.Model.ReportText, StringComparison.Ordinal);
            Assert.Empty(_launcher.Launched);
            preview.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void CloseClosesTheWindow()
    {
        RunWithWindow(Report(), window =>
        {
            Click(window, "Close");
            Assert.False(window.IsVisible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AnErrorThatEscapesTheUiThreadOpensTheErrorWindowAndClosingItClosesMotif()
    {
        avalonia.Invoke(() =>
        {
            var main = new Window();
            var shutdowns = 0;
            var reporter = new CrashReporter(TimeProvider.System, Services());
            var detach = reporter.Attach(Dispatcher.UIThread, main, () =>
            {
                shutdowns++;
                main.Close();
            });
            try
            {
                main.Show();
                Dispatcher.UIThread.Post(() => throw new InvalidOperationException("Refreshing the texts failed."));
                Dispatcher.UIThread.RunJobs();

                var window = Assert.IsType<CrashWindow>(reporter.Window);
                Assert.True(window.IsVisible);
                Assert.False(main.IsEnabled);
                Click(window, "Close");
                Assert.Equal(1, shutdowns);
                Assert.False(main.IsVisible);
            }
            finally
            {
                detach();
                main.Close();
            }
        });
    }

    [Fact]
    public void ASecondEscapedErrorWhileTheWindowIsOpenIsLeftUnhandledSoMotifExits()
    {
        avalonia.Invoke(() =>
        {
            var main = new Window();
            var reporter = new CrashReporter(TimeProvider.System, Services());
            var detach = reporter.Attach(Dispatcher.UIThread, main, () => { });
            try
            {
                Dispatcher.UIThread.Post(() => throw new InvalidOperationException("first"));
                Dispatcher.UIThread.RunJobs();
                var first = reporter.Window;
                Assert.NotNull(first);

                Dispatcher.UIThread.Post(() => throw new InvalidOperationException("second"));
                var escaped = Assert.Throws<InvalidOperationException>(() => Dispatcher.UIThread.RunJobs());

                Assert.Equal("second", escaped.Message);
                Assert.Same(first, reporter.Window);
            }
            finally
            {
                detach();
                reporter.Window?.Close();
                main.Close();
            }
        });
    }

    [Fact]
    public void AfterDetachingAnEscapedErrorIsNoLongerReported()
    {
        avalonia.Invoke(() =>
        {
            var main = new Window();
            var reporter = new CrashReporter(TimeProvider.System, Services());
            reporter.Attach(Dispatcher.UIThread, main, () => { })();

            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("after detaching"));
            Assert.Throws<InvalidOperationException>(() => Dispatcher.UIThread.RunJobs());

            Assert.Null(reporter.Window);
            main.Close();
        });
    }

    [Fact]
    public void DetachingClosesAnOpenErrorWindowWithoutClosingMotif()
    {
        avalonia.Invoke(() =>
        {
            var main = new Window();
            var shutdowns = 0;
            var reporter = new CrashReporter(TimeProvider.System, Services());
            var detach = reporter.Attach(Dispatcher.UIThread, main, () => shutdowns++);
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("while a session closes"));
            Dispatcher.UIThread.RunJobs();

            detach();

            Assert.False(reporter.Window!.IsVisible);
            Assert.Equal(0, shutdowns);
            main.Close();
        });
    }

    [ScreenshotFact]
    public void CaptureTheErrorWindow()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        avalonia.Invoke(() =>
        {
            foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
            foreach (var expanded in new[] { false, true })
            {
                var window = new CrashWindow(Report(), Services()) { RequestedThemeVariant = variant };
                try
                {
                    window.Show();
                    Named<Expander>(window, "Crash details").IsExpanded = expanded;
                    for (var pass = 0; pass < 3; pass++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    }
                    using var frame = window.CaptureRenderedFrame()!;
                    frame.Save(Path.Combine(folder, $"error-window-{(expanded ? "details" : "summary")}-{theme}.png"),
                        PngBitmapEncoderOptions.Default);
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    private CrashWindowServices Services() => new(_clipboard, _launcher);

    private void RunWithWindow(CrashReport report, Func<CrashWindow, Task> work) =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var window = new CrashWindow(report, Services());
            try
            {
                window.Show();
                window.UpdateLayout();
                await work(window);
            }
            finally
            {
                window.Close();
            }
        }, Limit);

    private static void Click(Window window, string content) =>
        window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, content))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static T Named<T>(Window window, string automationName) where T : Control =>
        window.GetLogicalDescendants().OfType<T>()
            .Single(control => AutomationProperties.GetName(control) == automationName);

    private static CrashReport Report()
    {
        try
        {
            throw new InvalidOperationException("Refreshing PRIVATEWORD at /home/private/project.fwdata failed.",
                new IOException("raw stderr: secret grammar text"));
        }
        catch (InvalidOperationException failure)
        {
            return new CrashReport(failure, DateTimeOffset.UtcNow, "0.1.0", "Test OS 1.0", "Test .NET 10");
        }
    }

    private sealed class RecordingClipboard : IClipboard
    {
        public List<string> Copied { get; } = [];

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Copied.Add(text);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLauncher : IUriLauncher
    {
        public List<Uri> Launched { get; } = [];

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Launched.Add(uri);
            return Task.FromResult(true);
        }
    }
}
