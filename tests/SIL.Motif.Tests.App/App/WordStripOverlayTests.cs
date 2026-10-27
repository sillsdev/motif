using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Views;
using Xunit;
using static SIL.Motif.Tests.App.AnalyzeTextsLayoutTests;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordStripOverlayTests
{
    [Fact]
    public void OneSetOfDispositionButtonsFollowsTheFocusedWordAndRestingWordsShowFaces()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, window) = await OpenAnalyzeTexts(seedReaderToken: false);
            try
            {
                var panel = Panel(window);
                var strips = Strips(panel).Cast<WordStripToken>().ToArray();
                Assert.True(strips.Length >= 2);
                Assert.Empty(panel.GetVisualDescendants().OfType<WordDispositionButtons>()
                    .Where(buttons => buttons.GetVisualAncestors().OfType<WordStripToken>().Any()));
                Assert.All(strips, strip => Assert.NotEmpty(strip.GetVisualDescendants().OfType<DispositionFace>()));

                strips[0].FocusWord();
                Settle(window);
                Assert.Single(strips[0].GetVisualDescendants().OfType<WordDispositionButtons>());
                Assert.Empty(strips[0].GetVisualDescendants().OfType<DispositionFace>());

                strips[1].FocusWord();
                Settle(window);
                Assert.Empty(strips[0].GetVisualDescendants().OfType<WordDispositionButtons>());
                Assert.NotEmpty(strips[0].GetVisualDescendants().OfType<DispositionFace>());
                Assert.Single(strips[1].GetVisualDescendants().OfType<WordDispositionButtons>());
                Assert.Single(panel.GetVisualDescendants().OfType<WordDispositionButtons>()
                    .Where(buttons => buttons.GetVisualAncestors().OfType<WordStripToken>().Any()));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }
}
