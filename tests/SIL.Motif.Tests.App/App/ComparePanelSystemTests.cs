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
            var panel = new ComparePanel(compare);
            var window = new Window
            {
                Content = panel,
                RequestedThemeVariant = ThemeVariant.Light,
                Width = 1036,
                Height = 660,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                TestFontScale.ApplyMacLineHeight(window);
                window.UpdateLayout();
                var matrix = Assert.Single(window.GetVisualDescendants().OfType<StackPanel>(), panel =>
                    AutomationProperties.GetName(panel) == "Compare matrix");
                var list = Assert.Single(window.GetVisualDescendants().OfType<ListBox>(), box =>
                    AutomationProperties.GetName(box) == "Words in the chosen cells");
                var first = list.GetVisualDescendants().OfType<WordRow>()
                    .OrderBy(row => row.TranslatePoint(default, window)!.Value.Y).First();
                var bottom = first.TranslatePoint(new Point(0, first.Bounds.Height), window)!.Value.Y;
                var headroom = window.Bounds.Height - bottom;
                var shortModeIsOn = panel.Classes.Contains("shortMatrix");
                var shortModeDiagnostic =
                    $"Compare panel height seen by short-mode check: {panel.ShortMatrixCheckedHeight:0.#} px; " +
                    $"short mode: {(shortModeIsOn ? "on" : "off")} (threshold: below 720 px).";
                Assert.True(shortModeIsOn,
                    $"Short Matrix mode should be active in a {window.Bounds.Height:0.#} px window. " +
                    shortModeDiagnostic);
                Assert.True(headroom >= 32,
                    $"The first word row ends at {bottom:0.#} px in a {window.Bounds.Height:0.#} px window. " +
                    $"Matrix height: {matrix.Bounds.Height:0.#} px; headroom below the row: {headroom:0.#} px " +
                    "(need at least 32 px, including 8 px of layout margin). " +
                    shortModeDiagnostic);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
