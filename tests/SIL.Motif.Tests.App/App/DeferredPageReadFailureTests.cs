using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DeferredPageReadFailureTests
{
    [Fact]
    public void NavigationAwaitingADeferredInitialPageReceivesTheCurrentReadFailure()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            Dispatcher.UIThread.RunJobs();
            var source = new ControlledPageSource();
            var (window, _, controls) = CreateWindow(source);
            var deferred = controls[^1];
            Task? initial = null;
            Task? navigation = null;
            Task? current = null;
            try
            {
                StartRead(window, deferred, source, out initial, out navigation, out current);
                var expected = new InvalidOperationException("Current initial page could not be read.");
                source.FailCurrentRead(expected);

                var currentFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => current!);
                Assert.Same(expected, currentFailure);
                var navigationFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => navigation!);
                Assert.Same(expected, navigationFailure);
                Assert.Empty(deferred.Items);
            }
            finally
            {
                window.Close();
                source.CancelPendingRead();
                Dispatcher.UIThread.RunJobs();
                await DrainAsync(controls.Select(control => control.PageRefresh)
                    .Concat(new[] { initial, navigation, current, source.PendingRead }));
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void NavigationAwaitingADeferredInitialPageReceivesTheCurrentReadCancellation()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            Dispatcher.UIThread.RunJobs();
            using var cancellation = new CancellationTokenSource();
            var source = new ControlledPageSource();
            var (window, _, controls) = CreateWindow(source);
            var deferred = controls[^1];
            Task? initial = null;
            Task? navigation = null;
            Task? current = null;
            try
            {
                StartRead(window, deferred, source, out initial, out navigation, out current);
                Assert.False(source.ReadCancellation.IsCancellationRequested);
                cancellation.Cancel();
                source.CancelCurrentRead(cancellation.Token);

                var currentCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => current!);
                Assert.Equal(cancellation.Token, currentCancellation.CancellationToken);
                var initialCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial!);
                Assert.Equal(cancellation.Token, initialCancellation.CancellationToken);
                var navigationCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation!);
                Assert.Equal(cancellation.Token, navigationCancellation.CancellationToken);
                Assert.True(source.PendingRead!.IsCanceled);
                Assert.True(current!.IsCanceled);
                Assert.True(initial!.IsCanceled);
                Assert.True(navigation!.IsCanceled);
                Assert.False(source.ReadCancellation.IsCancellationRequested);
                Assert.Empty(deferred.Items);
            }
            finally
            {
                window.Close();
                source.CancelPendingRead();
                Dispatcher.UIThread.RunJobs();
                await DrainAsync(controls.Select(control => control.PageRefresh)
                    .Concat(new[] { initial, navigation, current, source.PendingRead }));
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupersedingAStartedDeferredReadCancelsItsProducerAndReleasesItsWaiters(bool detach)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            Dispatcher.UIThread.RunJobs();
            var source = new ControlledPageSource();
            var (window, panel, controls) = CreateWindow(source);
            var deferred = controls[^1];
            Task? initial = null;
            Task? navigation = null;
            Task? current = null;
            try
            {
                StartRead(window, deferred, source, out initial, out navigation, out current);
                if (detach) panel.Children.Remove(deferred);
                else deferred.FullItemsSource = new[] { "replacement" };
                Assert.True(source.ReadCancellation.IsCancellationRequested);

                await current;
                await initial;
                await navigation;
                Assert.True(source.PendingRead!.IsCanceled);
                Assert.True(current.IsCompletedSuccessfully);
                Assert.True(initial.IsCompletedSuccessfully);
                Assert.True(navigation.IsCompletedSuccessfully);
                Assert.Equal(1, source.HeldReads);
                if (detach)
                {
                    Assert.DoesNotContain(deferred, panel.Children);
                    Assert.Empty(deferred.Items);
                }
                else
                {
                    Assert.Equal("replacement", Assert.Single(deferred.Items));
                    Assert.DoesNotContain("item-0", deferred.Items);
                    var replacementNavigation = deferred.ShowSourceIndexAsync(0);
                    Assert.True(replacementNavigation.IsCompletedSuccessfully);
                    replacementNavigation.GetAwaiter().GetResult();
                }
            }
            finally
            {
                window.Close();
                source.CancelPendingRead();
                Dispatcher.UIThread.RunJobs();
                await DrainAsync(controls.Select(control => control.PageRefresh)
                    .Concat(new[] { initial, navigation, current, source.PendingRead }));
            }
        }, TimeSpan.FromSeconds(30));
    }

    private static (MotifTestWindow Window, StackPanel Panel, ProgressiveItemsControl[] Controls)
        CreateWindow(ControlledPageSource source)
    {
        var controls = Enumerable.Range(0, 8).Select(_ => new ProgressiveItemsControl
        {
            DefersFirstPage = true,
            ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text }),
            FullItemsSource = new[] { "initial" },
        }).ToArray();
        var panel = new StackPanel();
        foreach (var control in controls) panel.Children.Add(control);
        controls[^1].FullItemsSource = source;
        source.Arm();
        return (new MotifTestWindow { Width = 800, Height = 600, Content = panel }, panel, controls);
    }

    private static void StartRead(
        MotifTestWindow window, ProgressiveItemsControl deferred, ControlledPageSource source,
        out Task initial, out Task navigation, out Task current)
    {
        initial = navigation = current = Task.CompletedTask;
        window.Show();
        initial = deferred.PageRefresh;
        Assert.False(initial.IsCompleted);
        Assert.Equal(0, source.HeldReads);
        navigation = deferred.ShowSourceIndexAsync(0);
        Assert.False(navigation.IsCompleted);
        Assert.Same(initial, deferred.PageRefresh);
        Assert.Equal(0, source.HeldReads);

        Dispatcher.UIThread.RunJobs();
        current = deferred.PageRefresh;
        Assert.NotSame(initial, current);
        Assert.Equal(1, source.HeldReads);
        Assert.False(current.IsCompleted);
        Assert.False(navigation.IsCompleted);
    }

    private static Task DrainAsync(IEnumerable<Task?> tasks) => Task.WhenAll(tasks
        .OfType<Task>().Distinct().Select(task => task.ContinueWith(completed =>
        {
            _ = completed.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)));

    private sealed class ControlledPageSource : IProgressivePageSource
    {
        private readonly TaskCompletionSource<IReadOnlyList<object>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _armed;
        public int Count => 40;
        public int InitialOffset => 0;
        public int HeldReads { get; private set; }
        public Task? PendingRead { get; private set; }
        public CancellationToken ReadCancellation { get; private set; }
        public int IndexOf(object item) => -1;
        public void Arm() => _armed = true;
        public void FailCurrentRead(Exception failure) => _completion.SetException(failure);
        public void CancelCurrentRead(CancellationToken cancellationToken) =>
            _completion.SetCanceled(cancellationToken);
        public void CancelPendingRead() => _completion.TrySetCanceled();
        public void ReleasePage() => CancelPendingRead();

        public Task<IReadOnlyList<object>> ReadPageAsync(int offset, int count, CancellationToken cancellationToken)
        {
            if (!_armed)
                return Task.FromResult<IReadOnlyList<object>>(Enumerable.Range(offset, count)
                    .Select(index => (object)("item-" + index)).ToArray());
            HeldReads++;
            ReadCancellation = cancellationToken;
            var pending = ReadHeldPageAsync(cancellationToken);
            PendingRead = pending;
            return pending;
        }

        private async Task<IReadOnlyList<object>> ReadHeldPageAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() =>
                _completion.TrySetCanceled(cancellationToken));
            return await _completion.Task.ConfigureAwait(false);
        }
    }
}
