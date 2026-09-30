using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves the two states the every-page capture cannot reach where contrast was measured: setup's last step, with
/// its solid primary button, and a Matrix holding every kind of cell, both zero and counted.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ContrastScreenshots
{
    [ScreenshotFact]
    public void CaptureSetupAndEveryKindOfMatrixCell()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, leaveSetupOpen: true);
            try
            {
                window.Width = 1240;
                window.Height = 780;
                var setup = workspace.Context.Setup!;
                foreach (var text in workspace.Selection.Texts) text.IsChecked = true;
                for (var step = 0; step < 3; step++) setup.NextCommand.Execute(null);
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    PageScreenshots.Save(window, Path.Combine(folder, $"08-setup-wizard-step4-1240-{theme}.png"));
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }

            foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
            {
                var compare = MatrixListsWindowWordsTests.Compare(MatrixListsWindowWordsTests.EveryKindOfWord);
                var matrix = new Window
                {
                    Content = new ComparePanel(compare), RequestedThemeVariant = variant, Width = 1100, Height = 700,
                };
                try
                {
                    matrix.Show();
                    PageScreenshots.Settle(matrix);
                    using var frame = matrix.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("No frame rendered for the Matrix.");
                    frame.Save(Path.Combine(folder, $"15-matrix-every-cell-1100-{theme}.png"), PngBitmapEncoderOptions.Default);
                }
                finally
                {
                    matrix.Close();
                }
            }
        }, TimeSpan.FromMinutes(2));
    }
}
