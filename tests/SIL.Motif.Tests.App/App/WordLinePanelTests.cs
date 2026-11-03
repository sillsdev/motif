using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.TextViewParity;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using WritingSystemText = SIL.Motif.App.Controls.WritingSystemText;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the rounding of <see cref="WordLinePanel.WordBand"/>: the offset to the tallest sibling, not whether it covers a sibling.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordLinePanelTests
{
    [Fact]
    public void TheNextRowStartsAtTheDrawnWordsCentredOffsetFromTheTallestSiblingRoundedUpToWholePixels() =>
        AvaloniaHeadlessPlatform.Invoke(() =>
        {
            WalkthroughFonts.Register();
            var sample = TextViewSample.Named("ltr");
            var styles = new TextStyles();
            styles.SetFallbackFamilies([WalkthroughFonts.DejaVuSansFamily]);
            WritingSystemDisplay Display(string id) => new(id, id, id, WritingSystemKind.Vernacular, 0, false,
                WalkthroughFonts.DejaVuSansFamily, string.Empty, false, new Dictionary<string, double> { ["Normal"] = 12 });
            styles.ReplaceContext([Display(sample.FormWritingSystem), Display(sample.GlossWritingSystem)], "en");
            var word = new InterlinearWord { Data = sample.Token(0) };
            var panel = new WordLinePanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            panel.Children.Add(word);
            var window = new MotifTestWindow { Content = panel, Width = 400, Height = 200 };
            WritingSystemText.SetResolver(window, styles);
            try
            {
                window.Show();
                PageScreenshots.SettleLayout(window);
                var drawn = word.LineMetrics.Word;

                panel.Children.Add(new Border { Width = 8, Height = drawn + 5 });
                panel.InvalidateMeasure();
                PageScreenshots.SettleLayout(window);

                Assert.Equal(drawn, word.LineMetrics.Word);
                Assert.Equal(drawn + 3, panel.WordBand);
            }
            finally { window.Close(); }
        });
}
