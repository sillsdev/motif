using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class CaptureStatePreservationTests
{
    [Fact]
    public void RawSavePreservesTheRenderedViewportThemeAndZoom()
    {
        WithCaptureFolder(folder => AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var app = Application.Current!;
            var window = new MainWindow
            {
                Width = 900,
                Height = 700,
                Content = new LayoutTransformControl
                {
                    LayoutTransform = WindowZoomPolicy.Transform,
                    Child = new TextBlock { Text = "The current view must survive saving." },
                },
            };
            window.Show();
            try
            {
                app.RequestedThemeVariant = ThemeVariant.Dark;
                WindowZoomPolicy.Apply(app, 150);
                PageScreenshots.Settle(window);
                var originalSize = window.ClientSize;
                using (var before = window.CaptureRenderedFrame())
                {
                    Assert.NotNull(before);
                    before.Save(Path.Combine(folder, "raw-before.png"), PngBitmapEncoderOptions.Default);
                }
                var viewportChanges = 0;
                window.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Window.WidthProperty || change.Property == Window.HeightProperty)
                        viewportChanges++;
                };
                PageScreenshots.Save(window, Path.Combine(folder, "raw.png"));

                Assert.Equal(0, viewportChanges);
                Assert.Equal(originalSize, window.ClientSize);
                Assert.Same(ThemeVariant.Dark, app.RequestedThemeVariant);
                Assert.Equal(1.5, WindowZoomPolicy.Transform.ScaleX);
                Assert.Equal(1.5, WindowZoomPolicy.Transform.ScaleY);
                Assert.True(File.Exists(Path.Combine(folder, "raw.png")));
                Assert.Equal(File.ReadAllBytes(Path.Combine(folder, "raw-before.png")),
                    File.ReadAllBytes(Path.Combine(folder, "raw.png")));
            }
            finally
            {
                WindowZoomPolicy.Apply(app, ZoomPolicy.DefaultPercent);
                app.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void RoutedSaveDoesNotResizeThePageBeforeWritingItsImage()
    {
        WithCaptureFolder(folder => AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 1240;
                window.Height = 900;
                PageScreenshots.Settle(window);
                var originalSize = window.ClientSize;
                MatrixCaptureState.SaveFrame(window, workspace, WorkspacePage.Overview, null,
                    Path.Combine(folder, "routed-before.png"));
                var viewportChanges = 0;
                window.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Window.WidthProperty || change.Property == Window.HeightProperty)
                        viewportChanges++;
                };

                PageScreenshots.SavePageFrame(window, workspace, WorkspacePage.Overview, null,
                    Path.Combine(folder, "routed.png"));

                Assert.Equal(0, viewportChanges);
                Assert.Equal(originalSize, window.ClientSize);
                MatrixCaptureState.Assert(window, workspace, WorkspacePage.Overview, null);
                Assert.True(File.Exists(Path.Combine(folder, "routed.png")));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void SavingStillRejectsClippedTextBeforeWritingAnImage()
    {
        WithCaptureFolder(folder => AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var window = new MainWindow
            {
                Width = 900,
                Height = 700,
                Content = new TextBlock { Text = "This text cannot fit.", Width = 4, Height = 1 },
            };
            window.Show();
            try
            {
                var path = Path.Combine(folder, "clipped.png");
                var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => PageScreenshots.Save(window, path));
                Assert.Contains("This text cannot fit.", failure.Message);
                Assert.False(File.Exists(path));
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30)));
    }

    private static void WithCaptureFolder(Action<string> capture)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
        var temporary = string.IsNullOrWhiteSpace(folder);
        folder = temporary ? Path.Combine(Path.GetTempPath(), $"motif-capture-state-{Guid.NewGuid():N}")
            : Path.Combine(folder!, "capture-state-preservation");
        Directory.CreateDirectory(folder!);
        try { capture(folder!); }
        finally { if (temporary) Directory.Delete(folder!, recursive: true); }
    }
}
