using Avalonia.Controls;
using Avalonia.Input.Platform;
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
}
