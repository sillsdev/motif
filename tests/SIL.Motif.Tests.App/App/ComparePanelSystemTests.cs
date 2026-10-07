using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

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
            const double largeFontScale = 1.25;
            window.Resources["Intent.Type.Section"] = 16 * largeFontScale;
            window.Resources["Intent.Type.Label"] = 11 * largeFontScale;
            try
            {
                window.Show();
                window.UpdateLayout();
                var matrix = Assert.Single(window.GetVisualDescendants().OfType<StackPanel>(), panel =>
                    AutomationProperties.GetName(panel) == "Compare matrix");
                var matrixCells = matrix.GetVisualDescendants().OfType<MatrixCell>().ToArray();
                Assert.NotEmpty(matrixCells);
                var panelMatrixCells = panel.GetVisualDescendants().OfType<MatrixCell>().ToArray();
                Assert.Equal(matrixCells.Length, panelMatrixCells.Length);
                var matrixCellLabels = matrix.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.Classes.Contains("matrixCellLabel")).ToArray();
                Assert.NotEmpty(matrixCellLabels);
                Assert.All(matrixCellLabels, label => Assert.False(label.IsEffectivelyVisible));
                Assert.All(matrixCells, cell =>
                {
                    Assert.False(cell.ShowsDetails);
                    var surface = Assert.Single(cell.GetVisualDescendants().OfType<Border>(), border =>
                        border.Classes.Contains("matrixCell"));
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(surface)));
                });
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
                var rowParts = string.Join("; ", first.GetVisualDescendants().OfType<Control>()
                    .Where(control => control.Classes.Any(className =>
                        className.StartsWith("wordPresentation", StringComparison.Ordinal)))
                    .Select(control => $"{control.GetType().Name}.{control.Classes.First(className =>
                        className.StartsWith("wordPresentation", StringComparison.Ordinal))} " +
                        $"{control.Bounds.Width:0.#}×{control.Bounds.Height:0.#}"));
                Assert.True(shortModeIsOn,
                    $"Short Matrix mode should be active in a {window.Bounds.Height:0.#} px window. " +
                    shortModeDiagnostic);
                Assert.True(headroom >= 32,
                    $"The first word row ends at {bottom:0.#} px in a {window.Bounds.Height:0.#} px window. " +
                    $"Matrix height: {matrix.Bounds.Height:0.#} px; headroom below the row: {headroom:0.#} px " +
                    $"(need at least 32 px, including 8 px of layout margin). Row {first.Bounds.Width:0.#}×" +
                    $"{first.Bounds.Height:0.#} at y={first.TranslatePoint(default, window)!.Value.Y:0.#}. " +
                    $"Parts: {rowParts}. " +
                    shortModeDiagnostic);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
