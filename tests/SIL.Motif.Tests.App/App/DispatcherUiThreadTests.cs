using Avalonia.Threading;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A view model that posts to <c>Dispatcher.UIThread</c> from off the UI thread, as <c>HealthStripViewModel</c>
/// does, must not meet a dispatcher that is still being built.
/// </summary>
/// <remarks>
/// Avalonia's dispatcher is created on first touch and publishes itself as <c>UIThread</c> before its platform
/// implementation is set, and the headless platform replaces that implementation again when it starts. A
/// <c>Post</c> from another thread inside either window threw a NullReferenceException in
/// <c>RequestForegroundProcessing</c>, once on CI in
/// <c>RecentProjectRowTests.CopiesOfTheOpenProjectAreExcludedWithoutABaselineToken</c>, whose continuation ran on
/// a pool thread while a parallel test started the platform. Every test now starts the platform first.
/// </remarks>
public sealed class DispatcherUiThreadTests
{
    [Fact]
    public void TheHeadlessPlatformIsAlreadyRunningWhenATestStarts()
    {
        Assert.True(AvaloniaHeadlessPlatform.IsCreated);
        Assert.False(Dispatcher.UIThread.CheckAccess());
        AvaloniaHeadlessPlatform.Invoke(() => Assert.True(Dispatcher.UIThread.CheckAccess()));
    }

    [Fact]
    public void ManyThreadsPostingTogetherNeverMeetAHalfBuiltDispatcher()
    {
        const int threads = 16;
        using var start = new Barrier(threads);
        var failures = new List<Exception>();
        var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            try
            {
                start.SignalAndWait();
                Dispatcher.UIThread.Post(static () => { });
            }
            catch (Exception exception)
            {
                lock (failures) failures.Add(exception);
            }
        })).ToList();
        foreach (var worker in workers) worker.Start();
        foreach (var worker in workers) worker.Join();

        Assert.Empty(failures);
    }
}
