using Avalonia;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves Overview and Timing over the shared sample's recorded times, and Try a Word beside its earlier timing.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewTimingScreenshots
{
    internal const string SubjectAgreementRuleKey = "c3f310a1-4c53-ff36-88e6-531c157983e6";

    [ScreenshotFact]
    public void CapturePopulatedOverviewAndTiming()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: ReadOverviewAndTiming);
            try
            {
                workspace.Context.TryWord("hawajafika");
                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                Assert.True(workspace.PageModel<TryWordPageModel>().HasEarlierTiming);
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    foreach (var width in new[] { 1040, 1240 })
                    {
                        window.Width = width;
                        window.Height = 780;
                        foreach (var (name, page) in new[]
                                 {
                                     ("14-overview-populated", WorkspacePage.Overview),
                                     ("16-timing-filled", WorkspacePage.Timing),
                                     ("17-try-a-word-earlier-timing", WorkspacePage.TryAWord),
                                 })
                        {
                            workspace.CurrentPage = page;
                            PageScreenshots.Save(window, Path.Combine(folder, $"{name}-{width}-{theme}.png"));
                        }
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    /// <summary>Gives the sample data a read Overview and stored parse times.</summary>
    internal static void ReadOverviewAndTiming(FakeCommandClient fake, AssessCommandResponse assessment)
    {
        var overview = SampleEvidence.Overview(assessment);
        OverviewPageWordsTests.AssertCaptureStopCounts(overview, "overview and timing");
        fake.OverviewCompletesWith(overview);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            SampleEvidence.Timing(assessment, request.By, request.Rule, request.ExplicitWords))));
        fake.AssessCompletesWith(assessment with
        {
            Measurements = [new ProducedAssessmentReference("assessment/one", "ParseTime", "assessment/one")],
        });
    }

}
