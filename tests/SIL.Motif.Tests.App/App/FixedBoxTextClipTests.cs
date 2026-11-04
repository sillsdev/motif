using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>Andika's line box is 1.611 em, so text in a box of fixed height can need more than the box gives it.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class FixedBoxTextClipTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void ReviewRowsKeepTheirTextAndTicksInsideTheFixedRowAtEveryZoom(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            LayoutInvariantFixtures.ResetSharedState();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(ReviewDenseTableTests.MixedChanges());
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                var page = workspace.PageModel<ReviewPageModel>();
                await page.LoadReviewAsync();
                window.Width = 1920;
                window.Height = 1080;
                WindowZoomPolicy.Apply(Application.Current!, zoom);
                PageScreenshots.Settle(window);
                foreach (var entry in page.TableEntries.OfType<ReviewListEntryViewModel>().Where(entry => entry.Review is not null))
                    entry.Review!.IsLookedAt = true;
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var rows = panel.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("denseTableRow")).ToArray();
                Assert.Equal(2, rows.Length);
                Assert.Equal(2, rows.SelectMany(row => row.GetVisualDescendants().OfType<TextBlock>())
                    .Count(text => text.Text == "✓" && text.IsEffectivelyVisible && text.Classes.Contains("denseTableCount")));
                Assert.Empty(FixedBoxClips.Find(panel, $"review@{zoom}", output));
            }
            finally
            {
                LayoutInvariantFixtures.ResetSharedState();
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(100, "listed-evidence", "ParsimonyRowsItems")]
    [InlineData(150, "listed-evidence", "ParsimonyRowsItems")]
    [InlineData(100, "suppressed-with-reason", "ParsimonySuppressedItems")]
    [InlineData(150, "suppressed-with-reason", "ParsimonySuppressedItems")]
    public void ParsimonyRowsKeepTheirTextInsideTheirRowsAtEveryZoom(int zoom, string state, string listName)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            LayoutInvariantFixtures.ResetSharedState();
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, state));
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var page = workspace.PageModel<ParsimonyPageModel>();
                if (state.StartsWith("suppressed", StringComparison.Ordinal)) page.IsShowingSuppressed = true;
                window.Width = 1920;
                window.Height = 1080;
                WindowZoomPolicy.Apply(Application.Current!, zoom);
                PageScreenshots.Settle(window);
                var list = Assert.Single(window.GetVisualDescendants().OfType<ListBox>(), box => box.Name == listName);
                Assert.Contains(list.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("denseTableRow"));
                Assert.Empty(FixedBoxClips.Find(list, $"parsimony-{state}@{zoom}", output));
            }
            finally
            {
                LayoutInvariantFixtures.ResetSharedState();
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }
}

internal static class FixedBoxClips
{
    internal static IReadOnlyList<string> Find(Visual root, string scene, ITestOutputHelper output)
    {
        var clips = new List<string>();
        var measured = new List<string>();
        foreach (var text in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!text.IsEffectivelyVisible || string.IsNullOrEmpty(text.Text) || text.Bounds.Height <= 0) continue;
            var box = text.GetVisualAncestors().OfType<Layoutable>().FirstOrDefault(IsBox);
            if (box is null) continue;
            var top = text.TranslatePoint(default, box)!.Value.Y;
            var bottom = top + text.Bounds.Height;
            var line = text.TextLayout.Height;
            var verdict = top < -Tolerance() || bottom > box.Bounds.Height + Tolerance() ||
                line > text.Bounds.Height + Tolerance() ? "CLIPS" : "ok";
            measured.Add($"{scene} {verdict} '{text.Text}' [{string.Join(' ', text.Classes)}] line={line:0.#} " +
                $"arranged={text.Bounds.Height:0.#} y={top:0.#}..{bottom:0.#} in {box.GetType().Name}[{string.Join(' ', (box as StyledElement)?.Classes ?? [])}] h={box.Bounds.Height:0.#}");
            output.WriteLine(measured[^1]);
            if (verdict == "CLIPS") clips.Add($"{scene}: '{text.Text}' needs {line:0.#} px, has {text.Bounds.Height:0.#} px at {top:0.#}..{bottom:0.#} in a {box.Bounds.Height:0.#} px box");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "test-results");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, $"fixed-box-text-{scene}.txt"), measured);
        return clips;

        static double Tolerance() => 0.25;
    }

    // The box that sizes or clips the text: a dense-table row or header, or a button.
    private static bool IsBox(Layoutable box) =>
        box is Button || box is Border border && border.Classes.Any(name => name.StartsWith("denseTable", StringComparison.Ordinal));
}
