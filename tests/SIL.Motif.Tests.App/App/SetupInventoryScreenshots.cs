using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class SetupInventoryScreenshots
{
    [Fact]
    public void SetupTextCountsMatchThePublishedBaselineAndSelection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, composeWindow: false);
            try
            {
                var inventory = TextInventoryQuery.Query(new(workspace.Context.ProjectPath!));
                Assert.True(inventory.Succeeded, inventory.Refusal?.Message);
                Assert.NotNull(inventory.Value);
                Assert.True(inventory.Value.HasBaseline);
                Assert.Equal(new[] { 3, 6 }, inventory.Value.Texts.Select(text => text.WordCount).Order());
                var choices = workspace.Selection.Texts.ToArray();
                Assert.Equal(inventory.Value.Texts.Select(text => text.Id).Order(),
                    choices.Select(text => text.Id).Order());
                foreach (var text in inventory.Value.Texts)
                {
                    var choice = Assert.Single(choices, choice => choice.Id == text.Id);
                    Assert.Equal(text.WordCount, choice.WordCount);
                    Assert.Equal(text.InterlinearizedWordCount, choice.InterlinearizedWordCount);
                    Assert.Equal(text.InterlinearizationPercent, choice.InterlinearizationPercent);
                }
                Assert.Equal(new[] { "3 distinct", "6 distinct" },
                    choices.Select(choice => choice.DistinctCountText).Order());
                workspace.CurrentPage = WorkspacePage.Texts;
                await workspace.Context.EvidencePublication;
                await workspace.Context.SelectionReads.Pending;
                var summary = workspace.Context.SelectionReads.Summary;
                Assert.NotNull(summary);
                Assert.Equal(9, summary.SourcePositions.Select(position => position.Word.Form)
                    .Distinct(StringComparer.Ordinal).Count());
                Assert.Equal(9, summary.SourcePositions.Count);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureSetupTextInventoryCounts()
    {
        var root = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                var setup = workspace.Context.Setup!;
                setup.Step = 1;
                setup.IsOpen = true;
                PageScreenshots.Settle(window);
                var dialog = Assert.IsType<SetupDialog>(window.FindInWindowOrWorkspace<SetupDialog>("SetupDialogView"));
                Assert.True(dialog.IsEffectivelyVisible);
                Assert.Same(setup, dialog.DataContext);
                Assert.All(workspace.Selection.Texts, text => Assert.True(text.IsChecked));
                var path = Path.Combine(root, "setup-inventory", "selection-counts.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                MatrixCaptureState.SaveFrame(window, workspace, WorkspacePage.Texts,
                    TextsTab.AnalyzeTexts, path);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }
}
