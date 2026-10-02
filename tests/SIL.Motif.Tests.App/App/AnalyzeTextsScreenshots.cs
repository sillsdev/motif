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
    public void CaptureWhyCardEvidenceStates()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure:
                (fake, assessment) => AnalyzeTextsLayoutTests.ConfigureStoredExplanation(fake, assessment));
            try
            {
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                workspace.CurrentPage = WorkspacePage.Texts;
                window.Height = 1500;
                AnalyzeTextsLayoutTests.Settle(window);
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                await inText.WarningEvidenceRefresh;
                var words = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens).ToArray();
                var warned = words.Single(token => token.Form == "alikula");
                var notNamed = words.Single(token => token.Form == "hawajafika");

                await inText.OpenTokenCardAsync(warned);
                CaptureStates(window, folder, "named-warning-card");

                await inText.OpenTokenCardAsync(notNamed);
                CaptureStates(window, folder, "no-named-warning-card");

                inText.Filter = ResultsInTextFilter.NamedInWarning;
                await inText.OpenTokenCardAsync(warned);
                CaptureStates(window, folder, "named-warning-filter");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

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

    private static void CaptureStates(MainWindow window, string folder, string scene)
    {
        window.Height = 1500;
        foreach (var (theme, variant) in Themes())
        {
            Application.Current!.RequestedThemeVariant = variant;
            foreach (var width in new[] { 1040, 1240 })
                Save(window, width, Path.Combine(folder, $"{scene}-{width}-{theme}.png"));
        }
    }

    private static void Save(MainWindow window, int width, string path)
    {
        window.Width = width;
        AnalyzeTextsLayoutTests.Settle(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}
