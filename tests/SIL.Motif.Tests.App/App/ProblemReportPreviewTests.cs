using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProblemReportPreviewTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private readonly AvaloniaHeadlessFixture _avalonia;
    private readonly RecordingClipboard _clipboard = new();
    private readonly RecordingLauncher _launcher = new();

    public ProblemReportPreviewTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void ThePreviewStartsPrivateSafeAndRequiresAnExplicitLocalDetailsChoice()
    {
        RunWithWindow(window =>
        {
            var preview = Find<TextBox>(window, "Problem report preview");
            var include = window.GetLogicalDescendants().OfType<CheckBox>().Single();

            Assert.False(include.IsChecked);
            Assert.Contains("Motif version:", preview.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATEWORD", preview.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("/home/private", preview.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret grammar", preview.Text, StringComparison.Ordinal);
            var instructions = string.Join(" ", window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(text => text.Text).OfType<string>());
            Assert.Contains("does not submit", instructions, StringComparison.Ordinal);
            Assert.Contains("GitHub Issues", instructions, StringComparison.Ordinal);
            Assert.Contains("copy the reviewed report and email john_lambert@sil.org", instructions,
                StringComparison.Ordinal);

            include.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Contains("PRIVATEWORD", preview.Text, StringComparison.Ordinal);
            Assert.Contains("/home/private/project.fwdata", preview.Text, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheNonClosingCopyActionSitsLeftOfTheClosingGroupWhichEnterAndEscapeActivate()
    {
        RunWithWindow(window =>
        {
            window.UpdateLayout();
            var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
            Button Named(string content) => Assert.Single(buttons, button => Equals(button.Content, content));
            var copy = Named(SIL.Motif.App.MotifAppStrings.CopyReport);
            var open = Named(SIL.Motif.App.MotifAppStrings.OpenIssueForm);
            var close = Named(SIL.Motif.App.MotifAppStrings.Close);
            double X(Button button) => button.TranslatePoint(default, window)!.Value.X;
            Assert.True(X(copy) < X(open));
            Assert.True(X(open) < X(close));
            Assert.True(close.IsDefault);
            Assert.True(close.IsCancel);
            Assert.False(copy.IsDefault || copy.IsCancel || open.IsDefault || open.IsCancel);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void CopyAndIssueActionsUseExactlyTheVisiblePreview()
    {
        RunWithWindow(async window =>
        {
            var preview = Find<TextBox>(window, "Problem report preview");
            window.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var reviewed = preview.Text ?? string.Empty;

            Click(window, SIL.Motif.App.MotifAppStrings.CopyReport);
            await Until(() => _clipboard.Copied.Count == 1);
            Assert.Equal(reviewed, _clipboard.Copied[0]);

            Click(window, SIL.Motif.App.MotifAppStrings.OpenIssueForm);
            await Until(() => _launcher.Launched.Count == 1);
            Assert.Equal("https", _launcher.Launched[0].Scheme);
            Assert.StartsWith(AppLinks.NewIssue, _launcher.Launched[0].AbsoluteUri, StringComparison.Ordinal);
            Assert.Contains(reviewed, Uri.UnescapeDataString(_launcher.Launched[0].AbsoluteUri), StringComparison.Ordinal);
            Assert.Contains("Review the report there before submitting it.",
                Find<TextBlock>(window, "Problem report status").Text, StringComparison.Ordinal);
        });
    }

    private void RunWithWindow(Func<ProblemReportPreviewWindow, Task> work) =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var window = new ProblemReportPreviewWindow(Report(), new ProblemReportWindowServices(_clipboard, _launcher));
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

    private static ProblemReport Report()
    {
        var refusal = new Refusal("project.operation-io", FailureReason.Refused,
            "Could not read PRIVATEWORD in /home/private/project.fwdata: secret grammar text.",
            new Dictionary<string, string> { ["projectPath"] = "/home/private/project.fwdata" });
        return ProblemReport.FromRefusal(SIL.Motif.App.ViewModels.WindowRefusal.From(refusal));
    }

    private static T Find<T>(Control parent, string automationName) where T : Control =>
        parent.GetLogicalDescendants().OfType<T>()
            .Single(control => AutomationProperties.GetName(control) == automationName);

    private static void Click(Window window, string content) =>
        window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, content))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Limit;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "The report preview did not finish the action.");
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
