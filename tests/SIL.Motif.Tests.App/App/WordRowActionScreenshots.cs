using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

/// <summary>Records the shared word row before hover, focus or selection at four viewport sizes.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class WordRowActionScreenshots(AvaloniaHeadlessFixture avalonia)
{
    [ScreenshotFact]
    public void CaptureClosedWordRowAtFourSizes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        avalonia.Invoke(() =>
        {
            var key = new WordPresentationKey("actions:kitabu");
            var row = new WordRow
            {
                Data = new WordPresentation(key, 0, WordRowViewModel.NotParsed("kitabu"), WordListOwner.ReadOnly),
                State = new WordInteractionState(key),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            var window = new MotifTestWindow { Content = row };
            try
            {
                window.Show();
                window.MouseMove(new Point(-10, -10));
                foreach (var (name, width, height) in new[]
                         { ("thin", 640, 600), ("half", 960, 780), ("full", 1920, 1080), ("short", 960, 600) })
                {
                    window.Width = width;
                    window.Height = height;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException($"No frame rendered for {name}.");
                    frame.Save(Path.Combine(folder, $"word-actions-{name}.png"), PngBitmapEncoderOptions.Default);
                }
                LayoutAssertions.BeforeCapture(window);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
