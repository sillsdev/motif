using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.ControlContracts;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class StagedUndoStripTests
{
    [Fact]
    public void ClickingUndoOnAStagedChangeInAStripRemovesTheChange()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var scenes = await TooltipScenes.Open();
            try
            {
                await scenes.Reach(TooltipScene.ReaderStaged);
                var changes = scenes.Workspace.PageModel<TextsPageModel>().ResultsInText.Changes;
                Assert.NotEmpty(changes.Items);
                var undo = scenes.Window.GetVisualDescendants().OfType<Button>().First(button =>
                    button.IsEffectivelyVisible && button.Content is string label && label == MotifAppStrings.Undo &&
                    button.GetVisualAncestors().OfType<Border>().Any(border => border.Classes.Contains("stagedStrip")));
                Assert.NotNull(undo.Command);

                var invoke = Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(undo));
                invoke.Invoke();
                for (var pass = 0; pass < 100 && changes.Items.Count > 0; pass++)
                {
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.Empty(changes.Items);
            }
            finally
            {
                scenes.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
