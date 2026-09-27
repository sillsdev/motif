using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Host;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Shows Motif's error window on the headless platform and drives its buttons against a scripted clipboard,
/// save dialog and mail launcher, and raises an error on the UI thread to show it is the window that opens.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CrashWindowTests(AvaloniaHeadlessFixture avalonia)
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private readonly RecordingClipboard _clipboard = new();
    private readonly ScriptedReportFiles _files = new();
    private readonly RecordingLauncher _launcher = new();

    [Fact]
    public void TheWindowShowsAPlainSummaryTheMessageFoldedDetailsAndItsFourButtons()
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
                Assert.Equal(CrashReportViewModel.Headline, Named<TextBlock>(window, "Crash headline").Text);
                Assert.Equal(CrashReportViewModel.Summary, Named<TextBlock>(window, "Crash summary").Text);
                Assert.Equal("Refreshing the texts failed.", Named<TextBlock>(window, "Crash message").Text);
                var details = Named<Expander>(window, "Crash details");
                Assert.False(details.IsExpanded);
                Assert.Equal("Details", details.Header);
                Assert.Equal(report.Details, Named<SelectableTextBlock>(window, "Crash details text").Text);
                Assert.Equal(["Report a problem", "Copy details", "Save report", "Email maintainer", "Close"],
                    window.GetLogicalDescendants().OfType<Button>().Select(button => button.Content as string));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ReportProblemOpensTheIssuesPageAndThanksTheReporter()
    {
        RunWithWindow(Report(), async window =>
        {
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text),
                text => text?.Contains("Thank you for helping improve Motif.", StringComparison.Ordinal) == true);

            window.GetLogicalDescendants().OfType<HyperlinkButton>()
                .Single(button => Equals(button.Content, "Report a problem"))
                .Command!.Execute(null);
            await Until(() => _launcher.Launched.Count == 1);

            Assert.Equal(new Uri("https://github.com/sillsdev/motif/issues"), _launcher.Launched[0]);
        });
    }

    [Fact]
    public void CopyDetailsPutsTheWholeReportOnTheClipboard()
    {
        var report = Report();
        RunWithWindow(report, async window =>
        {
            Click(window, "Copy details");
            await Until(() => _clipboard.Copied.Count == 1);

            Assert.Equal(report.ToText(), _clipboard.Copied[0]);
            Assert.Equal("The details are on the clipboard.", Named<TextBlock>(window, "Crash status").Text);
        });
    }

    [Fact]
    public void SaveReportWritesTheWholeReportUnderItsSuggestedTextFileName()
    {
        var report = Report();
        RunWithWindow(report, async window =>
        {
            Click(window, "Save report");
            await Until(() => _files.Saved.Count == 1);

            Assert.Equal((report.SuggestedFileName, report.ToText()), _files.Saved[0]);
            Assert.Equal("The report is saved. Attach it to your email.", Named<TextBlock>(window, "Crash status").Text);
        });
    }

    [Fact]
    public void ACancelledSaveSaysNothing()
    {
        _files.CancelSaves = true;
        RunWithWindow(Report(), async window =>
        {
            Click(window, "Save report");
            await Until(() => _files.SavePrompts == 1);

            Assert.False(Named<TextBlock>(window, "Crash status").IsVisible);
        });
    }

    [Fact]
    public void ASaveThatFailsSaysSoInTheWindow()
    {
        _files.SaveFailure = new IOException("the disk is full");
        RunWithWindow(Report(), async window =>
        {
            Click(window, "Save report");
            await Until(() => _files.SavePrompts == 1);

            Assert.Equal("Motif could not save the report: the disk is full",
                Named<TextBlock>(window, "Crash status").Text);
        });
    }

    [Fact]
    public void EmailMaintainerOpensTheMailProgramWithTheShortMailtoLink()
    {
        var report = Report();
        RunWithWindow(report, async window =>
        {
            Click(window, "Email maintainer");
            await Until(() => _launcher.Launched.Count == 1);

            Assert.Equal(CrashReportEmail.MailtoFor(report, MotifSupport.SupportEmail), _launcher.Launched[0]);
            Assert.Equal("Your email program should open. Attach the report you saved.",
                Named<TextBlock>(window, "Crash status").Text);
        });
    }

    [Fact]
    public void WithNoMailProgramTheWindowGivesTheAddressToWriteTo()
    {
        _launcher.Opens = false;
        RunWithWindow(Report(), async window =>
        {
            Click(window, "Email maintainer");
            await Until(() => _launcher.Launched.Count == 1);

            Assert.Equal("Motif could not open an email program. Write to john_lambert@sil.org and attach the saved report.",
                Named<TextBlock>(window, "Crash status").Text);
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
            var detach = reporter.Attach(Dispatcher.UIThread, main, () => shutdowns++);
            try
            {
                main.Show();
                Dispatcher.UIThread.Post(() => throw new InvalidOperationException("Refreshing the texts failed."));
                Dispatcher.UIThread.RunJobs();

                var window = Assert.IsType<CrashWindow>(reporter.Window);
                Assert.True(window.IsVisible);
                Assert.False(main.IsEnabled, "The workspace stayed usable after an unhandled error.");
                Assert.Equal("Refreshing the texts failed.", Named<TextBlock>(window, "Crash message").Text);
                Assert.Equal(0, shutdowns);

                window.Close();

                Assert.Equal(1, shutdowns);
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
                    Click(window, "Save report");
                    for (var pass = 0; pass < 3; pass++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    }
                    using var frame = window.CaptureRenderedFrame()!;
                    frame.Save(Path.Combine(folder, $"error-window-{(expanded ? "details" : "summary")}-{theme}.png"));
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    private CrashWindowServices Services() => new(_clipboard, _files, _launcher);

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

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Limit;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "The window never finished the action.");
    }

    private static void Click(Window window, string content) =>
        window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, content))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Named<T>(Window window, string automationName) where T : Control =>
        window.GetLogicalDescendants().OfType<T>()
            .Single(control => AutomationProperties.GetName(control) == automationName);

    private static CrashReport Report()
    {
        try
        {
            throw new InvalidOperationException("Refreshing the texts failed.");
        }
        catch (InvalidOperationException failure)
        {
            return new CrashReport(failure, new DateTimeOffset(2026, 9, 26, 14, 5, 9, TimeSpan.Zero), "0.1.0",
                "Test OS 1.0", "Test .NET 10");
        }
    }
}

