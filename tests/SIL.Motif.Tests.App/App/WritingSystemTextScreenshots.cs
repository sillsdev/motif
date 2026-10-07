using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class WritingSystemTextScreenshots
{
    [ScreenshotFact]
    public void CapturesRightToLeftTextCardWordListReviewAndMissingFontLine()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        Walkthrough.WalkthroughFonts.Register();

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (fake, _) => fake.ListTextWordsCompletesWith(WritingSystemTestData.ArabicText()));
            try
            {
                window.SetValue(TextElement.FontFamilyProperty, new FontFamily("fonts:MotifWalkthrough#Andika"));
                window.Height = 1500;
                var resolver = workspace.Context.TextStyles;

                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                var wordsView = workspace.PageModel<TextsPageModel>().Words;
                var texts = workspace.PageModel<TextsPageModel>();
                texts.Tab = TextsTab.AnalyzeTexts;
                texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.TextReader);
                workspace.CurrentPage = WorkspacePage.Texts;
                await wordsView.SetProjectAsync(PageScreenshots.SampleProjectPath);
                resolver.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
                resolver.ReplaceContext([WritingSystemTestData.Arabic]);
                PageScreenshots.Settle(window);
                Assert.Equal(WritingSystemTestData.Arabic.FontFamily,
                    resolver.Resolve(TextStyleRequest.Linguistic(WritingSystemTestData.Arabic.Id, "Normal"))
                        .RequestedFontFamily);
                var languageForm = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == WritingSystemTestData.Form && text.Classes.Contains("stripWord"));
                Assert.True(languageForm.IsEffectivelyVisible);
                Assert.Equal(WritingSystemTestData.Arabic.Id,
                    SIL.Motif.App.Controls.WritingSystemText.GetId(languageForm));
                Assert.Same(resolver, SIL.Motif.App.Controls.WritingSystemText.GetResolver(languageForm));
                Assert.Equal("Normal", SIL.Motif.App.Controls.WritingSystemText.GetStyleName(languageForm));
                Assert.Contains(languageForm.GetVisualAncestors(), ancestor => ancestor is TopLevel);
                Assert.Contains(WritingSystemTestData.Form, wordsView.Response!.Texts
                    .SelectMany(text => text.Lines).SelectMany(line => line.Tokens).Select(token => token.Form));
                Assert.Single(resolver.MissingFontNotices);
                var visible = texts.ResultsInText.VisibleLines.SelectMany(line => line.Tokens).ToArray();
                Assert.Contains(visible, item => item.Form == WritingSystemTestData.Form);
                var token = visible.Single(item => item.Form == WritingSystemTestData.Form);
                foreach (var (theme, variant) in Themes())
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    Capture(window, folder, "rtl-analyze-texts", theme);
                    Capture(window, folder, "missing-font-line", theme);
                }

                await texts.ResultsInText.OpenTokenCardAsync(token);
                foreach (var (theme, variant) in Themes())
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    Capture(window, folder, "rtl-word-card", theme);
                }

                texts.ResultsInText.CloseTokenCard();
                texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
                foreach (var (theme, variant) in Themes())
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    Capture(window, folder, "rtl-word-list", theme);
                }

                fake.PendingChangesIs(WritingSystemTestData.ArabicPendingChange());
                await workspace.Context.Changes.ReloadAsync();
                workspace.CurrentPage = WorkspacePage.Review;
                foreach (var (theme, variant) in Themes())
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    Capture(window, folder, "rtl-review", theme);
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

    private static void Capture(MainWindow window, string folder, string name, string theme)
    {
        foreach (var width in new[] { 1040, 1240 })
        {
            window.Width = width;
            PageScreenshots.Settle(window);
            LayoutAssertions.BeforeCapture(window);
            var path = Path.Combine(folder, $"{name}-{width}-{theme}.png");
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }
    }
}
