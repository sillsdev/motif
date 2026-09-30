using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves the Analyze texts states the page captures cannot reach, because they switch pages and so close the word
/// card: the text before the first parse, a word card open under its line, and a staged addition. Like
/// <see cref="PageScreenshots"/>, it runs only when <c>MOTIF_SCREENSHOTS</c> names a folder.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AnalyzeTextsScreenshots
{
    [ScreenshotFact]
    public void CaptureAnalyzeTextsStates()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, before) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(parse: false);
            try
            {
                foreach (var (theme, variant) in Themes())
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    Save(before, 1240, Path.Combine(folder, $"09-empty-parse-analyze-1240-{theme}.png"));
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                before.Close();
            }

            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var hawajafika = inText.VisibleLines[1].Tokens.Single(token => token.Form == "hawajafika");
                foreach (var (theme, variant) in Themes())
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    foreach (var width in new[] { 1040, 1240 })
                    {
                        inText.CloseTokenCard();
                        await inText.OpenTokenCardAsync(hawajafika);
                        Save(window, width, Path.Combine(folder, $"10-word-card-{width}-{theme}.png"));
                    }
                }

                inText.CloseTokenCard();
                var chakula = inText.VisibleLines[0].Tokens.Single(token => token.Form == "chakula");
                var add = chakula.Marking.FixChoices.Single(choice => choice.Label == "Add as Approved");
                await chakula.StageMarkingChoiceForTokenCommand!.ExecuteAsync(add);
                foreach (var (theme, variant) in Themes())
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    Save(window, 1240, Path.Combine(folder, $"11-staged-add-1240-{theme}.png"));
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static (string Theme, ThemeVariant Variant)[] Themes() =>
        [("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark)];

    private static void Save(MainWindow window, int width, string path)
    {
        window.Width = width;
        AnalyzeTextsLayoutTests.Settle(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}
