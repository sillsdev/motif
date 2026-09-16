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
}
