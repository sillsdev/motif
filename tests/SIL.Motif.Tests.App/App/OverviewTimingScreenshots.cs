using Avalonia;
using Avalonia.Styling;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves project health and diagnostics over the shared sample's recorded times.
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
                workspace.Context.UseDiagnosticsPreferences(new MemoryUserPreferencesStore());
                workspace.Context.ToggleDiagnosticsCommand.Execute(null);
                window.Height = 780;
                foreach (var (name, page) in new[]
                {
                    ("14-progress-populated", WorkspacePage.Progress),
                    ("16-diagnostics-filled", WorkspacePage.Diagnostics),
                })
                {
                    workspace.CurrentPage = page;
                    PageScreenshots.SaveAll(window, folder, name);
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
        SampleOverview.AssertCaptureStopCounts(overview, "overview and timing");
        fake.OverviewCompletesWith(overview);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            SampleEvidence.Timing(assessment, request.By, request.Rule, request.ExplicitWords))));
        fake.AssessCompletesWith(assessment with
        {
            Measurements = [new ProducedAssessmentReference("assessment/one", "ParseTime", "assessment/one")],
        });
    }

}
