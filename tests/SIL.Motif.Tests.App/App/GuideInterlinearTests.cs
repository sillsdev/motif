using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Guide;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using static SIL.Motif.Tests.App.GrammarGuideTestSupport;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GuideInterlinearTests
{
    private static readonly string[] Swahili =
    [
        "Before.", "word: nilisoma kitabu", "morphemes: ni-li-som-a kitabu", "gloss: 1SG-PST-read-FV book", "translation: I read a book.",
    ];

    private static readonly string[] Tamil =
    [
        "word: நான் புத்தகம் படித்தேன்", "morphemes: நான் புத்தகம் படி-த்த்-ஏன்", "gloss: 1SG book read-PST-1SG", "translation: I read the book.",
    ];

    [Fact]
    public void ABlockIsShownAsAMonospaceGridWithItsColumnsAligned()
    {
        var model = new GuideInterlinearViewModel();

        model.Show(Swahili);

        Assert.True(model.IsShown);
        Assert.False(model.HasProblems);
        var rows = model.GridText.Split('\n');
        Assert.Equal(4, rows.Length);
        Assert.Equal(rows[1].IndexOf("kitabu", StringComparison.Ordinal), rows[0].IndexOf("kitabu", StringComparison.Ordinal));
        Assert.Equal(rows[1].IndexOf("kitabu", StringComparison.Ordinal), rows[2].IndexOf("book", StringComparison.Ordinal));
        Assert.Equal("I read a book.", rows[3]);
    }

    [Fact]
    public void ATamilBlockIsShownWithItsScriptIntact()
    {
        var model = new GuideInterlinearViewModel();

        model.Show(Tamil);

        Assert.Contains("புத்தகம்", model.GridText, StringComparison.Ordinal);
        Assert.Contains("படி-த்த்-ஏன்", model.GridText, StringComparison.Ordinal);
    }

    [Fact]
    public void ASectionWithoutABlockShowsNothingAndAMismatchIsSaidUnderTheGrid()
    {
        var model = new GuideInterlinearViewModel();
        model.Show(["Just prose."]);
        Assert.False(model.IsShown);

        model.Show(["word: a b", "morphemes: a", "gloss: x y"]);
        Assert.True(model.IsShown);
        Assert.True(model.HasProblems);

        model.Clear();
        Assert.False(model.IsShown);
        Assert.False(model.HasProblems);
    }

    [Fact]
    public void TheGuidePageShowsTheGridOfTheSelectedSection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var shown = Show(SectionA, Swahili.Select(line => Paragraph("Normal", "p-" + line.GetHashCode(), Plain(line))).ToArray());
            var context = WorkspaceContextTests.NewContext(Fake(Hierarchy, _ => shown));
            var model = new GrammarGuidePageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new GrammarGuidePage(model);
            var window = new MotifTestWindow { Width = 1100, Height = 700, Content = page };
            try
            {
                window.Show();
                model.SelectedSection = model.Parts[0];
                PageScreenshots.Settle(window);

                var view = page.GetVisualDescendants().OfType<GuideInterlinear>().Single();
                Assert.True(view.IsEffectivelyVisible);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.Interlinear.GridText);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }
}
