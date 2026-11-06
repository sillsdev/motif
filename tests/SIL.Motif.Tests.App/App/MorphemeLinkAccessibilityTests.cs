using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MorphemeLinkAccessibilityTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void HoverLinksNameTheirOwnMorpheme()
    {
        avalonia.Invoke(() =>
        {
            var panel = new MorphemePanel
            {
                RevealLinks = true,
                Morphs = [new ParserReadingMorphViewModel(new ParserReadingMorph("ki-", "class", "n", null, false,
                    "silfw://localhost/link?database%3dp%26tool%3dlexiconEdit%26guid%3dx%26tag%3d"))],
            };
            panel.Measure(Size.Infinity);
            var link = Assert.Single(panel.GetVisualDescendants().OfType<HyperlinkButton>());
            Assert.Equal("Edit \"ki-\" in FieldWorks", AutomationProperties.GetName(link));
            Assert.Equal("Edit \"ki-\" in FieldWorks", ToolTip.GetTip(link));
        });
    }
}
