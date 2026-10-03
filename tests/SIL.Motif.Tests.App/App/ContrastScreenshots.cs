using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves the two states the every-page capture cannot reach where contrast was measured: setup's last step, with
/// its solid primary button, and a Matrix holding every kind of cell, both zero and counted.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ContrastScreenshots
{
    [Fact]
    public void TheSetupCaptureShowsTheSamplesNumbersBehindTheWizard()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenSetupAtItsLastStep();
            try
            {
                RequireTheSampleBehindSetup(workspace);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [ScreenshotFact]
    public void CaptureSetupAndEveryKindOfMatrixCell()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenSetupAtItsLastStep();
            try
            {
                window.Width = 1240;
                window.Height = 780;
                RequireTheSampleBehindSetup(workspace);
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
                    LayoutAssertions.BeforeCapture(matrix);
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

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenSetupAtItsLastStep()
    {
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(
            parse: false, leaveSetupOpen: true, configure: (fake, _) => fake.OverviewCompletesWith(BeforeTheFirstParse()));
        var setup = workspace.Context.Setup!;
        foreach (var text in workspace.Selection.Texts) text.IsChecked = true;
        await workspace.PageModel<TextsPageModel>().Words.ReloadAsync();
        for (var step = 0; step < 3; step++) setup.NextCommand.Execute(null);
        return (workspace, window);
    }

    /// <summary>A capture of setup must show the sample's numbers, not a refused read or zero occurrences.</summary>
    private static void RequireTheSampleBehindSetup(WorkspaceShellViewModel workspace)
    {
        var overview = workspace.PageModel<OverviewPageModel>();
        OverviewPageWordsTests.AssertCaptureStopCounts(overview.Overview!, "setup wizard");
        Assert.False(overview.HasOverviewRefusal, overview.OverviewRefusalLine);
        Assert.True(overview.ShowNumbers);
        var words = workspace.PageModel<TextsPageModel>().Words;
        Assert.True(words.OccurrenceCount > 0, words.SummaryText);
        Assert.Equal(words.SummaryText, workspace.Context.Setup!.RunSummary);
    }

    // The sample's own counts, read before anything is parsed, so the page behind setup matches the wizard's line.
    private static OverviewResponse BeforeTheFirstParse()
    {
        var populated = OverviewPageWordsTests.Populated();
        return populated with
        {
            SelectionWordCount = 9,
            TextOccurrenceCount = 9,
            AssessmentId = null,
            AssessedUtc = null,
            AssessmentElapsedSeconds = null,
            WordCoveragePercent = null,
            TextCoverage = new OverviewTextCoverage(0, 0, 0, 0, 9, 0),
            Accuracy = new OverviewAccuracy(0, 9, 0, 0, 0, 0, 0, 0),
            Timing = new OverviewTiming(null, null, [], 0),
            LookFirst = OverviewLookFirst.Empty,
        };
    }
}
