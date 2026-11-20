using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Walks the Open Record File window through its states and saves each as an image, in both themes.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ImportRecordWindowScreenshots
{
    [ScreenshotTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOpenRecordFileWalkthroughCapturesEveryState(bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var prior = Application.Current!.RequestedThemeVariant;
            var (model, _, _, _) = ImportRecordDialogModelTests.Make((request, _) =>
                request.FilePath == "old.md"
                    ? ImportRecordDialogModelTests.Refused(RefusalCodes.RecordImportPlanRefused)
                    : ImportRecordDialogModelTests.Ok(ImportRecordDialogModelTests.Plan(
                        operations: 5, refusals: 4, draft: request.DraftName, page: request.Page, pageCount: 2)));
            var window = new ImportRecordWindow(model);
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Show();
                var theme = dark ? "dark" : "light";
                Save(window, theme, "1-idle");

                model.Import.FilePath = "old.md";
                await model.Import.PreviewAsync();
                Save(window, theme, "2-whole-file-refused");

                model.Import.FilePath = "edited.md";
                await model.Import.PreviewAsync();
                Save(window, theme, "3-previewed");

                await model.Import.NextPageCommand.ExecuteAsync(null);
                Save(window, theme, "4-second-page");

                model.Import.DraftName = "From edited file";
                Save(window, theme, "5-named");
            }
            finally
            {
                Application.Current.RequestedThemeVariant = prior;
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void Save(ImportRecordWindow window, string theme, string state)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        PageScreenshots.Settle(window);
        LayoutAssertions.BeforeCapture(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame for {state}.");
        frame.Save(Path.Combine(folder, $"import-record-{state}-{theme}.png"), PngBitmapEncoderOptions.Default);
    }
}
