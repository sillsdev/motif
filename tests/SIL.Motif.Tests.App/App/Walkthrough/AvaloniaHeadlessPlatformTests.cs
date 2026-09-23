using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class AvaloniaHeadlessPlatformTests
{
    [Fact]
    public void TaskRunContinuationResumesOnAvaloniaThreadWhenPumped()
    {
        var uiThreadId = 0;
        var continuationThreadId = 0;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            uiThreadId = Environment.CurrentManagedThreadId;
            await Task.Run(() => 1).ConfigureAwait(true);
            continuationThreadId = Environment.CurrentManagedThreadId;
        }, TimeSpan.FromSeconds(5));

        Assert.Equal(uiThreadId, continuationThreadId);
    }

    [Fact]
    public void HeadlessTopLevelClipboardRoundTripsText()
    {
        string? copied = null;
        Exception? failure = null;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var window = new Window();
            window.Show();
            try
            {
                IClipboard clipboard = window.Clipboard
                    ?? throw new InvalidOperationException("The headless window has no clipboard.");
                await clipboard.SetTextAsync("clipboard smoke text");
                copied = await clipboard.TryGetTextAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(5));

        Assert.Null(failure);
        Assert.Equal("clipboard smoke text", copied);
    }

    [Fact]
    public void WalkthroughClickInvokesAButtonCommandThroughHeadlessInput()
    {
        var count = 0;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(Path.GetTempPath(), "unused.fwdata");
            var root = Assert.IsType<Grid>(walkthrough.Window.Content);
            var probe = new Button
            {
                Content = "Count probe",
                Command = new RelayCommand(() => count++),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            AutomationProperties.SetName(probe, "Count probe");
            Grid.SetRow(probe, 1);
            Grid.SetColumn(probe, 1);
            root.Children.Add(probe);

            walkthrough.Show();
            walkthrough.Click("Count probe");

            Assert.Equal(1, count);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(5));
    }
}
