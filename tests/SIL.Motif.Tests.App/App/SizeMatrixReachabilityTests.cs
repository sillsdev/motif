using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class SizeMatrixReachabilityTests
{
    [Fact]
    public void EveryPageKeepsControlsReachableAtSmallAndScaledSizes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var sizes = MatrixSizes.Read().ToDictionary(size => size.Name, StringComparer.Ordinal);
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                var sizesToInspect = new[] { ("thin", 100), ("thin", 150), ("half", 150) };
                var selection = new MatrixCaptureSelection(
                    PageScreenshots.Views().Select(view => view.Name).ToHashSet(StringComparer.Ordinal),
                    sizesToInspect.Select(point => new MatrixSelectionPoint(point.Item1, point.Item2, "light")).ToArray(),
                    LayoutCheckNames.ReachabilityAndTitleBar);
                var allFindings = new List<LayoutFinding>();
                foreach (var (scene, page, tab) in PageScreenshots.Views())
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    if (page == WorkspacePage.TryAWord) await PageScreenshots.TryTheSampleWord(workspace);
                    workspace.CurrentPage = page;
                    if (page == WorkspacePage.Texts && tab == TextsTab.AnalyzeTexts)
                        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);

                    foreach (var (sizeName, zoom) in sizesToInspect)
                    {
                        var size = sizes[sizeName];
                        SizeMatrixScreenshots.ApplySize(window, size, zoom);
                        PageScreenshots.Settle(window);
                        Assert.True(Math.Abs(window.ClientSize.Width - size.Width) < 1,
                            $"Expected client width {size.Width}, got {window.ClientSize.Width} after resize.");
                        Assert.True(Math.Abs(window.ClientSize.Height - size.Height) < 1,
                            $"Expected client height {size.Height}, got {window.ClientSize.Height} after resize.");
                        var context = new LayoutFindingContext(scene, size.Name, zoom, "light");
                        var findings = ReachabilityAssertions.InspectReachabilityAndTitleBar(window, context);
                        allFindings.AddRange(findings);
                    }
                }

                ExpectedMatrixFindings.AssertNoUnexpected(allFindings, requireAllExpected: true, selection);
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(4));
    }
}
