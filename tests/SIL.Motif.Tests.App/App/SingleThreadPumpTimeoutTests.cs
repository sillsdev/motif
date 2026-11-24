using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class SingleThreadPumpTimeoutTests
{
    [Fact]
    public async Task ABodyThatNeverCompletesFailsWithATimeoutNamingTheTest()
    {
        var never = new TaskCompletionSource();

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            SingleThreadPump.RunAsync(() => never.Task, "SomeTests.NeverEnds", TimeSpan.FromMilliseconds(200)));

        Assert.Contains("SomeTests.NeverEnds", failure.Message);
        Assert.Contains("did not finish", failure.Message);
    }

    [Fact]
    public async Task ABodyThatFinishesInTimeReturnsItsResult()
    {
        var result = await SingleThreadPump.RunAsync(async () =>
        {
            await Task.Yield();
            return 7;
        }, "SomeTests.Finishes", TimeSpan.FromSeconds(30));

        Assert.Equal(7, result);
    }
}
