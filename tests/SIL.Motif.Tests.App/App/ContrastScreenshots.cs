using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves setup's last step, with its solid primary button, for contrast inspection.
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
    public void CaptureSetup()
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


        }, TimeSpan.FromMinutes(2));
    }

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenSetupAtItsLastStep()
    {
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(
            parse: false, leaveSetupOpen: true, configure: (fake, _) => fake.OverviewCompletesWith(BeforeTheFirstParse()));
        var setup = workspace.Context.Setup!;
        foreach (var text in workspace.Selection.Texts) text.IsChecked = true;
        await workspace.Context.EvidencePublication;
        for (var step = 0; step < 3; step++) setup.NextCommand.Execute(null);
        await workspace.Context.EvidencePublication;
        return (workspace, window);
    }

    /// <summary>A capture of setup must show the sample's numbers, not a refused read or zero occurrences.</summary>
    private static void RequireTheSampleBehindSetup(WorkspaceShellViewModel workspace)
    {
        var overview = Assert.IsType<SIL.Motif.Contract.Responses.OverviewResponse>(workspace.Context.Health.Overview);
        SampleOverview.AssertCaptureStopCounts(overview, "setup wizard");
        Assert.False(overview.IsStale);
        var words = workspace.PageModel<TextsPageModel>().Words;
        Assert.True(words.OccurrenceCount > 0, words.SummaryText);
        Assert.Equal(words.SummaryText, workspace.Context.Setup!.RunSummary);
    }

    // The sample's own counts, read before anything is parsed, so the page behind setup matches the wizard's line.
    private static OverviewResponse BeforeTheFirstParse()
    {
        var populated = SampleOverview.Populated();
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
