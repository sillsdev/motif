using Avalonia;
using Avalonia.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WindowBoundsPreferenceTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void ClosingCapturesPlacementForBackgroundPreferenceReplay()
    {
        var folder = Directory.CreateTempSubdirectory("motif-window-bounds-").FullName;
        try
        {
            avalonia.Invoke(() =>
            {
                var path = Path.Combine(folder, "user-preferences.json");
                using var store = new FileUserPreferencesStore(path, TimeSpan.FromMinutes(5));
                var window = new MainWindow(rememberBounds: true, uriLauncher: null, preferencesStore: store);
                EventHandler<WindowClosingEventArgs> cancelClose = (_, e) => e.Cancel = true;
                window.Closing += cancelClose;
                try
                {
                    window.Show();
                    window.Width = 1120;
                    window.Height = 760;
                    window.Position = new PixelPoint(120, 80);
                    window.Close();

                    var expected = new WindowPlacement(1120, 760, 120, 80);
                    Assert.Equal(expected, store.Current.Window);
                    Assert.Equal(0, store.WriteCount);
                    window.Width = 1280;
                    window.Height = 900;
                    window.Position = new PixelPoint(240, 160);
                    Exception? failure = null;
                    var writer = new Thread(() => failure = Record.Exception(store.Flush)) { IsBackground = true };
                    writer.Start();
                    Assert.True(writer.Join(TimeSpan.FromSeconds(10)));

                    Assert.Null(failure);
                    Assert.Equal(PreferenceState.Ready, store.State);
                    Assert.Null(store.Problem);
                    Assert.Equal(expected, store.Current.Window);
                    Assert.Equal(expected, new FileUserPreferencesStore(path).Current.Window);
                    Assert.Equal(1, store.WriteCount);
                }
                finally
                {
                    window.Closing -= cancelClose;
                    window.Close();
                }
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
