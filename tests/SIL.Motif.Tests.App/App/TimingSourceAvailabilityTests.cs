using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingSourceAvailabilityTests
{
    [Fact]
    public void DiagnosticsShowsStoredRuleWorkWithoutTheRetiredMatrixSelectors()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            try
            {
                workspace.Context.Diagnostics = true;
                workspace.Context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
                await workspace.Context.EvidencePublication;
                PageScreenshots.Settle(window);
                var page = window.PageView<SIL.Motif.App.Views.DiagnosticsPage>();
                Assert.NotEmpty(Assert.IsType<DiagnosticsPageModel>(page.DataContext).SpeedByRule.Rows);
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button =>
                    Avalonia.Automation.AutomationProperties.GetName(button) is
                        "Words from a Matrix cell" or "Words from a list" or "More ways to choose words");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
