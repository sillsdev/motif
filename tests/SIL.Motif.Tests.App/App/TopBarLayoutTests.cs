using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Xunit;
using static SIL.Motif.Tests.App.AnalyzeTextsLayoutTests;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the top bar's freshness line gives way to the buttons beside it: when the bar runs short, as it does
/// at 1040 px while Parse all words shows its progress, the line shortens rather than running under Help.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TopBarLayoutTests
{
    [Fact]
    public void TheFreshnessLineEndsBeforeTheTopBarsButtons()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, window) = await PageScreenshots.OpenOverSampleData(parse: true);
            try
            {
                window.Width = 760;
                Settle(window);
                var line = window.FindControl<Control>("FreshnessLine")!;
                var help = window.FindControl<Button>("HelpButton")!;
                var detail = line.GetVisualDescendants().OfType<TextBlock>().Last(text => text.Classes.Contains("freshDetail"));
                var detailRight = detail.TranslatePoint(new Point(detail.Bounds.Width, 0), window)!.Value.X;
                var helpLeft = help.TranslatePoint(default, window)!.Value.X;
                Assert.True(detailRight <= helpLeft, $"The freshness detail ends at {detailRight}, past Help at {helpLeft}.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }
}
