using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ComparePanelSystemTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void TheChosenWordListStartsWithAWholeRowAt1240Pixels()
    {
        avalonia.Invoke(() =>
        {
            var compare = CompareViewModelTests.LostWords();
            compare.Toggle(compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved &&
                cell.Column == CompareColumnKind.NoParse), additive: false);
            var window = new Window
            {
                Content = new ComparePanel(compare),
                RequestedThemeVariant = ThemeVariant.Light,
                Width = 1036,
                Height = 660,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var list = Assert.Single(window.GetVisualDescendants().OfType<ListBox>(), box =>
                    AutomationProperties.GetName(box) == "Words in the chosen cells");
                var first = list.GetVisualDescendants().OfType<WordRow>()
                    .OrderBy(row => row.TranslatePoint(default, window)!.Value.Y).First();
                var bottom = first.TranslatePoint(new Point(0, first.Bounds.Height), window)!.Value.Y;
                Assert.True(bottom <= window.Bounds.Height,
                    $"The first word row ends at {bottom:0.#} px below the {window.Bounds.Height:0.#} px window.");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
