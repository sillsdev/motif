using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DeferredFirstPageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredSynchronousPagesFinishAndAllowSourceNavigation(bool useArray)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var (window, _, controls) = CreateWindow(useArray);
            try
            {
                window.Show();
                var deferred = controls[^1];
                var pending = deferred.PageRefresh;
                Assert.False(pending.IsCompleted);
                Dispatcher.UIThread.RunJobs();
                var producer = deferred.PageRefresh;
                Assert.NotSame(pending, producer);
                Assert.True(producer.IsCompletedSuccessfully);
                Assert.True(pending.IsCompletedSuccessfully);
                pending.GetAwaiter().GetResult();
                var forward = deferred.ShowSourceIndexAsync(25);
                Assert.True(forward.IsCompletedSuccessfully);
                forward.GetAwaiter().GetResult();
                Assert.Contains("item-25", deferred.Items);
                Assert.DoesNotContain("item-0", deferred.Items);
                var backward = deferred.ShowSourceIndexAsync(0);
                Assert.True(backward.IsCompletedSuccessfully);
                backward.GetAwaiter().GetResult();
                Assert.Contains("item-0", deferred.Items);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    // One item per control, so the six-item first-page budget runs out after six controls.
    [Fact]
    public void DetachingOrReplacingADeferredSourceCompletesTheSupersededWaiters()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var (window, panel, controls) = CreateWindow(useArray: true, itemCount: 1);
            try
            {
                window.Show();
                var replaced = controls[^2];
                var detached = controls[^1];
                var superseded = replaced.PageRefresh;
                var canceled = detached.PageRefresh;
                Assert.False(superseded.IsCompleted);
                Assert.False(canceled.IsCompleted);
                replaced.FullItemsSource = new[] { "replacement" };
                var replacement = replaced.PageRefresh;
                Assert.NotSame(superseded, replacement);
                Assert.False(replacement.IsCompleted);
                panel.Children.Remove(detached);

                Dispatcher.UIThread.RunJobs();
                Assert.NotSame(replacement, replaced.PageRefresh);
                Assert.True(replaced.PageRefresh.IsCompletedSuccessfully);
                Assert.True(superseded.IsCompletedSuccessfully);
                Assert.True(canceled.IsCompletedSuccessfully);
                Assert.True(replacement.IsCompletedSuccessfully);
                Task.WhenAll(superseded, canceled, replacement).GetAwaiter().GetResult();
                Assert.Equal("replacement", Assert.Single(replaced.Items));
                Assert.DoesNotContain("item-0", replaced.Items);
                var navigation = replaced.ShowSourceIndexAsync(0);
                Assert.True(navigation.IsCompletedSuccessfully);
                navigation.GetAwaiter().GetResult();
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    private static (MotifTestWindow Window, StackPanel Panel, ProgressiveItemsControl[] Controls)
        CreateWindow(bool useArray, int itemCount = 40)
    {
        var panel = new StackPanel();
        var controls = Enumerable.Range(0, 8).Select(controlIndex => new ProgressiveItemsControl
        {
            DefersFirstPage = true,
            ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text }),
            FullItemsSource = useArray
                ? Enumerable.Range(0, itemCount).Select(index => "item-" + index).ToArray()
                : Enumerable.Range(0, itemCount).Select(index => "item-" + index).ToList(),
        }).ToArray();
        foreach (var control in controls) panel.Children.Add(control);
        return (new MotifTestWindow { Width = 800, Height = 600, Content = panel }, panel, controls);
    }
}
