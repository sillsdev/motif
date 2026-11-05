using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AvaloniaHeadlessPlatformTests
{
    [Fact]
    public void DedicatedThreadOwnsAvaloniaDispatcher()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(5));
    }

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

    // The deadline fires off-thread and must wake a sleeping frame, or a stuck item hangs the run.
    [Fact]
    public async Task WorkThatNeverCompletesEndsTheFrameAtItsDeadline()
    {
        var run = Task.Run(() => Assert.Throws<TimeoutException>(() => AvaloniaHeadlessFixture.RunUntilComplete(
            () => new TaskCompletionSource().Task, TimeSpan.FromMilliseconds(200))));
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(Wait.Scaled(TimeSpan.FromSeconds(60)))));
        await run;
    }

    [Fact]
    public async Task WorkCompletedFromAnotherThreadEndsTheFrameBeforeItsDeadline()
    {
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var run = Task.Run(() => AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            // Runs only inside the pushed frame, so the gate completes while it sleeps.
            Dispatcher.UIThread.Post(() => started.TrySetResult(), DispatcherPriority.Background);
            return gate.Task;
        }, TimeSpan.FromMinutes(5)));
        await started.Task.WaitAsync(Wait.Scaled(TimeSpan.FromSeconds(60)));
        gate.SetResult();
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(Wait.Scaled(TimeSpan.FromSeconds(60)))));
        await run;
    }

    [Fact]
    public void HeadlessTopLevelClipboardRoundTripsText()
    {
        string? copied = null;
        Exception? failure = null;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var window = new MotifTestWindow();
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
            var transformRoot = Assert.IsType<LayoutTransformControl>(walkthrough.Window.Content);
            var root = Assert.IsType<Grid>(transformRoot.Child);
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
