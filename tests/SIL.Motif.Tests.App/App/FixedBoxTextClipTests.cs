using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>Text and controls stay within their measured boxes at the supported interface zooms.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class FixedBoxTextClipTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void DiagnosticWarningsKeepTheirTextInsideTheControlsAtEveryZoom(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            LayoutInvariantFixtures.ResetSharedState();
            var fake = new FakeCommandClient();
            var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Recorded", [], [],
                "A recorded warning with enough detail to wrap when the window is narrow.");
            fake.StoredGrammarCheckIs(new GrammarCheckResponse([warning], HasBaseline: true));
            var context = WorkspaceContextTests.NewContext(fake);
            context.Diagnostics = true;
            var model = new DiagnosticsPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 640, Height = 600, Content = page };
            try
            {
                WindowZoomPolicy.Apply(Application.Current!, zoom);
                window.Show();
                PageScreenshots.Settle(window);
                var list = page.FindControl<ListBox>("DiagnosticWarningRows")!;
                Assert.Single(list.Items);
                Assert.Empty(FixedBoxClips.Find(page, $"diagnostics@{zoom}", output));
            }
            finally
            {
                LayoutInvariantFixtures.ResetSharedState();
                window.Close();
                await context.StopProjectWorkAsync();
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
