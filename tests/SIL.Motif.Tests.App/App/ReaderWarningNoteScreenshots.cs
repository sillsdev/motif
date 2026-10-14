using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReaderWarningNoteScreenshots
{
    [ScreenshotTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrolledWarningNoteFitsAndTheFormerNoWrapStateReproducesItsOverflow(bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            var settings = SettingsTestAccess.Model(window);
            var priorTheme = Application.Current!.RequestedThemeVariant;
            CopyableTextBlock? note = null;
            var originalWrapping = TextWrapping.NoWrap;
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = 640;
                window.Height = 600;
                settings.ZoomPercent = 150;
                PageScreenshots.Settle(window);
                note = Assert.Single(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "No stored warning names this word.");
                originalWrapping = note.TextWrapping;
                note.BringIntoView();
                PageScreenshots.Settle(window);
                AssertInsideWindow(window, note);
                LayoutAssertions.AssertCurrent(window);
                Save(window, dark, "after");

                note.TextWrapping = TextWrapping.NoWrap;
                PageScreenshots.Settle(window);
                note.BringIntoView();
                PageScreenshots.Settle(window);
                AssertInsideWindow(window, note);
                Save(window, dark, "baseline-reproduced");
                var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutAssertions.AssertCurrent(window));
                Assert.Contains("No stored warning names this word.", failure.Message);
                Assert.Contains("horizontal", failure.Message);
            }
            finally
            {
                if (note is not null) note.TextWrapping = originalWrapping;
                settings.ZoomPercent = ZoomPolicy.DefaultPercent;
                Application.Current.RequestedThemeVariant = priorTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void AssertInsideWindow(Window window, Control control)
    {
        var first = control.TranslatePoint(default, window)!.Value;
        var last = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(first, last)),
            $"The warning note is outside the actual client: {first} to {last}.");
    }

    private static void Save(Window window, bool dark, string state)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No note frame rendered.");
        frame.Save(Path.Combine(folder, $"reader-warning-note-{state}-640-600-150-{(dark ? "dark" : "light")}.png"),
            PngBitmapEncoderOptions.Default);
    }
}
