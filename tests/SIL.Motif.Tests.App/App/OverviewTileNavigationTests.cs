using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewTileNavigationTests
{
    [Fact]
    public void WaitingProposalNextRowOpensProposals()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                var progress = workspace.PageModel<ProgressPageModel>();
                progress.WaitingProposals = 1;
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var next = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                    Avalonia.Automation.AutomationProperties.GetName(button) == progress.WaitingProposalsText);
                HeadlessClick.Click(window, next, progress.WaitingProposalsText);

                Assert.Equal(WorkspacePage.Proposals, workspace.CurrentPage);
            }
            finally
            {
                window.Close();
                workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }
}