/// <summary>Records each report the error window saves, or cancels or fails the save when told to.</summary>
public sealed class ScriptedReportFiles : IReportFilePicker
{
    /// <summary>The reports written, oldest first.</summary>
    public List<(string SuggestedFileName, string Text)> Saved { get; } = [];

    /// <summary>How many times a save dialog was shown, whether or not it wrote.</summary>
    public int SavePrompts { get; private set; }

    /// <summary>When true, the save dialog is cancelled.</summary>
    public bool CancelSaves { get; set; }

    /// <summary>When set, the save throws this after the dialog.</summary>
    public Exception? SaveFailure { get; set; }

    public Task<bool> SaveReportAsync(string suggestedFileName, string text, CancellationToken cancellationToken = default)
    {
        SavePrompts++;
        if (SaveFailure is { } failure) return Task.FromException<bool>(failure);
        if (CancelSaves) return Task.FromResult(false);
        Saved.Add((suggestedFileName, text));
        return Task.FromResult(true);
    }
}

/// <summary>Records each link handed to the mail program, and answers whether one opened.</summary>
public sealed class RecordingLauncher : IUriLauncher
{
    /// <summary>The links launched, oldest first.</summary>
    public List<Uri> Launched { get; } = [];

    /// <summary>What each launch reports: whether a program opened the link.</summary>
    public bool Opens { get; set; } = true;

    public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        Launched.Add(uri);
        return Task.FromResult(Opens);
    }
}
