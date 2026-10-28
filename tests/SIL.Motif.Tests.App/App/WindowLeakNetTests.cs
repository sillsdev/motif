using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>The window-leak net finds a window a test left open, and a closed window is collectable.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WindowLeakNetTests
{
    [Fact]
    public void TheNetFindsAndClosesAWindowConstructedAndNeverClosed() => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var before = WindowLeakNetAttribute.OpenWindows().ToHashSet();
        var leaked = new MotifTestWindow();

        Assert.Same(leaked, Assert.Single(WindowLeakNetAttribute.CloseLeaked(before)));
        Assert.Empty(WindowLeakNetAttribute.CloseLeaked(before));
    });

    [Fact]
    public void TheNetLeavesAWindowItWasToldToKeepAndOneThatPredatesTheTest() => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var predating = new MotifTestWindow();
        var before = WindowLeakNetAttribute.OpenWindows().ToHashSet();
        var kept = WindowLeakNetAttribute.Keep(new MotifTestWindow());

        Assert.Empty(WindowLeakNetAttribute.CloseLeaked(before));
        Assert.Contains(kept, WindowLeakNetAttribute.OpenWindows());
        kept.Close();
        predating.Close();
    });

    [Fact]
    public void AClosedMainWindowIsCollectableAfterAFullCollection() => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var reference = ShowAndClose();
        for (var round = 0; round < 3 && reference.IsAlive; round++)
        {
            // The headless compositor holds its last batch, and so the last window, until the next render commit.
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(reference.IsAlive, "A closed MainWindow is still reachable after a full collection.");
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowAndClose()
    {
        var (_, window) = FakeComposedWindow.Create();
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.Close();
        return new WeakReference(window);
    }
}
