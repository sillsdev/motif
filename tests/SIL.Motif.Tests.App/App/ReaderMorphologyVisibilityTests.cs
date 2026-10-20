using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReaderMorphologyVisibilityTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void ReaderShowsCompletePrimaryMorphologyBeforeOpeningAWordCard() =>
        avalonia.Invoke(() => CheckCompleteMorphology(false));

    /// <summary>Checks a four-morpheme stored reading and a differing five-morpheme parser reading at four sizes.</summary>
    internal static void CheckCompleteMorphology(bool capture)
    {
        var result = PageScreenshots.Assessment().Words.Single(word => word.Word == "alikula");
        var stored = result.StoredAnalyses.Select(reading => new ProjectAnalysis(reading.StoredAnalysisId!, reading.Morphs)
        {
            StoredAnalysisId = reading.StoredAnalysisId,
            StoredAnalysisOpinion = reading.StoredAnalysisOpinion,
            Identity = reading.Identity,
        }).ToArray();
        using var token = new ResultsTokenViewModel("Primary morphology", 1,
            new TextToken("alikula", "alikula", null, stored[0].StoredAnalysisOpinion)
            {
                Analysis = stored[0],
                StoredAnalyses = stored,
            }, result);
        var strip = new WordStripToken { Data = token, DataContext = token, VerticalAlignment = VerticalAlignment.Top };
        var window = new MotifTestWindow { Content = strip };
        try
        {
            window.Show();
            window.MouseMove(new Point(-10, -10));
            var failures = new List<Exception>();
            foreach (var (name, width, height) in new[]
                     { ("thin", 640, 600), ("half", 960, 780), ("full", 1920, 1080), ("short", 960, 600) })
            {
                window.Width = width;
                window.Height = height;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                if (capture)
                {
                    var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
                    Directory.CreateDirectory(folder);
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException($"No frame rendered for {name}.");
                    frame.Save(Path.Combine(folder, $"reader-morphology-{name}.png"), PngBitmapEncoderOptions.Default);
                }
                try
                {
                    Assert.False(token.PresentationState.IsOpen);
                    Assert.True(token.HasMorePanGlossReadings);
                    Assert.True(strip.FindControl<Control>("PanGlossLine")!.IsEffectivelyVisible);
                    AssertComplete(strip.FindControl<MorphemePanel>("FieldWorksMorphology")!, token.PrimaryFieldWorksMorphs);
                    AssertComplete(strip.FindControl<MorphemePanel>("PanGlossMorphology")!, token.PanGlossReadingMorphs);
                    LayoutAssertions.AssertCurrent(window);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Select(failure => failure.Message)));
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertComplete(MorphemePanel panel, IReadOnlyList<ParserReadingMorphViewModel> morphs)
    {
        Assert.True(panel.IsEffectivelyVisible);
        Assert.True(morphs.Count > 2);
        var text = panel.GetVisualDescendants().OfType<CopyableTextBlock>().ToArray();
        Assert.Equal(morphs.Select(morph => morph.Form),
            text.Where(item => item.Classes.Contains("morphemePanelForm")).Select(item => item.Text));
        Assert.Equal(morphs.Select(morph => morph.GlossOrPlaceholder),
            text.Where(item => item.Classes.Contains("morphemePanelGloss")).Select(item => item.Text));
        Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("morphemePanelOverflow"));
    }
}

/// <summary>Records complete primary reader morphology while keeping alternate readings in the word card.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ReaderMorphologyScreenshots(AvaloniaHeadlessFixture avalonia)
{
    [ScreenshotFact]
    public void CapturePrimaryReaderMorphologyAtFourSizes() =>
        avalonia.Invoke(() => ReaderMorphologyVisibilityTests.CheckCompleteMorphology(true));
}
